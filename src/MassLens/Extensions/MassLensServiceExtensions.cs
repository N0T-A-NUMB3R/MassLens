using MassLens.Core;
using MassLens.Middleware;
using MassLens.Observers;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MassLens.Extensions;

public static class MassLensServiceExtensions
{
    public static IBusRegistrationConfigurator AddMassLens(
        this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumeObserver<MassLensConsumeObserver>();
        configurator.AddConsumeObserver<MassLensSagaConsumeObserver>();
        configurator.AddPublishObserver<MassLensPublishObserver>();
        configurator.AddSendObserver<MassLensSendObserver>();
        return configurator;
    }

    public static IServiceCollection AddMassLensUI(
        this IServiceCollection services,
        Action<MassLensOptions>? configure = null)
    {
        var options = new MassLensOptions();
        configure?.Invoke(options);

        MessageStore.Instance.Configure(options);

        services.AddSingleton(options);
        services.AddHealthChecks().AddCheck<MassLensHealthCheck>("masslens");

        if (!string.IsNullOrWhiteSpace(options.AlertWebhookUrl))
        {
            services.AddHttpClient();
            services.AddSingleton<ThresholdNotifier>();
            services.AddHostedService(sp => sp.GetRequiredService<ThresholdNotifier>());
        }

        return services;
    }

    public static IApplicationBuilder UseMassLens(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetService<MassLensOptions>() ?? new MassLensOptions();
        if (!options.Enabled) return app;
        app.UseMiddleware<MassLensMiddleware>(options);
        return app;
    }
}
