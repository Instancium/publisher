using Instancium.Publisher.State;

namespace Instancium.Publisher.Core.Interfaces
{
    public interface IPipelineStateManager
    {
        Task SaveAsync(PipelineContext context);
        Task<PipelineContext?> LoadAsync();
        Task RollbackAsync();
    }
}
