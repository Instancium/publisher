using Instancium.Publisher.Core.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class ProcessExecutionService : IProcessExecutionService
{
    // The process will be killed if it produces no output for 30 seconds
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    public async Task<int> ExecuteAsync(
        string executable,
        string arguments,
        string workingDirectory,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null,
        CancellationToken ct = default)
    {
        using var idleCts = new CancellationTokenSource(IdleTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, idleCts.Token);

        using var process = CreateProcess(executable, arguments, workingDirectory, redirectOutput: true);
        process.Start();

        // Read the streams and reset the idle timer on each process output
        var outTask = ReadStreamWithIdleResetAsync(process.StandardOutput, onStdOut, idleCts, linkedCts.Token);
        var errTask = ReadStreamWithIdleResetAsync(process.StandardError, onStdErr, idleCts, linkedCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            await Task.WhenAll(outTask, errTask);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (idleCts.IsCancellationRequested)
        {
            KillProcessTreeSafely(process);
            throw new TimeoutException($"Process '{executable}' hung (no output for {IdleTimeout.TotalSeconds}s) and was terminated.");
        }
    }

    // The same approach for GetOutputAsync...
    public async Task<string> GetOutputAsync(
        string executable,
        string arguments,
        string workingDirectory,
        CancellationToken ct = default)
    {
        using var idleCts = new CancellationTokenSource(IdleTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, idleCts.Token);

        using var process = CreateProcess(executable, arguments, workingDirectory, redirectOutput: true);
        var stdOutBuilder = new StringBuilder();
        var stdErrBuilder = new StringBuilder();

        process.Start();

        var outTask = ReadStreamWithIdleResetAsync(process.StandardOutput, msg => stdOutBuilder.AppendLine(msg), idleCts, linkedCts.Token);
        var errTask = ReadStreamWithIdleResetAsync(process.StandardError, msg => stdErrBuilder.AppendLine(msg), idleCts, linkedCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            await Task.WhenAll(outTask, errTask);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Command '{executable} {arguments}' failed (Exit: {process.ExitCode}). Error: {stdErrBuilder}");
            }
            return stdOutBuilder.ToString().Trim();
        }
        catch (OperationCanceledException) when (idleCts.IsCancellationRequested)
        {
            KillProcessTreeSafely(process);
            throw new TimeoutException($"Process '{executable}' hung (no output for {IdleTimeout.TotalSeconds}s). Error output before hang: {stdErrBuilder}");
        }
    }

    private static async Task ReadStreamWithIdleResetAsync(
        StreamReader reader,
        Action<string>? onLine,
        CancellationTokenSource idleCts,
        CancellationToken ct)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                // LIFELINE: Reset the idle timer. The process is not hung.
                try { idleCts.CancelAfter(IdleTimeout); } catch { /* Ignore if already disposed */ }

                onLine?.Invoke(line);
            }
        }
        catch (OperationCanceledException) { /* Normal termination on timeout */ }
    }

    private static Process CreateProcess(string executable, string arguments, string workingDirectory, bool redirectOutput)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : executable,
                Arguments = isWindows ? $"/c {executable} {arguments}" : arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = redirectOutput,
                RedirectStandardError = redirectOutput,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
    }

    private static void KillProcessTreeSafely(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { }
    }
}