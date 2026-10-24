using Instancium.Publisher.Core.Interfaces;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Instancium.Publisher.Pipeline.Steps;

public class RunUnitTestsStep
{
    private readonly IProcessExecutionService _processService;

    public RunUnitTestsStep(IProcessExecutionService processService)
    {
        _processService = processService;
    }

    public async Task ExecuteAsync(string targetPath, CancellationToken ct = default)
    {
        var stdOutBuilder = new StringBuilder();
        var stdErrBuilder = new StringBuilder();

        bool isFile = Path.HasExtension(targetPath);
        string workingDirectory = isFile ? Path.GetDirectoryName(targetPath)! : targetPath;
        string arguments = isFile ? $"test \"{targetPath}\"" : "test";

        int exitCode = await _processService.ExecuteAsync(
            "dotnet",
            arguments,
            workingDirectory,
            onStdOut: msg => stdOutBuilder.AppendLine(msg),
            onStdErr: msg => stdErrBuilder.AppendLine(msg),
            ct: ct);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Unit tests failed. Exit code: {exitCode}.\nError: {stdErrBuilder}\nOutput: {stdOutBuilder}");
        }
    }
}