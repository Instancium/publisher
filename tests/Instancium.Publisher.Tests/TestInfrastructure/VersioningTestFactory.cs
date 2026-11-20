using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.Versioning;
using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Domain.Versioning.Strategies;

namespace Instancium.Publisher.Tests.TestInfrastructure;

internal static class VersioningTestFactory
{
    public static IVersionBumpService CreateBumpService() =>
        new VersionBumpService(
        [
            new PatchBumpStrategy(),
            new MinorBumpStrategy(),
            new MajorBumpStrategy(),
            new PreReleaseIterationBumpStrategy(),
            new NewPreReleasePatchBumpStrategy(),
            new NewPreReleaseMinorBumpStrategy(),
            new NewPreReleaseMajorBumpStrategy(),
            new PromoteToReleaseBumpStrategy(),
            new CustomBumpStrategy()
        ]);
}
