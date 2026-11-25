using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.Pipeline.Steps;
using Instancium.Publisher.Application.State;
using Instancium.Publisher.Application.Versioning;
using Instancium.Publisher.Application.Workflows;
using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Domain.Versioning.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace Instancium.Publisher.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddPublisherApplication(this IServiceCollection services)
    {
        services.AddSingleton<IVersionBumpStrategy, PatchBumpStrategy>();
        services.AddSingleton<IVersionBumpStrategy, MinorBumpStrategy>();
        services.AddSingleton<IVersionBumpStrategy, MajorBumpStrategy>();
        services.AddSingleton<IVersionBumpStrategy, PreReleaseIterationBumpStrategy>();
        services.AddSingleton<IVersionBumpStrategy, NewPreReleasePatchBumpStrategy>();
        services.AddSingleton<IVersionBumpStrategy, NewPreReleaseMinorBumpStrategy>();
        services.AddSingleton<IVersionBumpStrategy, NewPreReleaseMajorBumpStrategy>();
        services.AddSingleton<IVersionBumpStrategy, PromoteToReleaseBumpStrategy>();
        services.AddSingleton<IVersionBumpStrategy, CustomBumpStrategy>();
        services.AddSingleton<IVersionBumpService, VersionBumpService>();
        services.AddSingleton<IProjectVersionService, RepositoryVersionService>();

        services.AddSingleton<ProjectContext>();
        services.AddTransient<BumpVersionStep>();
        services.AddTransient<BuildAndSignStep>();
        services.AddTransient<MergeAndPublishStep>();
        services.AddTransient<CommitWorkflow>();
        services.AddTransient<ReleaseWorkflow>();
        services.AddTransient<ReleaseRecoveryService>();

        return services;
    }
}
