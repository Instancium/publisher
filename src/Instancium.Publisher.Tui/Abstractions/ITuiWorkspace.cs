namespace Instancium.Publisher.Tui.Abstractions;

using System.Threading;
using System.Threading.Tasks;

public interface ITuiWorkspace
{
    string Name { get; }
    string Description { get; }
    Task ExecuteAsync(CancellationToken ct = default);
}