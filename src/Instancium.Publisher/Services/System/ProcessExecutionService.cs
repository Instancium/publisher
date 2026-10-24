using Instancium.Publisher.Core.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;


public sealed class ProcessExecutionService : IProcessExecutionService
{
    public async Task<int> ExecuteAsync(
        string executable,
        string arguments,
        string workingDirectory,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null,
        CancellationToken ct = default)
    {
        using var process = CreateProcess(executable, arguments, workingDirectory, redirectOutput: true);

        if (onStdOut != null)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) onStdOut(e.Data);
            };
        }

        if (onStdErr != null)
        {
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) onStdErr(e.Data);
            };
        }

        process.Start();

        if (onStdOut != null) process.BeginOutputReadLine();
        if (onStdErr != null) process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    public async Task<string> GetOutputAsync(
        string executable,
        string arguments,
        string workingDirectory,
        CancellationToken ct = default)
    {
        using var process = CreateProcess(executable, arguments, workingDirectory, redirectOutput: true);

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            string error = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"Command '{executable} {arguments}' failed (Exit: {process.ExitCode}). Error: {error}");
        }

        return output.Trim();
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
}