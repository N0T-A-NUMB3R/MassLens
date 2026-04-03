using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MassLens.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MassLens.Middleware;

internal sealed class MassLensMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MassLensOptions _options;
    private readonly byte[] _dashboardHtml;
    private readonly byte[]? _logoBytes;

    // SSE connection counter
    private int _sseConnections;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters             = { new JsonStringEnumConverter() }
    };

    public MassLensMiddleware(RequestDelegate next, MassLensOptions options)
    {
        _next          = next;
        _options       = options;
        _dashboardHtml = LoadEmbeddedHtml();
        _logoBytes     = LoadEmbeddedLogo();
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path  = ctx.Request.Path.Value ?? "";
        var base_ = _options.BasePath.TrimEnd('/');

        if (!path.StartsWith(base_, StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var env = ctx.RequestServices.GetService<IHostEnvironment>();
        if (_options.DisableInProduction && env?.IsProduction() == true)
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        if (!await AuthorizeAsync(ctx))
            return;

        var sub = path[base_.Length..].TrimStart('/');

        if (sub is "" or "index.html")
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await ctx.Response.Body.WriteAsync(_dashboardHtml);
            return;
        }

        if (sub == "stream")
        {
            await HandleSseAsync(ctx);
            return;
        }

        if (sub == "snapshot")
        {
            await WriteJsonAsync(ctx, MessageStore.Instance.GetSnapshot());
            return;
        }

        if (sub.StartsWith("inject", StringComparison.OrdinalIgnoreCase))
        {
            if (_options.ReadOnly) { ctx.Response.StatusCode = 403; return; }
            await HandleInjectAsync(ctx);
            return;
        }

        if (sub == "saga/export")
        {
            await HandleSagaExportAsync(ctx);
            return;
        }

        if (sub == "logo" && _logoBytes is not null)
        {
            ctx.Response.ContentType = "image/png";
            ctx.Response.Headers["Cache-Control"] = "public, max-age=86400";
            await ctx.Response.Body.WriteAsync(_logoBytes);
            return;
        }

        if (sub.StartsWith("trace", StringComparison.OrdinalIgnoreCase))
        {
            await HandleTraceAsync(ctx);
            return;
        }

        ctx.Response.StatusCode = 404;
    }

    private async Task<bool> AuthorizeAsync(HttpContext ctx)
    {
        if (_options.AllowedIPs.Length > 0)
        {
            var remote  = ctx.Connection.RemoteIpAddress;
            var allowed = remote is not null && IsAllowedIp(remote, _options.AllowedIPs);

            if (!allowed)
            {
                ctx.Response.StatusCode = 403;
                return false;
            }
        }

        if (_options.HeaderToken is { Length: > 0 } token)
        {
            var header = ctx.Request.Headers["X-MassLens-Token"].FirstOrDefault() ?? "";
            if (!TimingSafeEqual(header, token))
            {
                ctx.Response.StatusCode = 401;
                return false;
            }
        }

        if (_options.AuthorizationPolicy is { Length: > 0 } policy)
        {
            var auth   = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
            var result = await auth.AuthorizeAsync(ctx.User, policy);
            if (!result.Succeeded)
            {
                ctx.Response.StatusCode = 403;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks whether <paramref name="remote"/> is covered by any entry in <paramref name="allowedIPs"/>.
    /// Each entry may be an exact IP (e.g. "192.168.1.5") or a CIDR block (e.g. "10.0.0.0/8").
    /// Loopback addresses (127.x / ::1) are always allowed when the list is non-empty.
    /// </summary>
    private static bool IsAllowedIp(IPAddress remote, string[] allowedIPs)
    {
        if (IPAddress.IsLoopback(remote)) return true;

        // Normalise IPv4-mapped IPv6 (::ffff:x.x.x.x) to plain IPv4
        if (remote.IsIPv4MappedToIPv6)
            remote = remote.MapToIPv4();

        foreach (var entry in allowedIPs)
        {
            var slash = entry.IndexOf('/');
            if (slash < 0)
            {
                // exact match
                if (IPAddress.TryParse(entry, out var exact) && exact.Equals(remote))
                    return true;
            }
            else
            {
                // CIDR match
                if (!IPAddress.TryParse(entry[..slash], out var network)) continue;
                if (!int.TryParse(entry[(slash + 1)..], out var prefix))  continue;

                if (network.IsIPv4MappedToIPv6)
                    network = network.MapToIPv4();

                if (remote.AddressFamily != network.AddressFamily) continue;

                var remBytes  = remote.GetAddressBytes();
                var netBytes  = network.GetAddressBytes();
                var totalBits = remBytes.Length * 8;
                if (prefix < 0 || prefix > totalBits) continue;

                var fullBytes = prefix / 8;
                var remBits   = prefix % 8;

                // compare full bytes
                for (int i = 0; i < fullBytes; i++)
                    if (remBytes[i] != netBytes[i]) goto next;

                // compare remaining bits
                if (remBits > 0)
                {
                    var mask = (byte)(0xFF << (8 - remBits));
                    if ((remBytes[fullBytes] & mask) != (netBytes[fullBytes] & mask)) goto next;
                }

                return true;
                next:;
            }
        }
        return false;
    }

    /// <summary>Constant-time string comparison to prevent timing attacks on the token.</summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static bool TimingSafeEqual(string a, string b)
    {
        var diff = a.Length ^ b.Length;
        var len  = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private async Task HandleSseAsync(HttpContext ctx)
    {
        if (Interlocked.Increment(ref _sseConnections) > _options.MaxSseConnections)
        {
            Interlocked.Decrement(ref _sseConnections);
            ctx.Response.StatusCode = 429;
            ctx.Response.Headers["Retry-After"] = "5";
            return;
        }

        ctx.Response.Headers["Content-Type"]      = "text/event-stream";
        ctx.Response.Headers["Cache-Control"]     = "no-cache";
        ctx.Response.Headers["Connection"]        = "keep-alive";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        var ct = ctx.RequestAborted;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var snap    = MessageStore.Instance.GetSnapshot();
                var payload = JsonSerializer.Serialize(snap, _json);
                await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Interlocked.Decrement(ref _sseConnections);
        }
    }

    private static async Task HandleInjectAsync(HttpContext ctx)
    {
        if (ctx.Request.Method != "POST")
        {
            ctx.Response.StatusCode = 405;
            return;
        }

        using var body = await JsonDocument.ParseAsync(ctx.Request.Body);
        var root = body.RootElement;

        if (!root.TryGetProperty("messageType", out var typeProp))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsync("{\"error\":\"messageType required\"}");
            return;
        }

        var messageType = typeProp.GetString() ?? "";
        var payload     = root.TryGetProperty("payload", out var p) ? p.GetRawText() : "{}";
        var user        = GetUser(ctx);

        MessageStore.Instance.Write(new MessageEntry
        {
            MessageType  = messageType,
            ConsumerType = "MassLens.Injection",
            Direction    = MessageDirection.Published,
            Timestamp    = DateTimeOffset.UtcNow
        });

        await WriteJsonAsync(ctx, new { ok = true, messageType, injectedBy = user });
    }

    private static async Task HandleTraceAsync(HttpContext ctx)
    {
        var correlationId = ctx.Request.Query["correlationId"].FirstOrDefault();

        if (string.IsNullOrEmpty(correlationId))
        {
            var recent = MessageStore.Instance.GetRecentCorrelationIds(30);
            await WriteJsonAsync(ctx, recent);
            return;
        }

        var all = MessageStore.Instance.GetTrace(correlationId);
        if (all.Length == 0) { ctx.Response.StatusCode = 404; return; }

        var entries = all
            .Where(e => e.Direction is MessageDirection.Consumed or MessageDirection.Faulted)
            .ToArray();
        if (entries.Length == 0) entries = all;

        var firstTs = entries.Min(e => e.Timestamp);
        var totalMs = (entries.Max(e => e.Timestamp) - firstTs).TotalMilliseconds
                    + entries.Max(e => e.Duration.TotalMilliseconds);

        var result = entries.Select(e => new
        {
            messageType      = e.MessageType,
            consumerType     = e.ConsumerType,
            direction        = e.Direction.ToString(),
            durationMs       = Math.Round(e.Duration.TotalMilliseconds, 1),
            offsetMs         = Math.Round((e.Timestamp - firstTs).TotalMilliseconds, 1),
            sizeBytes        = e.SizeBytes,
            exceptionType    = e.ExceptionType,
            exceptionMessage = e.ExceptionMessage,
            timestamp        = e.Timestamp,
            faulted          = e.Direction == MessageDirection.Faulted,
            widthPct         = totalMs > 0 ? Math.Round(e.Duration.TotalMilliseconds / totalMs * 100, 1) : 0,
            offsetPct        = totalMs > 0 ? Math.Round((e.Timestamp - firstTs).TotalMilliseconds / totalMs * 100, 1) : 0,
        }).OrderBy(e => e.timestamp).ToArray();

        await WriteJsonAsync(ctx, new { correlationId, totalMs = Math.Round(totalMs, 1), entries = result });
    }

    private static async Task HandleSagaExportAsync(HttpContext context)
    {
        var snapshot = MessageStore.Instance.GetSnapshot();
        var format   = context.Request.Query["format"].ToString();

        if (format == "csv")
        {
            context.Response.ContentType = "text/csv";
            context.Response.Headers.Append("Content-Disposition", "attachment; filename=sagas.csv");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SagaName,CorrelationId,CurrentState,LastSeen");
            foreach (var saga in snapshot.Sagas)
                foreach (var inst in saga.ActiveInstances)
                    sb.AppendLine($"{saga.Name},{inst.CorrelationId},{inst.State},{inst.UpdatedAt:O}");
            await context.Response.WriteAsync(sb.ToString());
        }
        else
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.Append("Content-Disposition", "attachment; filename=sagas.json");
            await context.Response.WriteAsync(JsonSerializer.Serialize(snapshot.Sagas, _json));
        }
    }

    private static async Task WriteJsonAsync<T>(HttpContext ctx, T value)
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(value, _json));
    }

    private static string GetUser(HttpContext ctx) =>
        ctx.User?.Identity?.Name
        ?? ctx.Request.Headers["X-User"].FirstOrDefault()
        ?? ctx.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";

    private static byte[] LoadEmbeddedHtml()
    {
        var asm  = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
                      .First(n => n.EndsWith("dashboard.html", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var ms     = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[]? LoadEmbeddedLogo()
    {
        var asm  = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
                      .FirstOrDefault(n => n.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;
        using var stream = asm.GetManifestResourceStream(name)!;
        using var ms     = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
