# Instancium.Publisher

AI-powered release and commit automation CLI built on a layered, extensible .NET architecture.

## Architecture

```
Instancium.Publisher.Tui            (net10.0, Exe: Instancium.Publisher)
        │
        ▼
Instancium.Publisher.Infrastructure (net10.0: Git, AI, MSBuild, Process, State, Config)
        │  implements ports
        ▼
Instancium.Publisher.Application    (net8.0;net10.0: Ports, Steps, Workflows, Catalogs)
        │
        ▼
Instancium.Publisher.Domain         (net8.0;net10.0: SemVer, Policies, Bump Strategies)
```

## Project Map

| Project | Target(s) | Responsibility |
|---|---|---|
| `src/Instancium.Publisher.Domain` | `net8.0;net10.0` | `SemanticVersion`, `RepositoryVersionPolicy`, `IVersionBumpStrategy` + 9 built-in strategies |
| `src/Instancium.Publisher.Application` | `net8.0;net10.0` | Ports (`IGitService`, `IProjectEcosystem`, `IReleasePublisher`, ...), `CommitWorkflow`, `ReleaseWorkflow`, `ReleaseRecoveryService` |
| `src/Instancium.Publisher.Infrastructure` | `net10.0` | `GitService`, `SdkAiCommitAssistant`, `MsBuildProjectEcosystem`, `RepositoryProjectScanner`, `GitHubCliReleasePublisher`, `ProcessExecutionService` |
| `src/Instancium.Publisher.Tui` | `net10.0` | Spectre.Console host, `MainTuiOrchestrator`, thin `CommitWorkspace` & `ReleaseWorkspace` |
| `tests/Instancium.Publisher.Tests` | `net10.0` | Unit and architecture guard tests |

## Configuration

Configure `PublisherSettings:AiApiKey` via environment variable before running AI commands:

```powershell
$env:PublisherSettings__AiApiKey = "your-api-key"
```

## Extending

### Add a Version Bump Strategy
1. Implement `IVersionBumpStrategy` in `Domain` (or any referencing assembly).
2. Register it in DI: `services.AddSingleton<IVersionBumpStrategy, MyStrategy>();`.

### Add a Project Ecosystem
1. Implement `IProjectEcosystem` (`ManifestFilePatterns`, `ExcludedDirectoryNames`, `ReadVersionAsync`, `WriteVersionAsync`).
2. Register it in DI: `services.AddSingleton<IProjectEcosystem, NpmProjectEcosystem>();`.

### Add a Release Publisher
1. Implement `IReleasePublisher` (`PublishAsync(ReleasePublicationRequest, CancellationToken)`).
2. Register it in DI: `services.AddSingleton<IReleasePublisher, MyReleasePublisher>();`.

