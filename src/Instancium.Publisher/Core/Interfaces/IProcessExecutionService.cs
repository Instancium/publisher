namespace Instancium.Publisher.Core.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IProcessExecutionService
{
    Task<int> ExecuteAsync(
        string executable,
        string arguments,
        string workingDirectory,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null,
        CancellationToken ct = default);

    Task<string> GetOutputAsync(
        string executable,
        string arguments,
        string workingDirectory,
        CancellationToken ct = default);
}