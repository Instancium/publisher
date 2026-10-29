namespace Instancium.Publisher.Services.Ai;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using Instancium.Publisher.Configuration;
using Instancium.Publisher.Core.Interfaces;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

public record FileDiffStatus(string Path, string Status);

public sealed class SignalRAiCommitAssistant : IAiCommitAssistant, IAsyncDisposable
{
    private readonly string _hubUrl;
    private readonly string _apiKey;
    private readonly string _modelId;
    private HubConnection? _hubConnection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, Channel<string>> _activeChannels = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _activeCompletions = new();
    private readonly ConcurrentDictionary<string, bool> _joinedSessions = new();
    private readonly IProgressReporter _progressReporter;

    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lock", ".min.js", ".min.css", ".map", ".svg", ".png", ".jpg", ".jpeg", ".gif", ".ico",
        ".pdf", ".dll", ".so", ".exe", ".bin", ".gguf", ".zip", ".tar", ".gz", ".wasm",
        ".bundle.js", ".Designer.cs", ".g.cs"
    };

    private static readonly string[] IgnoredFileSubstrings =
    {
        "package-lock.json", "pnpm-lock.yaml", "yarn.lock", "obj/Debug/", "obj/Release/",
        "bin/Debug/", "bin/Release/", ".min."
    };

    public SignalRAiCommitAssistant(
        IOptions<PublisherSettings> settings,
        IProgressReporter progressReporter)
    {
        ArgumentNullException.ThrowIfNull(progressReporter);
        var config = settings.Value;
        string baseUrl = config.AiGatewayUrl ?? "http://localhost:5000";
        string hubPath = "/hub/gateway";
        _hubUrl = $"{baseUrl.TrimEnd('/')}/{hubPath.TrimStart('/')}";
        _apiKey = config.AiApiKey ?? string.Empty;
        _modelId = config.AiModelId ?? "qwen3-vl-8b-instruct";
        _progressReporter = progressReporter;
    }

    private void AbortAllActiveStreams(Exception exception)
    {
        foreach (var kvp in _activeChannels)
        {
            kvp.Value.Writer.TryComplete(exception);
        }
        foreach (var kvp in _activeCompletions)
        {
            kvp.Value.TrySetException(exception);
        }
        _activeChannels.Clear();
        _activeCompletions.Clear();
    }

    private void RegisterHubEventHandlers(HubConnection connection)
    {
        connection.Closed += ex =>
        {
            _joinedSessions.Clear();
            var error = ex ?? new InvalidOperationException("Underlying transport connection was closed.");
            AbortAllActiveStreams(error);
            return Task.CompletedTask;
        };

        connection.Reconnecting += ex =>
        {
            var error = ex ?? new InvalidOperationException("Connection interrupted. Reconnecting...");
            AbortAllActiveStreams(error);
            return Task.CompletedTask;
        };

        connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
        {
            if (_activeChannels.TryGetValue(delta.SessionId, out var channel))
            {
                if (delta.IsDone)
                {
                    channel.Writer.TryComplete();
                    if (_activeCompletions.TryGetValue(delta.SessionId, out var tcs))
                    {
                        tcs.TrySetResult();
                    }
                }
                else if (!string.IsNullOrEmpty(delta.Content))
                {
                    channel.Writer.TryWrite(delta.Content);
                }
            }
        });

        connection.On<string>("ReceiveError", error =>
        {
            var exception = new InvalidOperationException($"Remote Gateway Error: {error}");
            AbortAllActiveStreams(exception);
        });

        connection.On<System.Text.Json.JsonElement>("ReceiveContextOverflow", overflowData =>
        {
            try
            {
                string sessionId = overflowData.GetProperty("SessionId").GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(sessionId))
                {
                    var overflowEx = new InvalidOperationException($"Context Window Boundary Reached for session {sessionId}.");
                    if (_activeChannels.TryGetValue(sessionId, out var channel))
                    {
                        channel.Writer.TryComplete(overflowEx);
                    }
                    if (_activeCompletions.TryGetValue(sessionId, out var tcs))
                    {
                        tcs.TrySetException(overflowEx);
                    }
                }
            }
            catch (Exception ex)
            {
                AbortAllActiveStreams(new InvalidOperationException("Fatal parsing error during Context Overflow response.", ex));
            }
        });

        connection.On<string>("ReceiveSessionClosed", sessionId =>
        {
            if (_activeChannels.TryRemove(sessionId, out var channel))
            {
                channel.Writer.TryComplete();
            }
            if (_activeCompletions.TryRemove(sessionId, out var tcs))
            {
                tcs.TrySetResult();
            }
        });
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        while (_hubConnection != null && _hubConnection.State == HubConnectionState.Reconnecting)
        {
            await Task.Delay(200, ct);
        }

        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
        {
            return;
        }

        await _connectionLock.WaitAsync(ct);
        try
        {
            while (_hubConnection != null && _hubConnection.State == HubConnectionState.Reconnecting)
            {
                await Task.Delay(200, ct);
            }

            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
            {
                return;
            }

            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
            }

            _joinedSessions.Clear();

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    if (!string.IsNullOrWhiteSpace(_apiKey))
                    {
                        options.AccessTokenProvider = () => Task.FromResult(_apiKey)!;
                    }
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHubEventHandlers(_hubConnection);

            await _hubConnection.StartAsync(ct);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async IAsyncEnumerable<string> StreamChatAsync(
        string sessionId,
        string repoId,
        ChatMessage deltaMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var outputChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        _ = Task.Run(() => ExecuteStreamingPipelineAsync(sessionId, repoId, deltaMessage, outputChannel.Writer, ct), ct);

        await foreach (var token in outputChannel.Reader.ReadAllAsync(ct))
        {
            yield return token;
        }
    }

    private async Task ExecuteStreamingPipelineAsync(
        string sessionId,
        string repoId,
        ChatMessage deltaMessage,
        ChannelWriter<string> outputWriter,
        CancellationToken ct)
    {
        const int maxAttempts = 2;
        Exception? terminalException = null;
        try
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                await EnsureConnectedAsync(ct);
                var connection = _hubConnection ?? throw new InvalidOperationException("SignalR Hub connection is not established.");
                var internalChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true
                });
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _activeChannels[sessionId] = internalChannel;
                _activeCompletions[sessionId] = tcs;
                bool retryNeeded = false;
                bool yieldedAny = false;

                try
                {
                    if (!_joinedSessions.ContainsKey(sessionId))
                    {
                        await connection.InvokeAsync("JoinSession", sessionId, repoId ?? string.Empty, ct);
                        _joinedSessions.TryAdd(sessionId, true);

                        await connection.InvokeAsync("MarkSessionAsEphemeral", sessionId, ct);
                    }

                    await connection.SendAsync("SendPromptDelta", sessionId, deltaMessage, ct);

                    await foreach (var token in internalChannel.Reader.ReadAllAsync(ct))
                    {
                        yieldedAny = true;
                        await outputWriter.WriteAsync(token, ct);
                    }

                    await tcs.Task.WaitAsync(ct);
                    return;
                }
                catch (Exception ex) when (!yieldedAny && attempt < maxAttempts && (ex.Message.Contains("not active", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase)))
                {
                    _joinedSessions.TryRemove(sessionId, out _);
                    retryNeeded = true;
                }
                finally
                {
                    if (_activeChannels.TryGetValue(sessionId, out var existingChannel) && existingChannel == internalChannel)
                    {
                        _activeChannels.TryRemove(sessionId, out _);
                    }
                    if (_activeCompletions.TryGetValue(sessionId, out var existingTcs) && existingTcs == tcs)
                    {
                        _activeCompletions.TryRemove(sessionId, out _);
                    }
                }

                if (retryNeeded)
                {
                    await Task.Delay(300, ct);
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            terminalException = ex;
        }
        finally
        {
            outputWriter.TryComplete(terminalException);
        }
    }

    private async Task<int> GetSessionTokenCountAsync(string sessionId, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);
        return await _hubConnection!.InvokeAsync<int>("GetSessionTokenCount", sessionId, ct);
    }

    private async Task RollbackSessionAsync(string sessionId, int targetPosition, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);
        await _hubConnection!.InvokeAsync("RollbackSession", sessionId, targetPosition, ct);
    }

    private async Task DestroySessionAsync(string sessionId, CancellationToken ct)
    {
        _joinedSessions.TryRemove(sessionId, out _);
        if (_hubConnection != null)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                await _hubConnection.InvokeAsync("DestroySession", sessionId, cts.Token);
            }
            catch
            {
            }
        }
    }

    private async Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);
        return await _hubConnection!.InvokeAsync<NativeModelDetails>("GetActiveModelDetailsAsync", ct);
    }

    public async IAsyncEnumerable<string> StreamCommitMessageAsync(
        string diff,
        string diffStat,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var modelDetails = await GetActiveModelDetailsAsync(ct);
        int contextSize = modelDetails.ContextSize > 0 ? modelDetails.ContextSize : 4096;
        const int systemPromptTokens = 400;
        const int reserveTokens = 512;
        int usableTokens = Math.Max(512, contextSize - systemPromptTokens - reserveTokens);
        int safeChunkChars = (int)(usableTokens * 2.0);
        var totalFileStatuses = ExtractFileStatuses(diff);
        if (diff.Length <= safeChunkChars)
        {
            string sessionId = $"diff-direct-{Guid.NewGuid():N}";
            try
            {
                string prompt = BuildFinalCommitPrompt(diff, diffStat, totalFileStatuses);
                var deltaMessage = new ChatMessage("user", prompt);
                await foreach (var token in StreamChatAsync(sessionId, _modelId, deltaMessage, ct))
                {
                    yield return token;
                }
            }
            finally
            {
                await DestroySessionAsync(sessionId, CancellationToken.None);
            }
            yield break;
        }
        await _progressReporter.ReportAsync(
            $"Diff exceeds single chunk capacity ({diff.Length} chars > {safeChunkChars} safe limit). Engaging Checkpointed Map-Reduce...",
            ct);
        string pipelineSessionId = $"diff-pipeline-{Guid.NewGuid():N}";
        try
        {
            string systemPrompt = """
                You are an expert source code reviewer analyzing git diffs for a release commit.
                Your task is to analyze diff chunks and provide concise technical summaries.
                CRITICAL STATUS ACCURACY RULES:
                - If a file is marked [MODIFIED], describe changes/fixes to existing code. NEVER state that it was added or created.
                - Only describe a file as new if marked [ADDED].
                - Describe files marked [DELETED] as removed, and [RENAMED] as renamed or moved.
                - Provide 1-2 dense sentences without markdown fences or headers.
                """;
            var initMessage = new ChatMessage("system", systemPrompt);
            await ConsumeStreamAsync(pipelineSessionId, initMessage, ct);
            int sysCheckpoint = await GetSessionTokenCountAsync(pipelineSessionId, ct);
            var chunks = ChunkDiffByFiles(diff, safeChunkChars);
            var partialSummaries = new List<string>();
            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _progressReporter.ReportAsync(
                    $"Analyzing part {i + 1}/{chunks.Count} with KV-cache reuse...",
                    ct);
                var chunkStatuses = ExtractFileStatuses(chunks[i]);
                string filesHeader = chunkStatuses.Count > 0
                    ? string.Join("\n", chunkStatuses.Select(f => $"- [{f.Status}] {f.Path}"))
                    : "- [MODIFIED] Unknown files";
                string chunkPrompt = $"""
                    FILES IN THIS CHUNK WITH STATUS:
                    {filesHeader}
                    DIFF:
                    {chunks[i]}
                    """;
                var userMessage = new ChatMessage("user", chunkPrompt);
                string summary = await StreamSummaryAsync(pipelineSessionId, userMessage, ct);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    partialSummaries.Add(summary);
                }
                await RollbackSessionAsync(pipelineSessionId, sysCheckpoint, ct);
            }
            await _progressReporter.ReportAsync(
                "Hierarchically aggregating summaries into final commit message...",
                ct);
            string aggregatedSummary = await ReduceSummariesTreeAsync(pipelineSessionId, sysCheckpoint, partialSummaries, safeChunkChars, ct);
            string finalPrompt = BuildFinalCommitPrompt(aggregatedSummary, diffStat, totalFileStatuses);
            var finalMsg = new ChatMessage("user", finalPrompt);
            await foreach (var token in StreamChatAsync(pipelineSessionId, _modelId, finalMsg, ct))
            {
                yield return token;
            }
        }
        finally
        {
            await DestroySessionAsync(pipelineSessionId, CancellationToken.None);
        }
    }

    public async Task<string> GenerateCommitMessageAsync(string diff, string diffStat, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var token in StreamCommitMessageAsync(diff, diffStat, ct))
        {
            sb.Append(token);
        }

        string rawMessage = sb.ToString();
        string sanitized = SanitizeCommitMessage(rawMessage);

        // FALLBACK STRATEGY:
        // If the model (especially small 4B ones) failed to generate a body despite our prompt,
        // we construct a minimal valid body from the diffStat to ensure the commit isn't empty.
        if (!string.IsNullOrWhiteSpace(sanitized) && !sanitized.Contains('\n'))
        {
            // It's a single line (title only). Let's append a generic body based on stats.
            var statsLines = diffStat.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (statsLines.Length > 0)
            {
                sanitized += Environment.NewLine + Environment.NewLine;
                // Take top 3 changed files as a summary if body is missing
                var summaryFiles = statsLines.Take(3).Select(l => l.Trim()).Where(l => !l.StartsWith(" "));
                foreach (var file in summaryFiles)
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
        string filteredLog = FilterReleaseNoise(gitLog);
        const int signalRSafeLimit = 15000;
        if (filteredLog.Length <= signalRSafeLimit)
        {
            string prompt = BuildChangelogPrompt(filteredLog);
            string directSessionId = $"changelog-direct-{Guid.NewGuid():N}";
            try
            {
                var deltaMessage = new ChatMessage("user", prompt);
                await foreach (var token in StreamChatAsync(directSessionId, _modelId, deltaMessage, ct))
                {
                    yield return token;
                }
            }
            finally
            {
                await DestroySessionAsync(directSessionId, CancellationToken.None);
            }
            yield break;
        }
        await _progressReporter.ReportAsync(
            $"Commit log exceeds SignalR packet limits ({filteredLog.Length} chars). Engaging Checkpointed Map-Reduce...",
            ct);
        string pipelineSessionId = $"changelog-pipeline-{Guid.NewGuid():N}";
        try
        {
            string systemPrompt = """
                You are an expert technical writer analyzing git commit logs.
                Extract key features, bug fixes, and architectural changes from the provided log segments.
                Provide concise bullet points. Ignore trivial formatting commits.
                """;
            var initMessage = new ChatMessage("system", systemPrompt);
            await ConsumeStreamAsync(pipelineSessionId, initMessage, ct);
            int sysCheckpoint = await GetSessionTokenCountAsync(pipelineSessionId, ct);
            var chunks = ChunkStringByLines(filteredLog, signalRSafeLimit);
            var partialSummaries = new List<string>();
            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _progressReporter.ReportAsync(
                    $"Analyzing log segment {i + 1}/{chunks.Count} with KV-cache reuse...",
                    ct);
                string chunkPrompt = $"Extract partial release notes from this log segment:\n{chunks[i]}";
                var userMessage = new ChatMessage("user", chunkPrompt);
                string summary = await StreamSummaryAsync(pipelineSessionId, userMessage, ct);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    partialSummaries.Add(summary);
                }
                await RollbackSessionAsync(pipelineSessionId, sysCheckpoint, ct);
            }
            await _progressReporter.ReportAsync(
                "Hierarchically aggregating log summaries into final Changelog...",
                ct);
            string aggregatedSummary = await ReduceSummariesTreeAsync(pipelineSessionId, sysCheckpoint, partialSummaries, signalRSafeLimit, ct);
            string finalPrompt = BuildChangelogPrompt(aggregatedSummary);
            var finalMsg = new ChatMessage("user", finalPrompt);
            await foreach (var token in StreamChatAsync(pipelineSessionId, _modelId, finalMsg, ct))
            {
                yield return token;
            }
        }
        finally
        {
            await DestroySessionAsync(pipelineSessionId, CancellationToken.None);
        }
    }

    private static string FilterReleaseNoise(string rawLog)
    {
        if (string.IsNullOrWhiteSpace(rawLog))
        {
            return rawLog;
        }

        var lines = rawLog.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var filteredLines = lines.Where(line => !IsReleaseNoise(line));
        return string.Join(Environment.NewLine, filteredLines);
    }

    private static bool IsReleaseNoise(string line)
    {
        string lower = line.ToLowerInvariant();
        return lower.Contains("prepare version") ||
               lower.Contains("prepared version") ||
               lower.Contains("backport") ||
               lower.Contains("merge release") ||
               lower.Contains("chore(release)") ||
               lower.Contains("chore: merge") ||
               lower.Contains("chore: backport");
    }

    private static string BuildChangelogPrompt(string contextData)
    {
        return $"""
            Analyze the following aggregated commit data and generate a professional release changelog.
            
            RULES:
            1. MUST be exclusively in English.
            2. Group changes logically (e.g., Features, Bug Fixes, Chores, Refactoring).
            3. CRITICAL STATUS ACCURACY RULES:
               - 'A' means added/created.
               - 'M' means modified/updated. NEVER describe 'M' as new or added.
               - 'D' means deleted.
               - 'R' means renamed/moved.
            4. Keep descriptions concise and high-level.
            5. ABSOLUTELY IGNORE release engineering noise:
               - DO NOT mention version bumps, release preparations, or backports.
               - DO NOT include commits related to merging release branches.
               - Focus ONLY on actual code changes, features, and bug fixes.
            6. Provide ONLY the changelog text. Do NOT use markdown code blocks or outer quotes.
            
            Aggregated Log Data:
            {contextData}
            """;
    }

    private static List<string> ChunkStringByLines(string text, int maxChars)
    {
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (currentChunk.Length > 0 && currentChunk.Length + line.Length > maxChars)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }
            currentChunk.AppendLine(line);
        }
        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }
        return chunks;
    }

    private async Task<string> ReduceSummariesTreeAsync(
        string sessionId,
        int sysCheckpoint,
        List<string> summaries,
        int maxCharLimit,
        CancellationToken ct)
    {
        string joined = string.Join("\n- ", summaries);
        if (joined.Length <= maxCharLimit || summaries.Count <= 3)
        {
            return joined;
        }
        await _progressReporter.ReportWarningAsync(
            $"Aggregated summaries exceed window ({joined.Length} chars). Executing intermediate tree reduction...",
            ct);
        var reducedList = new List<string>();
        const int groupSize = 3;
        for (int i = 0; i < summaries.Count; i += groupSize)
        {
            ct.ThrowIfCancellationRequested();
            var group = summaries.Skip(i).Take(groupSize).ToList();
            string groupText = string.Join("\n- ", group);
            string prompt = $"""
                Combine and compress the following partial review notes into a dense, cohesive summary.
                Keep all architectural details but remove redundant phrasing.
                NOTES:
                - {groupText}
                """;
            var msg = new ChatMessage("user", prompt);
            string compressed = await StreamSummaryAsync(sessionId, msg, ct);
            reducedList.Add(compressed);
            await RollbackSessionAsync(sessionId, sysCheckpoint, ct);
        }
        return await ReduceSummariesTreeAsync(sessionId, sysCheckpoint, reducedList, maxCharLimit, ct);
    }

    private async Task<string> StreamSummaryAsync(string sessionId, ChatMessage message, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var token in StreamChatAsync(sessionId, _modelId, message, ct))
        {
            sb.Append(token);
        }
        return sb.ToString().Trim(' ', '\r', '\n', '`', '\'', '"');
    }

    private async Task ConsumeStreamAsync(string sessionId, ChatMessage message, CancellationToken ct)
    {
        await foreach (var _ in StreamChatAsync(sessionId, _modelId, message, ct))
        {
        }
    }

    private static string BuildFinalCommitPrompt(
        string aggregatedContext,
        string diffStat,
        List<FileDiffStatus> fileStatuses)
    {
        string fileListText = BuildCompactFileRegistry(fileStatuses);
        string compactDiffStat = CompressDiffStat(diffStat);
        string statSection = string.IsNullOrWhiteSpace(compactDiffStat) ? string.Empty : $"""
DIFF VOLUME STATISTICS:
{compactDiffStat}
""";

        // OPTIMIZATION FOR SMALLER MODELS (4B-8B):
        // 1. Simplified instructions.
        // 2. Explicit "Example" block (Few-Shot Prompting) is critical for small models to follow format.
        // 3. Stronger negative constraints against placeholders.

        return $"""
You are an expert developer generating a Conventional Commit message.
INPUT DATA:
{fileListText}
{statSection}
CONTEXT:
{aggregatedContext}

INSTRUCTIONS:
1. LANGUAGE: English ONLY.
2. FORMAT:
   - Line 1: type(scope): description (Max 72 chars). Imperative mood.
   - Line 2: EMPTY.
   - Line 3+: Bullet points describing changes.
3. CRITICAL RULES:
   - DO NOT use placeholders like "<no-body>" or "No changes".
   - If details are sparse, summarize the file changes based on the INPUT DATA.
   - NEVER say a file was "added" if status is [MODIFIED].

EXAMPLE OUTPUT:
feat(auth): implement jwt refresh token logic

- Add RefreshToken method to AuthService
- Update User model with token expiry field
- Modify login endpoint to return refresh token

YOUR TURN (Generate ONLY the commit message):
""";
    }

    private static bool IsGeneratedOrBinary(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string extension = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(extension) && IgnoredExtensions.Contains(extension)) return true;
        return IgnoredFileSubstrings.Any(sub => filePath.Contains(sub, StringComparison.OrdinalIgnoreCase));
    }

    private static List<FileDiffStatus> ExtractFileStatuses(string diffText)
    {
        var result = new List<FileDiffStatus>();
        using var reader = new StringReader(diffText);
        string? line;
        string? currentFile = null;
        string currentStatus = "MODIFIED";
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("diff --git a"))
            {
                if (currentFile != null) result.Add(new FileDiffStatus(currentFile, currentStatus));
                var parts = line.Split(' ');
                if (parts.Length >= 4 && parts[3].StartsWith("b/")) currentFile = parts[3][2..];
                else if (parts.Length >= 3 && parts[2].StartsWith("a/")) currentFile = parts[2][2..];
                currentStatus = "MODIFIED";
                continue;
            }
            if (line.StartsWith("new file mode")) currentStatus = "ADDED";
            else if (line.StartsWith("deleted file mode")) currentStatus = "DELETED";
            else if (line.StartsWith("similarity index") || line.StartsWith("rename from")) currentStatus = "RENAMED";
        }
        if (currentFile != null) result.Add(new FileDiffStatus(currentFile, currentStatus));
        return result.DistinctBy(f => f.Path).ToList();
    }

    private static List<string> ChunkDiffByFiles(string rawDiff, int maxLength)
    {
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();
        using var reader = new StringReader(rawDiff);
        string? line;
        bool isCurrentFileIgnored = false;
        string currentFileName = string.Empty;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("diff --git"))
            {
                var parts = line.Split(' ');
                string targetPath = parts.Length >= 4 ? parts[3] : (parts.Length >= 3 ? parts[2] : string.Empty);
                targetPath = targetPath.Trim('"');
                currentFileName = targetPath.StartsWith("b/") || targetPath.StartsWith("a/")
                    ? targetPath[2..]
                    : targetPath;
                isCurrentFileIgnored = IsGeneratedOrBinary(currentFileName);
                if (currentChunk.Length > 0 && currentChunk.Length + line.Length > maxLength)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                }
                if (isCurrentFileIgnored)
                {
                    currentChunk.AppendLine(line);
                    currentChunk.AppendLine($"[SKIPPED: Auto-generated/binary payload for {currentFileName}]");
                    continue;
                }
            }

            if (!isCurrentFileIgnored)
            {
                if (currentChunk.Length > 0 && currentChunk.Length + line.Length > maxLength)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                    if (!string.IsNullOrEmpty(currentFileName))
                    {
                        currentChunk.AppendLine($"[... CONTINUATION OF DIFF FOR FILE: {currentFileName} ...]");
                    }
                }
                currentChunk.AppendLine(line);
            }
        }
        if (currentChunk.Length > 0) chunks.Add(currentChunk.ToString());
        return chunks;
    }

    private static string BuildCompactFileRegistry(List<FileDiffStatus> fileStatuses)
    {
        const int maxFlatDisplay = 20;
        if (fileStatuses.Count <= maxFlatDisplay)
        {
            return string.Join("\n", fileStatuses.Select(f => $"- [{f.Status.ToUpperInvariant()}] {f.Path}"));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Total files changed: {fileStatuses.Count}. Module Breakdown:");
        var groups = fileStatuses
            .GroupBy(f =>
            {
                var parts = f.Path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 2 && parts[0].Equals("src", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{parts[0]}/{parts[1]}";
                }
                return parts.Length > 1 ? parts[0] : "Root";
            })
            .OrderByDescending(g => g.Count());

        foreach (var group in groups)
        {
            int modifiedCount = group.Count(f => f.Status == "MODIFIED");
            int addedCount = group.Count(f => f.Status == "ADDED");
            int deletedCount = group.Count(f => f.Status == "DELETED");
            int renamedCount = group.Count(f => f.Status == "RENAMED");

            var statusParts = new List<string>();
            if (modifiedCount > 0) statusParts.Add($"{modifiedCount} modified");
            if (addedCount > 0) statusParts.Add($"{addedCount} added");
            if (deletedCount > 0) statusParts.Add($"{deletedCount} deleted");
            if (renamedCount > 0) statusParts.Add($"{renamedCount} renamed");

            sb.AppendLine($"- {group.Key}: {string.Join(", ", statusParts)}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string CompressDiffStat(string diffStat)
    {
        if (string.IsNullOrWhiteSpace(diffStat)) return string.Empty;
        var lines = diffStat.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 12) return diffStat;

        var topEntries = lines.Take(10);
        string summaryLine = lines.Last();
        var sb = new StringBuilder();
        foreach (var line in topEntries)
        {
            sb.AppendLine(line);
        }
        sb.AppendLine($"... ({lines.Length - 11} more files)");
        sb.AppendLine(summaryLine);
        return sb.ToString().TrimEnd();
    }

    private static string SanitizeCommitMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage)) return rawMessage;

        // 1. Remove common AI hallucinations/placeholders for empty body
        string cleaned = rawMessage
            .Replace("<no-body>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</no-body>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("[no-body]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(no body)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("No body provided.", string.Empty, StringComparison.OrdinalIgnoreCase);

        var lines = cleaned.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

        // Filter out empty lines at the start to find the real title
        var contentLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        if (contentLines.Count == 0) return string.Empty;

        string title = contentLines[0].Trim();

        // Enforce Conventional Commit Title Length
        const int maxHeaderLength = 72;
        const int truncateAt = 69;
        if (title.Length > maxHeaderLength)
        {
            title = string.Concat(title.AsSpan(0, truncateAt), "...");
        }

        // If only title exists (model failed to generate body), return just title.
        // The caller or a fallback mechanism should handle adding a default body if needed.
        if (contentLines.Count <= 1) return title;

        // Process Body
        // Skip the first line (title) and any immediate empty lines
        var bodyLines = contentLines.Skip(1).ToList();

        // Remove trailing empty lines
        while (bodyLines.Count > 0 && string.IsNullOrWhiteSpace(bodyLines[^1]))
        {
            bodyLines.RemoveAt(bodyLines.Count - 1);
        }

        if (bodyLines.Count == 0) return title;

        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine(); // Mandatory blank line between title and body

        foreach (var line in bodyLines)
        {
            // Clean up individual body lines (remove accidental markdown fences if any)
            string cleanLine = line.TrimStart('`', '*', '-').Trim();
            if (!string.IsNullOrWhiteSpace(cleanLine))
            {
                // Ensure bullet points are formatted correctly if the model outputted raw text
                if (!cleanLine.StartsWith("- ") && !cleanLine.StartsWith("* "))
                {
                    // Heuristic: if it looks like a list item but missed the bullet, add it? 
                    // For now, just preserve what the model gave, but clean whitespace.
                    builder.AppendLine($"- {cleanLine}");
                }
                else
                {
                    builder.AppendLine(cleanLine);
                }
            }
        }

        return builder.ToString().TrimEnd();
    }

    public async ValueTask DisposeAsync()
    {
        _connectionLock.Dispose();
        var sessionsToDestroy = _joinedSessions.Keys.ToList();
        foreach (var sessionId in sessionsToDestroy)
        {
            await DestroySessionAsync(sessionId, CancellationToken.None);
        }

        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }

        _activeChannels.Clear();
        _activeCompletions.Clear();
    }
}