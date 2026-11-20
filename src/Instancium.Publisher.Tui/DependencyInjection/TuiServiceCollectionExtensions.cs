using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Tui.Abstractions;
using Instancium.Publisher.Tui.Configuration;
using Instancium.Publisher.Tui.Workspaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Instancium.Publisher.Tui.DependencyInjection;

public static class TuiServiceCollectionExtensions
{
    public static IServiceCollection AddPublisherTui(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<NavigationSettings>(
            configuration.GetSection("PublisherSettings"));

        services.AddSingleton<IProgressReporter, TuiProgressReporter>();
        services.AddTransient<ITuiWorkspace, CommitWorkspace>();
        services.AddTransient<ITuiWorkspace, ReleaseWorkspace>();
        services.AddSingleton<MainTuiOrchestrator>();

        return services;
    }
}
