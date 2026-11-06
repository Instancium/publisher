using Instancium.Publisher.Configuration;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.Pipeline.Steps;
using Instancium.Publisher.Services.Ai;
using Instancium.Publisher.Services.Git;
using Instancium.Publisher.Services.Runtime;
using Instancium.Publisher.Services.Versioning;
using Instancium.Publisher.State;
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
                // В реальном приложении здесь можно добавить файловый логгер,
                // например: logging.AddFile("logs/publisher-{Date}.txt");
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<PublisherSettings>(
                    context.Configuration.GetSection("PublisherSettings"));

                services.AddSingleton<IEnvironmentInfoProvider, EnvironmentInfoProvider>();
                services.AddSingleton<ProjectContext>();
                services.AddSingleton<IProgressReporter, TuiProgressReporter>();
                services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
                services.AddSingleton<IGitService, GitService>();
                services.AddSingleton<IVersionEngine, VersionEngine>();
                services.AddSingleton<IPipelineStateManager, PipelineStateManager>();
                services.AddSingleton<IAiCommitAssistant, SignalRAiCommitAssistant>();
                services.AddSingleton<IProjectDiscoveryService, ProjectDiscoveryService>();

                services.AddTransient<BumpVersionStep>();
                services.AddTransient<GenerateCommitMessageStep>();
                services.AddTransient<GitAddAllStep>();
                services.AddTransient<GitCommitAndPushStep>();
                services.AddTransient<MergeAndPublishStep>();
                services.AddTransient<RunUnitTestsStep>();

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