using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.Pipeline.Steps;
using Instancium.Publisher.Application.State;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.Infrastructure.DependencyInjection;
using Instancium.Publisher.Tui;
using Instancium.Publisher.Tui.Workspaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Instancium.Publisher;

internal static class Program
{
    internal static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddPublisherInfrastructure(context.Configuration);

                services.AddSingleton<ProjectContext>();
                services.AddSingleton<IProgressReporter, TuiProgressReporter>();
                services.AddTransient<BumpVersionStep>();
                services.AddTransient<GenerateCommitMessageStep>();
                services.AddTransient<MergeAndPublishStep>();
                services.AddTransient<ITuiWorkspace, CommitWorkspace>();
                services.AddTransient<ITuiWorkspace, ReleaseWorkspace>();
                services.AddSingleton<MainTuiOrchestrator>();
            })
            .Build();

        var orchestrator = host.Services.GetRequiredService<MainTuiOrchestrator>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await orchestrator.RunInteractiveLoopAsync(lifetime.ApplicationStopping);
    }
}