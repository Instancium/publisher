namespace Instancium.Publisher.Application.Abstractions;

using System.Threading;
using System.Threading.Tasks;

public interface IProgressReporter
{
    ValueTask ReportAsync(string message, CancellationToken ct = default);

    ValueTask ReportWarningAsync(string message, CancellationToken ct = default);
}