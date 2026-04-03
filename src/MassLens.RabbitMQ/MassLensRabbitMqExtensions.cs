using MassLens.Core;
using Microsoft.Extensions.DependencyInjection;

namespace MassLens.RabbitMQ;

public static class MassLensRabbitMqExtensions
{
    public static IServiceCollection AddMassLensRabbitMq(
        this IServiceCollection services,
        Action<RabbitMqRequeueOptions>? configure = null)
    {
        var opts = new RabbitMqRequeueOptions();
        configure?.Invoke(opts);

        services.AddSingleton(opts);
        services.AddScoped<RabbitMqRequeueService>();
        services.AddScoped<RequeueService>(sp => sp.GetRequiredService<RabbitMqRequeueService>());

        return services;
    }
}
