namespace Instancium.Publisher.Services.Ai;

using Instancium.Publisher.Infrastructure.Configuration;
using Instancium.Publisher.Application.Abstractions;
using InstantAIGate.Client.Chat;
using InstantAIGate.Client.Configuration;
using InstantAIGate.Client.Telemetry;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// SDK-backed implementation of <see cref="IAiCommitAssistant"/>. All transport, session
/// lifecycle, KV-cache checkpointing, rollback verification, and Map-Reduce recovery mechanics
/// are delegated to InstantAIGate.Client; this class owns only domain orchestration: diff
/// optimization, chunking, prompt construction, and output sanitization.
/// </summary>
public sealed class SdkAiCommitAssistant : IAiCommitAssistant
{
    private const int DefaultContextSize = 4096;
    private const int SystemPromptTokenBudget = 400;
    private const int ReserveTokens = 512;
    private const int MinUsableTokens = 512;
    private const double CharsPerTokenEstimate = 2.0;

    /// <summary>
    /// Conservative character ceiling per hub message. The server clamps
    /// MaximumReceiveMessageSize to 32768 bytes; JSON envelope overhead and multi-byte
    /// UTF-8 content make 15000 characters a safe upper bound for a single prompt.
    /// </summary>
    private const int TransportSafeCharLimit = 15000;
    private const int DefaultMaxChunkLength = 3000;

    private readonly IChatClientFactory _chatFactory;
    private readonly IChatMapReduceClient _mapReduceClient;
    private readonly IGatewayTelemetryClient _telemetryClient;
    private readonly IDiffPayloadOptimizer _diffOptimizer;
    private readonly IProgressReporter _progressReporter;
    private readonly PublisherSettings _settings;

    public SdkAiCommitAssistant(
        IChatClientFactory chatFactory,
        IChatMapReduceClient mapReduceClient,
        IGatewayTelemetryClient telemetryClient,
        IDiffPayloadOptimizer diffOptimizer,
        IProgressReporter progressReporter,
        IOptions<PublisherSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(chatFactory);
        ArgumentNullException.ThrowIfNull(mapReduceClient);
        ArgumentNullException.ThrowIfNull(telemetryClient);
        ArgumentNullException.ThrowIfNull(diffOptimizer);
        ArgumentNullException.ThrowIfNull(progressReporter);
        ArgumentNullException.ThrowIfNull(settings);
        _chatFactory = chatFactory;
        _mapReduceClient = mapReduceClient;
        _telemetryClient = telemetryClient;
        _diffOptimizer = diffOptimizer;
        _progressReporter = progressReporter;
        _settings = settings.Value;
    }

