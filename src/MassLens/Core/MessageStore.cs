using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MassLens.Core;

public sealed class MessageStore
{
    public static readonly MessageStore Instance = new();

    private Channel<MessageEntry> _channel =
        Channel.CreateBounded<MessageEntry>(new BoundedChannelOptions(10_000)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<string, ConsumerMetrics>         _consumers  = new();
    private readonly ConcurrentDictionary<string, SagaStateMachineMetrics> _sagas      = new();
    private readonly ConcurrentDictionary<string, List<MessageEntry>>      _traceIndex = new();
    private CircularBuffer<long>         _globalThroughput = new(10_000);
    private CircularBuffer<MessageEntry> _recentEntries    = new(500);

    private long _totalPublished;
    private long _totalSent;
    private long _totalConsumed;
    private long _totalFaulted;

    // caps the trace index to avoid unbounded memory growth
    private const int TraceIndexMaxKeys    = 10_000;
    private const int TraceIndexMaxPerKey  = 50;

    private int  _configured = 0;
    private CancellationTokenSource _drainCts = new();
    private Timer? _retentionTimer;

    private MessageStore()
    {
        StartDrain();
    }

    private void StartDrain()
    {
        var token = _drainCts.Token;
        _ = Task.Run(async () =>
        {
            try   { await DrainChannelAsync(token); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // restart drain on unexpected failure
                await Task.Delay(500);
                if (!_drainCts.IsCancellationRequested)
                    StartDrain();
                _ = ex; // suppress unused warning
            }
        });
    }

    public void Configure(MassLensOptions options)
    {
        if (Interlocked.CompareExchange(ref _configured, 1, 0) != 0) return;

        // cancel and discard the initial drain before replacing the channel
        _drainCts.Cancel();
        _drainCts = new CancellationTokenSource();

        _channel = Channel.CreateBounded<MessageEntry>(new BoundedChannelOptions(options.ChannelCapacity)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _globalThroughput = new CircularBuffer<long>(options.ChannelCapacity);
        _recentEntries    = new CircularBuffer<MessageEntry>(options.RecentEntriesCapacity);

        if (options.MetricsRetentionHours > 0)
        {
            var interval = TimeSpan.FromHours(options.MetricsRetentionHours);
            _retentionTimer = new Timer(_ => Reset(), null, interval, interval);
        }

        StartDrain();
    }

    /// <summary>Resets all in-memory counters, trace history, and consumer metrics.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _totalConsumed,  0);
        Interlocked.Exchange(ref _totalPublished, 0);
        Interlocked.Exchange(ref _totalSent,      0);
        Interlocked.Exchange(ref _totalFaulted,   0);
        _consumers.Clear();
        _sagas.Clear();
        _traceIndex.Clear();
        _globalThroughput.Clear();
        _recentEntries.Clear();
        HeatmapAggregator.Instance.Reset();
        ThroughputPredictor.Instance.Reset();
    }

    public void Write(MessageEntry entry) => _channel.Writer.TryWrite(entry);

    public SagaStateMachineMetrics GetOrAddSaga(string name) =>
        _sagas.GetOrAdd(name, n => new SagaStateMachineMetrics(n));

    public void RecordPreConsume(string consumerType, string endpoint) =>
        _consumers.GetOrAdd(consumerType, _ => new ConsumerMetrics(consumerType, endpoint))
                  .RecordPreConsume();

    private async Task DrainChannelAsync(CancellationToken ct)
    {
        await foreach (var entry in _channel.Reader.ReadAllAsync(ct))
        {
            _recentEntries.Write(entry);

            if (!string.IsNullOrEmpty(entry.CorrelationId))
            {
                // evict oldest key when cap is reached
                if (_traceIndex.Count >= TraceIndexMaxKeys)
                {
                    var oldest = _traceIndex
                        .OrderBy(kv => kv.Value.FirstOrDefault()?.Timestamp ?? DateTimeOffset.MaxValue)
                        .Select(kv => kv.Key)
                        .FirstOrDefault();
                    if (oldest is not null)
                        _traceIndex.TryRemove(oldest, out _);
                }

                var list = _traceIndex.GetOrAdd(entry.CorrelationId, _ => new List<MessageEntry>());
                lock (list)
                {
                    list.Add(entry);
                    if (list.Count > TraceIndexMaxPerKey)
                        list.RemoveAt(0);
                }
            }

            switch (entry.Direction)
            {
                case MessageDirection.Consumed:
                    Interlocked.Increment(ref _totalConsumed);
                    _globalThroughput.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    var cm = _consumers.GetOrAdd(entry.ConsumerType,
                        _ => new ConsumerMetrics(entry.ConsumerType, entry.EndpointAddress));
                    cm.RecordConsumed(entry.Duration, entry.SizeBytes);
                    HeatmapAggregator.Instance.Record(entry.ConsumerType);
                    ThroughputPredictor.Instance.Record(cm.GetThroughput());
                    break;

                case MessageDirection.Faulted:
                    Interlocked.Increment(ref _totalFaulted);
                    var fm = _consumers.GetOrAdd(entry.ConsumerType,
                        _ => new ConsumerMetrics(entry.ConsumerType, entry.EndpointAddress));
                    fm.RecordFaulted(entry.Duration);
                    break;

                case MessageDirection.Published:
                    Interlocked.Increment(ref _totalPublished);
                    break;

                case MessageDirection.Sent:
                    Interlocked.Increment(ref _totalSent);
                    break;
            }
        }
    }

    public MessageEntry[] GetTrace(string correlationId)
    {
        return _traceIndex.TryGetValue(correlationId, out var list)
            ? list.ToArray()
            : [];
    }

    public string[] GetRecentCorrelationIds(int limit = 20)
    {
        return _traceIndex
            .Where(kv => kv.Value.Any(e =>
                e.Direction is MessageDirection.Consumed or MessageDirection.Faulted))
            .OrderByDescending(kv => kv.Value.Max(e => e.Timestamp))
            .Take(limit)
            .Select(kv => kv.Key)
            .ToArray();
    }

    public MessageEntry[] GetRecentEntries() => _recentEntries.ReadAll().AsEnumerable().Reverse().ToArray();

    public DashboardSnapshot GetSnapshot()
    {
        var consumers  = _consumers.Values.Select(c => c.GetSnapshot()).ToArray();
        var cutoff     = DateTimeOffset.UtcNow.AddSeconds(-60).ToUnixTimeMilliseconds();
        var samples    = _globalThroughput.ReadAll();
        double rate    = (double)samples.Count(ts => ts >= cutoff) / 60;

        return new DashboardSnapshot
        {
            Timestamp        = DateTimeOffset.UtcNow,
            TotalConsumed    = Interlocked.Read(ref _totalConsumed),
            TotalPublished   = Interlocked.Read(ref _totalPublished),
            TotalSent        = Interlocked.Read(ref _totalSent),
            TotalFaulted     = Interlocked.Read(ref _totalFaulted),
            GlobalThroughput = rate,
            Consumers        = consumers,
            Sagas            = _sagas.Values.Select(s => s.GetSnapshot()).ToArray(),
            Heatmap          = HeatmapAggregator.Instance.GetSnapshot(),
            Predictor        = ThroughputPredictor.Instance.GetSnapshot()
        };
    }
}

public sealed class DashboardSnapshot
{
    public DateTimeOffset Timestamp        { get; init; }
    public long TotalConsumed              { get; init; }
    public long TotalPublished             { get; init; }
    public long TotalSent                  { get; init; }
    public long TotalFaulted               { get; init; }
    public double GlobalThroughput         { get; init; }
    public ConsumerSnapshot[]  Consumers   { get; init; } = [];
    public SagaSnapshot[]      Sagas       { get; init; } = [];
    public HeatmapSnapshot     Heatmap     { get; init; } = new();
    public PredictorSnapshot   Predictor   { get; init; } = new();
}
