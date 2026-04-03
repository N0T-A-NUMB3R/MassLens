using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MassLens.Core;

/// <summary>
/// Background service that fires a webhook POST when the error rate exceeds the configured threshold.
/// Runs every 30 seconds. Sends at most one alert per breach (resets when rate drops below threshold).
/// </summary>
internal sealed class ThresholdNotifier : BackgroundService
{
    private readonly MassLensOptions         _options;
    private readonly IHttpClientFactory      _httpFactory;
    private readonly ILogger<ThresholdNotifier> _log;

    private long   _prevConsumed;
    private long   _prevFaulted;
    private bool   _alertFired;

    public ThresholdNotifier(
        MassLensOptions options,
        IHttpClientFactory httpFactory,
        ILogger<ThresholdNotifier> log)
    {
        _options     = options;
        _httpFactory = httpFactory;
        _log         = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.AlertWebhookUrl))
            return;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);

            var snap      = MessageStore.Instance.GetSnapshot();
            var consumed  = snap.TotalConsumed;
            var faulted   = snap.TotalFaulted;
            var deltaC    = consumed - _prevConsumed;
            var deltaF    = faulted  - _prevFaulted;
            _prevConsumed = consumed;
            _prevFaulted  = faulted;

            if (deltaC <= 0) continue;

            var rate = deltaF / (double)deltaC * 100;

            if (rate >= _options.AlertErrorRateThreshold && !_alertFired)
            {
                _alertFired = true;
                _ = SendAlertAsync(rate, deltaF, deltaC, ct);
            }
            else if (rate < _options.AlertErrorRateThreshold)
            {
                _alertFired = false;
            }
        }
    }

    private async Task SendAlertAsync(double rate, long faulted, long consumed, CancellationToken ct)
    {
        try
        {
            var client  = _httpFactory.CreateClient();
            var payload = new
            {
                source      = "MassLens",
                level       = "alert",
                message     = $"Error rate {rate:F1}% exceeds threshold {_options.AlertErrorRateThreshold}%",
                errorRate   = Math.Round(rate, 2),
                threshold   = _options.AlertErrorRateThreshold,
                faultedLast30s  = faulted,
                consumedLast30s = consumed,
                timestamp   = DateTimeOffset.UtcNow
            };
            await client.PostAsJsonAsync(_options.AlertWebhookUrl, payload, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "MassLens: failed to send threshold alert to {Url}", _options.AlertWebhookUrl);
        }
    }
}
