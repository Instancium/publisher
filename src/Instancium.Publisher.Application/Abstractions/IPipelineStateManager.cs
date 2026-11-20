using Instancium.Publisher.Application.Pipeline;

namespace Instancium.Publisher.Application.Abstractions
{
    public interface IPipelineStateManager
    {
        Task SaveAsync(PipelineContext context);
        Task<PipelineContext?> LoadAsync();
        Task RollbackAsync();
    }
}
