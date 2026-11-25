using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Infrastructure.Ai;
using Instancium.Publisher.Infrastructure.Configuration;
using Instancium.Publisher.Infrastructure.Git;
using Instancium.Publisher.Infrastructure.GitHub;
using Instancium.Publisher.Infrastructure.Publishing;
using Instancium.Publisher.Infrastructure.Runtime;
using Instancium.Publisher.Infrastructure.Signing;
using Instancium.Publisher.Infrastructure.State;
using Instancium.Publisher.Infrastructure.Versioning;
using Instancium.Publisher.Infrastructure.Versioning.DotNet;
using Instancium.Trust.Algorithms;
using Instancium.Trust.Keys;
using Instancium.Trust.Manifests;
using InstantAIGate.Client.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Instancium.Publisher.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPublisherInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PublisherSettings>(
            configuration.GetSection("PublisherSettings"));

        PublisherSettings publisherSettings = configuration
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
        services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<IProjectEcosystem, MsBuildProjectEcosystem>();
        services.AddSingleton<IPipelineStateManager, PipelineStateManager>();
        services.AddSingleton<IDiffPayloadOptimizer, RegexDiffPayloadOptimizer>();
        services.AddSingleton<IAiCommitAssistant, SdkAiCommitAssistant>();
        services.AddSingleton<IProjectDiscoveryService, RepositoryProjectScanner>();
        services.AddSingleton<IReleasePublisher, GitHubCliReleasePublisher>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISignatureAlgorithm, Ed25519SignatureAlgorithm>();
        services.AddSingleton<SignatureAlgorithmRegistry>();
        services.AddSingleton<KeyContainerProtector>();
        services.AddSingleton<KeyContainerCodec>();
        services.AddSingleton<ManifestSigner>();

        services.AddSingleton<IArtifactBuilder, DeclarativeArtifactBuilder>();
        services.AddSingleton<IArtifactSigner, LocalReleaseKeySigner>();

        return services;
    }
}
