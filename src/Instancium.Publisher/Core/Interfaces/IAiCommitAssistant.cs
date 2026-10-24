namespace Instancium.Publisher.Core.Interfaces;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Mode B Contract Update: Direct use of InstantAIGate.Core namespace not required here, 
// but implied in the implementation that references InstantAIGate.Core.Dtos.
public interface IAiCommitAssistant
{
    IAsyncEnumerable<string> StreamCommitMessageAsync(string diff, string diffStat, CancellationToken ct = default);
    Task<string> GenerateCommitMessageAsync(string diff, string diffStat, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamReleaseNotesAsync(string gitLog, CancellationToken ct = default);
}