    public async IAsyncEnumerable<string> StreamCommitMessageAsync(
        string diff,
        string diffStat,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        List<FileDiffStatus> totalFileStatuses = GitDiffStatusParser.ExtractFileStatuses(diff);
        string optimizedDiff = _diffOptimizer.Optimize(diff);
        int safeChunkChars = await ResolveSafeChunkCharsAsync(ct).ConfigureAwait(false);

        if (optimizedDiff.Length <= safeChunkChars)
        {
            ChatMessage prompt = CommitPromptBuilder.BuildFinalPrompt(optimizedDiff, diffStat, totalFileStatuses);
            await foreach (string token in StreamSingleShotAsync(prompt, ct).ConfigureAwait(false))
            {
                yield return token;
            }
            yield break;
        }

        await _progressReporter.ReportAsync(
            $"Diff exceeds single chunk capacity ({optimizedDiff.Length} chars > {safeChunkChars} safe limit). Engaging Checkpointed Map-Reduce...",
            ct).ConfigureAwait(false);

        List<string> chunks = AiPayloadChunker.ChunkDiffByFiles(optimizedDiff, safeChunkChars);
        var mapMessages = new List<ChatMessage>(chunks.Count);
        foreach (string chunk in chunks)
        {
            List<FileDiffStatus> chunkStatuses = GitDiffStatusParser.ExtractFileStatuses(chunk);
            mapMessages.Add(CommitPromptBuilder.BuildChunkPrompt(chunk, chunkStatuses));
        }

        var request = new ChatMapReduceRequest
        {
            WarmupMessage = new ChatMessage("system", CommitPromptBuilder.MapReduceSystemPrompt),
            MapMessages = mapMessages,
            FinalPromptFactory = aggregated => CommitPromptBuilder.BuildFinalPrompt(aggregated, diffStat, totalFileStatuses),
            MaxAggregatedChars = safeChunkChars,
            Progress = new ProgressAdapter(_progressReporter)
        };

        await foreach (string token in _mapReduceClient.StreamAsync(request, ct).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    public async Task<string> GenerateCommitMessageAsync(string diff, string diffStat, CancellationToken ct = default)
    {
        var builder = new StringBuilder();
        await foreach (string token in StreamCommitMessageAsync(diff, diffStat, ct).ConfigureAwait(false))
        {
            builder.Append(token);
        }
        string sanitized = CommitMessageSanitizer.Sanitize(builder.ToString());
        if (!string.IsNullOrWhiteSpace(sanitized) && !sanitized.Contains('\n'))
        {
            string[] statsLines = diffStat.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (statsLines.Length > 0)
            {
                sanitized += Environment.NewLine + Environment.NewLine;
                var summaryFiles = statsLines.Take(3).Select(l => l.Trim()).Where(l => !l.StartsWith(' '));
                foreach (string file in summaryFiles)
                {
                    sanitized += $"- Update {file}" + Environment.NewLine;
                }
                sanitized = sanitized.TrimEnd();
            }
        }
        return sanitized;
    }

    public async IAsyncEnumerable<string> StreamReleaseNotesAsync(
        string gitLog,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string filteredLog = ReleaseNotesPromptBuilder.FilterReleaseNoise(gitLog);
        int safeLimit = await ResolveSafeChunkCharsAsync(ct).ConfigureAwait(false);

        if (filteredLog.Length <= safeLimit)
        {
            ChatMessage prompt = ReleaseNotesPromptBuilder.BuildChangelogPrompt(filteredLog);
            await foreach (string token in StreamSingleShotAsync(prompt, ct).ConfigureAwait(false))
            {
                yield return token;
            }
            yield break;
        }

        await _progressReporter.ReportAsync(
            $"Commit log exceeds chunk limits ({filteredLog.Length} chars). Engaging Checkpointed Map-Reduce...",
            ct).ConfigureAwait(false);

        List<string> chunks = AiPayloadChunker.ChunkStringByLines(filteredLog, safeLimit);
        var mapMessages = new List<ChatMessage>(chunks.Count);
        foreach (string chunk in chunks)
        {
            mapMessages.Add(new ChatMessage("user", $"Extract partial release notes from this log segment:\n{chunk}"));
        }

        var request = new ChatMapReduceRequest
        {
            WarmupMessage = new ChatMessage("system", ReleaseNotesPromptBuilder.MapReduceSystemPrompt),
            MapMessages = mapMessages,
            FinalPromptFactory = aggregated => ReleaseNotesPromptBuilder.BuildChangelogPrompt(aggregated),
            MaxAggregatedChars = safeLimit,
            Progress = new ProgressAdapter(_progressReporter)
        };

        await foreach (string token in _mapReduceClient.StreamAsync(request, ct).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    private async IAsyncEnumerable<string> StreamSingleShotAsync(
        ChatMessage message,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using IChatClient client = _chatFactory.Create(ChatContextPolicy.Stateless);
        await foreach (string token in client.StreamAsync(message, ct).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    private async Task<int> ResolveSafeChunkCharsAsync(CancellationToken ct)
    {
        NativeModelDetails details = await _telemetryClient.GetActiveModelDetailsAsync(ct).ConfigureAwait(false);
        int contextSize = details.ContextSize > 0 ? details.ContextSize : DefaultContextSize;
        int usableTokens = Math.Max(MinUsableTokens, contextSize - SystemPromptTokenBudget - ReserveTokens);
        int llmSafeLimit = (int)(usableTokens * CharsPerTokenEstimate);
        int configuredLimit = _settings.MaxChunkLength > 0 ? _settings.MaxChunkLength : DefaultMaxChunkLength;
        return Math.Min(Math.Min(llmSafeLimit, configuredLimit), TransportSafeCharLimit);
    }

    /// <summary>
    /// Adapts the SDK's <see cref="IProgress{T}"/> pipeline snapshots onto the publisher's
    /// <see cref="IProgressReporter"/>. Publisher reporters complete synchronously
    /// (ValueTask.CompletedTask), so blocking here is safe and preserves deterministic
    /// message ordering in the TUI live status. Reduce phases map to warning styling,
    /// matching the legacy tree-reduction UX.
    /// </summary>
    private sealed class ProgressAdapter : IProgress<MapReduceProgress>
    {
        private readonly IProgressReporter _reporter;

        public ProgressAdapter(IProgressReporter reporter)
        {
            _reporter = reporter;
        }

        public void Report(MapReduceProgress value)
        {
            ValueTask task = value.Phase == MapReducePhase.Reduce
                ? _reporter.ReportWarningAsync(value.Message, CancellationToken.None)
                : _reporter.ReportAsync(value.Message, CancellationToken.None);
            task.AsTask().GetAwaiter().GetResult();
        }
    }
}