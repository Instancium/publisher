namespace Instancium.Publisher.Tests.Domain;

using System;
using Instancium.Publisher.Core.Domain;
using Xunit;

/// <summary>
/// Verifies the SemVer 2.0 value object: parsing, formatting and ordering rules
/// that the repository versioning policy relies upon.
/// </summary>
public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null, null)]
    [InlineData("v1.2.3", 1, 2, 3, null, null)]
    [InlineData("V10.20.30", 10, 20, 30, null, null)]
    [InlineData("1.2.3-rc.4", 1, 2, 3, "rc", 4)]
    [InlineData("1.0.0-alpha", 1, 0, 0, "alpha", null)]
    [InlineData("2.5.7-beta.12", 2, 5, 7, "beta", 12)]
    public void Parse_ValidStrings_ProducesExpectedComponents(
        string rawVersion,
        int expectedMajor,
        int expectedMinor,
        int expectedPatch,
        string? expectedTag,
        int? expectedIteration)
    {
        SemanticVersion version = SemanticVersion.Parse(rawVersion);

        Assert.Equal(expectedMajor, version.Major);
        Assert.Equal(expectedMinor, version.Minor);
        Assert.Equal(expectedPatch, version.Patch);
        Assert.Equal(expectedTag, version.PreReleaseTag);
        Assert.Equal(expectedIteration, version.PreReleaseIteration);
    }

    [Fact]
    public void Parse_Null_ThrowsArgumentNullException()
    {
        // Assert.Throws demands an exact type match: null input contracts to ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => SemanticVersion.Parse(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrWhitespace_ThrowsArgumentException(string rawVersion)
    {
        Assert.Throws<ArgumentException>(() => SemanticVersion.Parse(rawVersion));
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("abc")]
    [InlineData("1.2.3-")]
    [InlineData("v")]
    [InlineData("1.2.x")]
    public void Parse_InvalidStrings_ThrowsFormatException(string rawVersion)
    {
        Assert.Throws<FormatException>(() => SemanticVersion.Parse(rawVersion));
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-rc.4")]
    [InlineData("1.0.0-beta")]
    [InlineData("1.2.3+build.5")]
    public void ToString_RoundTripsOriginalString(string rawVersion)
    {
        SemanticVersion version = SemanticVersion.Parse(rawVersion);

        Assert.Equal(rawVersion, version.ToString());
    }

    [Theory]
    [MemberData(nameof(OrderedVersionPairs))]
    public void CompareTo_OrdersVersionsAccordingToSemVerRules(string lowerRaw, string higherRaw)
    {
        SemanticVersion lower = SemanticVersion.Parse(lowerRaw);
        SemanticVersion higher = SemanticVersion.Parse(higherRaw);

        Assert.True(lower.CompareTo(higher) < 0, $"Expected {lowerRaw} < {higherRaw}.");
        Assert.True(higher.CompareTo(lower) > 0, $"Expected {higherRaw} > {lowerRaw}.");
        Assert.Equal(0, lower.CompareTo(SemanticVersion.Parse(lowerRaw)));
    }

    public static TheoryData<string, string> OrderedVersionPairs() => new()
    {
        { "1.0.0", "1.0.1" },
        { "1.0.9", "1.0.10" },
        { "1.0.1", "1.1.0" },
        { "1.9.0", "2.0.0" },
        { "1.0.0-rc.1", "1.0.0" },
        { "1.0.0-rc.1", "1.0.0-rc.2" },
        { "1.0.0-alpha", "1.0.0-beta" }
    };

    [Fact]
    public void Constructor_NegativeComponents_ThrowArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticVersion(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticVersion(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticVersion(0, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticVersion(1, 0, 0, "rc", -1));
    }
}