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
using InstantAIGate.Client.DependencyInjection;
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
                services.Configure<PublisherSettings>(
                    context.Configuration.GetSection("PublisherSettings"));

                PublisherSettings publisherSettings = context.Configuration
                    .GetSection("PublisherSettings")
                    .Get<PublisherSettings>() ?? new PublisherSettings();

                services.AddInstantAIGateClient(options =>
                {
                    options.BaseUrl = publisherSettings.AiGatewayUrl;
                    options.ApiKey = publisherSettings.AiApiKey;
                    options.LlmRepoId = publisherSettings.AiModelId;
                    options.ClientId = "publisher";

                    // Total reconnect budget is 17 seconds, kept below the server-side
                    // disconnect grace period (30 seconds) so sessions and their pinned
                    // KV-cache prefixes survive typical network blinks without eviction.
                    options.ReconnectDelays =
                    [
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10)
                    ];
                });

                services.AddSingleton<IEnvironmentInfoProvider, EnvironmentInfoProvider>();
                services.AddSingleton<ProjectContext>();
                services.AddSingleton<IProgressReporter, TuiProgressReporter>();
                services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
                services.AddSingleton<IGitService, GitService>();
                services.AddSingleton<IVersionEngine, VersionEngine>();
                services.AddSingleton<IPipelineStateManager, PipelineStateManager>();
                services.AddSingleton<IDiffPayloadOptimizer, RegexDiffPayloadOptimizer>();
                services.AddSingleton<IAiCommitAssistant, SdkAiCommitAssistant>();
                services.AddSingleton<IProjectDiscoveryService, ProjectDiscoveryService>();
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