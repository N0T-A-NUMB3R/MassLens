using MassLens.Demo.Consumers;
using MassLens.Demo.Sagas;
using MassLens.Demo.Workers;
using MassLens.Extensions;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("MassLens.Demo", LogLevel.Information);

builder.Services.AddMassTransit(x =>
{
    x.AddMassLens();

    x.AddConsumer<OrderSubmittedConsumer>();
    x.AddConsumer<PaymentConsumer>().Endpoint(e => e.PrefetchCount = 10);
    x.AddConsumer<InventoryConsumer>().Endpoint(e => e.PrefetchCount = 8);
    x.AddConsumer<ShippingConsumer>();
    x.AddConsumer<NotificationConsumer>();
    x.AddConsumer<StockConsumer>().Endpoint(e => e.PrefetchCount = 20);
    x.AddConsumer<StockCheckedConsumer>().Endpoint(e => e.PrefetchCount = 20);
    x.AddConsumer<TrackEventConsumer>().Endpoint(e => e.PrefetchCount = 30);

    x.AddSagaStateMachine<OrderStateMachine, OrderState>()
        .InMemoryRepository();

    x.UsingInMemory((ctx, cfg) =>
    {
        cfg.ConcurrentMessageLimit = 50;

        cfg.UseMessageRetry(r => r.Exponential(3,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(200)));

        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddMassLensUI(o =>
{
    o.Enabled               = true;
    o.ReadOnly              = false;
    o.DisableInProduction   = false;
    o.ChannelCapacity       = 50_000;
    o.RecentEntriesCapacity = 2_000;
    o.MetricsRetentionHours = 0;
});

builder.Services.AddHostedService<LoadGeneratorWorker>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseHealthChecks("/health");
app.UseMassLens();

app.MapGet("/", () => Results.Redirect("/masslens"));
app.MapGet("/status", () => new { status = "running", dashboard = "/masslens", health = "/health" });

app.Run();
