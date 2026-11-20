# ADR-0002: Extension Seams

## Context
Version bump strategies, project manifest discovery/parsing, and release publication targets were previously hardcoded via closed enums, `*.csproj` assumptions, and inline `gh` CLI calls.

## Decision
Introduce three explicit extension seams registered via `IEnumerable<T>` / DI:
- `IVersionBumpStrategy` in `Domain.Versioning` (with `KnownVersionBumpStrategies` constants and `IVersionBumpService` catalog in `Application`).
- `IProjectEcosystem` in `Application.Abstractions` (implemented by `MsBuildProjectEcosystem` in `Infrastructure`, consumed by `RepositoryVersionService` and `RepositoryProjectScanner`).
- `IReleasePublisher` in `Application.Abstractions` (implemented by `GitHubCliReleasePublisher` in `Infrastructure`).

External assembly loading (`AssemblyLoadContext`), a separate `Contracts` NuGet package, MAUI/VS hosts, and Security packaging (Ed25519/Authenticode/SHA-256) are explicitly deferred; no empty projects or stub implementations are created for them.

## Consequences
- New bump strategies, project ecosystems (e.g., npm, Python, Cargo), and release targets can be added by implementing an interface and registering it in DI without modifying core orchestration.
- Out-of-proc plugin loading can be layered on later without breaking in-proc contracts.
