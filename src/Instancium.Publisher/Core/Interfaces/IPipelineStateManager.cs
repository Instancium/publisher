using Instancium.Publisher.Application.Pipeline;

namespace Instancium.Publisher.Core.Interfaces
{
    public interface IPipelineStateManager
    {
        Task SaveAsync(PipelineContext context);
        Task<PipelineContext?> LoadAsync();
        Task RollbackAsync();
    }
}
