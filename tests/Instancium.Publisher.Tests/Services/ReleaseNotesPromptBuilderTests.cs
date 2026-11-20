namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Infrastructure.Ai;
using Xunit;

/// <summary>
/// Verifies atomic commit-level release noise filtering so version bump stats
/// never leak into adjacent feature commits.
/// </summary>
public sealed class ReleaseNotesPromptBuilderTests
{
    [Fact]
    public void FilterReleaseNoise_StructuredLog_DropsReleaseCommitAtomicallyWithBodyAndStat()
    {
        string rawLog =
            "\u001ea1b2c3d\u001fchore(release): prepare version v1.7.0\u001fBump all csproj versions\u001f\n src/App.csproj | 2 +-\n 1 file changed" +
            "\u001ee4f5g6h\u001ffeat(git): enforce merged head tag lookup\u001f- Add --merged HEAD to git tag\n- Prevent cross-branch anchoring\u001f\n src/GitService.cs | 14 +++++++-------\n 1 file changed" +
            "\u001e9988776\u001fchore: backport release v1.7.0 to dev\u001f\u001f\n src/Domain.csproj | 2 +-";

        string filtered = ReleaseNotesPromptBuilder.FilterReleaseNoise(rawLog);

        Assert.DoesNotContain("prepare version", filtered);
        Assert.DoesNotContain("src/App.csproj", filtered);
        Assert.DoesNotContain("backport", filtered);
        Assert.DoesNotContain("src/Domain.csproj", filtered);

        Assert.Contains("e4f5g6h feat(git): enforce merged head tag lookup", filtered);
        Assert.Contains("- Add --merged HEAD to git tag", filtered);
        Assert.Contains("src/GitService.cs", filtered);
    }

    [Fact]
    public void FilterReleaseNoise_LegacyUnstructuredLog_FallsBackToLineFiltering()
    {
        string legacyLog = "a1b2c3d chore(release): prepare version v1.6.0\ne4f5g6h feat(ai): add chunker";

        string filtered = ReleaseNotesPromptBuilder.FilterReleaseNoise(legacyLog);

        Assert.DoesNotContain("prepare version", filtered);
        Assert.Contains("feat(ai): add chunker", filtered);
    }

    [Fact]
    public void BuildChangelogPrompt_EnforcesDomainGranularityWithoutSingleRowBias()
    {
        var message = ReleaseNotesPromptBuilder.BuildChangelogPrompt("a1b2c3d feat(git): improve tag lookup");

        Assert.DoesNotContain("It is expected and perfectly correct if the table contains only 1 row", message.Content);
        Assert.Contains("DO NOT collapse multiple unrelated changes or modules into a single generic row", message.Content);
    }
}