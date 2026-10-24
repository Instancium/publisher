using Instancium.Publisher.Configuration;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.Pipeline.Steps;
using Instancium.Publisher.Services.Ai;
using Instancium.Publisher.Services.Git;
using Instancium.Publisher.Services.Versioning;
using Instancium.Publisher.State;
using Instancium.Publisher.Tui;
using Instancium.Publisher.Tui.Workspaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Instancium.Publisher;

internal static class Program
{
    internal static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<PublisherSettings>(
                    context.Configuration.GetSection("PublisherSettings"));

                services.AddSingleton<ProjectContext>();
                services.AddSingleton<IProgressReporter, TuiProgressReporter>();
                services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
                services.AddSingleton<IGitService, GitService>();
                services.AddSingleton<IVersionEngine, VersionEngine>();
                services.AddSingleton<IPipelineStateManager, PipelineStateManager>();
                services.AddSingleton<IAiCommitAssistant, SignalRAiCommitAssistant>();

                services.AddTransient<BumpVersionStep>();
                services.AddTransient<GenerateCommitMessageStep>();
                services.AddTransient<GitAddAllStep>();
                services.AddTransient<GitCommitAndPushStep>();
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