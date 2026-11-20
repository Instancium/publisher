using Instancium.Publisher.Application.Pipeline.Steps;
using Instancium.Publisher.Application.State;
using Microsoft.Extensions.DependencyInjection;

namespace Instancium.Publisher.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddPublisherApplication(this IServiceCollection services)
    {
        services.AddSingleton<ProjectContext>();
        services.AddTransient<BumpVersionStep>();
        services.AddTransient<GenerateCommitMessageStep>();
        services.AddTransient<MergeAndPublishStep>();

        return services;
    }
}
