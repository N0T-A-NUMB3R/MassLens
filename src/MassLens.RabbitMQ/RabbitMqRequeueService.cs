using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MassLens.Core;

namespace MassLens.RabbitMQ;

public sealed class RabbitMqRequeueOptions
{
    public string Host     { get; set; } = "localhost";
    public int    Port     { get; set; } = 15672;
    public string VHost    { get; set; } = "/";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public bool   Ssl      { get; set; } = false;
}

public sealed class RabbitMqRequeueService : RequeueService
{
    private readonly HttpClient          _http;
    private readonly RabbitMqRequeueOptions _opts;
    private readonly string              _baseUrl;

    public RabbitMqRequeueService(MassTransit.IBus bus, RabbitMqRequeueOptions opts) : base(bus)
    {
        _opts    = opts;
        _baseUrl = $"{(opts.Ssl ? "https" : "http")}://{opts.Host}:{opts.Port}/api";

        _http = new HttpClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.Username}:{opts.Password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<int> RequeueFromDlqAsync(
        string dlqName,
        string targetQueue,
        int count,
        string user,
        CancellationToken ct = default)
    {
        var vhost   = Uri.EscapeDataString(_opts.VHost);
        var reqBody = new
        {
            count,
            ackmode    = "ack_requeue_false",
            encoding   = "auto",
            truncate   = 50_000
        };

        var json     = JsonSerializer.Serialize(reqBody);
        var content  = new StringContent(json, Encoding.UTF8, "application/json");
        var url      = $"{_baseUrl}/queues/{vhost}/{Uri.EscapeDataString(dlqName)}/get";

        var response = await _http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        var raw      = await response.Content.ReadAsStringAsync(ct);
        var messages = JsonDocument.Parse(raw).RootElement.EnumerateArray().ToList();

        foreach (var msg in messages)
        {
            var publishUrl  = $"{_baseUrl}/exchanges/{vhost}/amq.default/publish";
            var publishBody = new
            {
                properties       = msg.TryGetProperty("properties", out var props) ? props : (object)new { },
                routing_key      = targetQueue,
                payload          = msg.TryGetProperty("payload", out var p) ? p.GetString() : "",
                payload_encoding = "string"
            };

            var publishContent = new StringContent(JsonSerializer.Serialize(publishBody), Encoding.UTF8, "application/json");
            await _http.PostAsync(publishUrl, publishContent, ct);
        }

        MessageStore.Instance.AppendAudit(new AuditEntry
        {
            Action    = "RabbitMQ DLQ Requeue",
            Detail    = $"{messages.Count} messages from {dlqName} → {targetQueue}",
            User      = user,
            Timestamp = DateTimeOffset.UtcNow
        });

        return messages.Count;
    }

    public async Task<List<RabbitMqQueueInfo>> GetQueuesAsync(CancellationToken ct = default)
    {
        var vhost    = Uri.EscapeDataString(_opts.VHost);
        var response = await _http.GetAsync($"{_baseUrl}/queues/{vhost}", ct);
        response.EnsureSuccessStatusCode();

        var raw  = await response.Content.ReadAsStringAsync(ct);
        var doc  = JsonDocument.Parse(raw);
        var list = new List<RabbitMqQueueInfo>();

        foreach (var q in doc.RootElement.EnumerateArray())
        {
            list.Add(new RabbitMqQueueInfo
            {
                Name        = q.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Messages    = q.TryGetProperty("messages", out var m) ? m.GetInt32() : 0,
                Consumers   = q.TryGetProperty("consumers", out var c) ? c.GetInt32() : 0,
                MessageRate = q.TryGetProperty("messages_details", out var d)
                    && d.TryGetProperty("rate", out var r) ? r.GetDouble() : 0
            });
        }

        return list;
    }
}

public sealed class RabbitMqQueueInfo
{
    public string Name        { get; init; } = "";
    public int    Messages    { get; init; }
    public int    Consumers   { get; init; }
    public double MessageRate { get; init; }
}
