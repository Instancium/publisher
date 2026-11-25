# Instancium Publisher

[![License: Apache 2.0](https://img.shields.io/badge/license-Apache2.0-blue.svg)](LICENSE.txt)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Client for InstantAIGate](https://img.shields.io/badge/Client%20for-InstantAIGate-007acc.svg)](https://github.com/Instancium/instant-ai-gate)
[![Signed with Instancium.Trust](https://img.shields.io/badge/Signed%20with-Instancium.Trust-2ea44f.svg)](https://github.com/Instancium/trust)

**Local-first TUI release orchestrator with AI-powered commits/changelogs, declarative multi-ecosystem artifact packaging, and cryptographic release signing.**

Instancium Publisher eliminates the friction between writing code and shipping verifiable releases. It orchestrates Git workflows, repository-wide semantic versioning, declarative artifact builds, Ed25519 manifest signing via [Instancium.Trust](https://github.com/Instancium/trust), and GitHub Releases — backed by local LLM inference through [InstantAIGate](https://github.com/Instancium/instant-ai-gate).

## What It Does

> Instancium Publisher replaces fragile, black-box server-side release scripts with a **Local-First Orchestration** model. Shifting AI generation, artifact signing, and workflow validation to the developer workstation guarantees human-in-the-loop oversight and keeps signing keys off remote CI runners.

- **Shift-Left AI Validation:** Sanitizes and optimizes raw Git diffs locally before payloads reach the LLM gateway, avoiding context dilution and inaccurate summaries.
- **Interactive AI Commit Generation:** Streams Conventional Commit messages in real time and requires explicit approval before writing to Git history. Preserves partial staging when files are already indexed.
- **Noise-Filtered Changelogs:** Reads structured Git commit records (`--no-merges`, subject, body, compact stat) and atomically drops release-engineering noise (version bumps, merges, backports) before generating release notes.
- **Deterministic SemVer 2.0 Management:** Resolves the repository version across all discovered project manifests and aligns every project to the target version on release.
- **Declarative Artifact Building & Cryptographic Signing:** Reads `instancium.publish.json` from the target repository, executes ecosystem build commands (`.NET`, `Node`, `Rust`, `Python`), packages binaries and external runtime drivers into `.zip` archives with SHA-256 hashes, and signs `manifest.json` + `manifest.sig` using a local `Instancium.Trust` Release Key container.
- **Stateful Git Recovery:** Journals every release phase outside the Git working tree (`%TEMP%/Instancium.Publisher/`). Interrupted releases can be resumed without re-bumping versions or rolled back cleanly.

## Key Features

### Zero-Toil Repository Versioning

Discovers project manifests under the repository root via `IProjectEcosystem` (MSBuild `*.csproj` out of the box; skipping `bin`, `obj`, `.git`, `.vs`, `node_modules`, and hidden directories). Resolves the current repository version as the **highest declared SemVer** and aligns every project to the new version on release — including projects without a prior `<Version>` element.

### Deterministic SemVer 2.0 Engine

Built on pluggable `IVersionBumpStrategy` components:

- Stable bumps: `Patch`, `Minor`, `Major`
- Pre-release progression: `Pre-Release Iteration`, `Pre-Release Patch`, `Pre-Release Minor`, `Pre-Release Major`, `Promote To Release`
- Explicit `Custom` SemVer 2.0 input with strict validation

### Declarative Build, Packaging & Zero-Trust Signing

When a target repository contains `instancium.publish.json`, the release pipeline automatically:

1. Runs configured build targets (with `{version}`, `{stageDir}`, `{outputDir}`, and `{repoRoot}` token expansion) into an isolated staging directory under `%TEMP%/Instancium.Publisher/staging/`.
2. Packages target outputs and external assets (such as Vulkan or `llama.cpp` runtime drivers) into `.zip` archives and computes streaming SHA-256 digests.
3. Unlocks the local Argon2id + AES-256-GCM Release Key container via `Instancium.Trust`, validates `release-key.cert` expiration, emits `manifest.json` and detached `manifest.sig`, and wipes private key bytes from memory in `finally`.
4. Uploads all staged archives, `manifest.json`, and `manifest.sig` to the GitHub Release via `gh release create`.

Repositories without `instancium.publish.json` skip the build/sign phase cleanly without prompting for a signing password.

### InstantAIGate Integration

Built on the official `InstantAIGate.Client` SDK (SignalR `/hub/gateway`). Streams inference tokens for commit messages and changelogs, with automatic reconnect and SDK-managed KV-cache checkpointed Map-Reduce for oversized diffs and commit logs.

### Network Resilience & Process Safety

Remote Git operations retry up to 4 times with exponential backoff on transient failures (HTTP 5xx, RPC errors, connection resets). Hung child processes are terminated by a 30-second idle timeout, and stale `.git/index.lock` files are cleaned up before retrying.

## Installation

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git 2.30+ in `PATH`
- A running [InstantAIGate](https://github.com/Instancium/instant-ai-gate) instance with `/hub/gateway` accessible
- GitHub CLI (`gh`) authenticated in `PATH` — **optional**. Without it, tags and branches are still pushed and release notes are exported locally

### Build & Test

```bash
git clone [https://github.com/Instancium/Publisher.git](https://github.com/Instancium/Publisher.git)
cd Publisher
dotnet build Instancium.Publisher.slnx -c Release
dotnet test Instancium.Publisher.slnx -c Release
```

## Configuration

Publisher loads `appsettings.json` from the **executable directory**. When building from source, edit `src/Instancium.Publisher.Tui/appsettings.json` (copied to output on build).

```json
{
  "PublisherSettings": {
    "TargetBranch": "main",
    "ReleaseBranchPrefix": "release-prep/v",
    "AiGatewayUrl": "http://localhost:5000",
    "AiModelId": "qwen3-4b-instruct",
    "AiApiKey": "",
    "MaxChunkLength": 10000,
    "DiffOptimization": {
      "Enabled": true,
      "ContextLines": 3,
      "IgnoreWhitespace": true,
      "MaxHunkLines": 80
    },
    "Signing": {
      "ReleaseKeyContainerPath": "",
      "ReleaseCertificatePath": "",
      "ReleaseKeyId": "",
      "PasswordEnvironmentVariable": "INSTANCIUM_RELEASE_KEY_PASSWORD"
    },
    "RegisteredProjects": {
      "MyAwesomeApp": "C:\\path\\to\\MyAwesomeApp"
    }
  }
}
```

**Keep secrets out of source control.** Override secrets via environment variables:

```powershell
# Windows (PowerShell)
$env:PublisherSettings__AiApiKey = "your-instantaigate-access-token"
$env:INSTANCIUM_RELEASE_KEY_PASSWORD = "your-release-key-container-password"
```

```bash
# Linux / macOS
export PublisherSettings__AiApiKey="your-instantaigate-access-token"
export INSTANCIUM_RELEASE_KEY_PASSWORD="your-release-key-container-password"
```

If `INSTANCIUM_RELEASE_KEY_PASSWORD` is not set, Publisher falls back to a masked interactive terminal prompt when signing is required.

### Configuration Options

| Setting | Default | Description |
| :--- | :--- | :--- |
| `TargetBranch` | `main` | Branch into which release branches are merged and tagged |
| `ReleaseBranchPrefix` | `release-prep/v` | Prefix for temporary release preparation branches |
| `AiGatewayUrl` | `http://localhost:5000` | InstantAIGate server base URL |
| `AiModelId` | `qwen3-vl-8b-instruct` | Target model id on the gateway (also display fallback when telemetry is unavailable) |
| `AiApiKey` | `""` | Bearer token for SignalR authentication |
| `MaxChunkLength` | `3000` | Upper bound (characters) per AI payload chunk. Effective limit = `min(MaxChunkLength, modelContextBudget, 15000)` |
| `DiffOptimization.Enabled` | `true` | Enables Git diff header stripping, hunk truncation, and pathspec exclusions |
| `DiffOptimization.ContextLines` | `3` | Unified diff context lines (`-U{n}`) |
| `DiffOptimization.IgnoreWhitespace` | `true` | Passes `-w --ignore-blank-lines` to `git diff` |
| `DiffOptimization.MaxHunkLines` | `80` | Maximum lines per diff hunk before truncation |
| `Signing.ReleaseKeyContainerPath` | `""` | Path to encrypted `release-key.key.json` container |
| `Signing.ReleaseCertificatePath` | `""` | Path to signed `release-key.cert` envelope (preferred; validates expiry and extracts `kid`) |
| `Signing.ReleaseKeyId` | `""` | Fallback `kid` when `ReleaseCertificatePath` is not configured |
| `Signing.PasswordEnvironmentVariable` | `INSTANCIUM_RELEASE_KEY_PASSWORD` | Environment variable checked before prompting interactively for the container password |
| `RegisteredProjects` | `{}` | Map of display name to repository root directory (or `.csproj` path). `Exit` is reserved |

### Declarative Release Manifest (`instancium.publish.json`)

Place `instancium.publish.json` at the root of a target repository to enable automated artifact building, packaging, and signing:

```json
{
  "product": "Instancium.Vectra",
  "targets": [
    {
      "name": "win-x64",
      "command": "dotnet",
      "arguments": ["publish", "src/App/App.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "-o", "{stageDir}"],
      "archiveName": "vectra-{version}-win-x64.zip"
    }
  ],
  "externalAssets": [
    {
      "sourcePath": "drivers/vulkan",
      "archiveName": "vulkan-runtime-{version}.zip"
    }
  ]
}
```

## Usage

```bash
# Launch the interactive TUI
dotnet run --project src/Instancium.Publisher.Tui/Instancium.Publisher.Tui.csproj -c Release

# Or run the compiled binary
./src/Instancium.Publisher.Tui/bin/Release/net10.0/Instancium.Publisher      # Linux/macOS
.\src\Instancium.Publisher.Tui\bin\Release\net10.0\Instancium.Publisher.exe  # Windows
```

### Commit Control Center

1. Checks the Git index first; if nothing is staged, runs `git add -A`.
2. Optimizes the cached diff and streams a Conventional Commit message from InstantAIGate.
3. Prompts for approval or abort (abort keeps files staged).
4. Sanitizes the message, commits locally, and pushes with an interactive retry loop.
5. If the working tree is clean but unpushed commits exist, offers a push-only path.

### Release Master & Recovery

1. **Step 1 — Inspect & Bump:** Discovers project manifests, prompts for a bump strategy with `current -> next` preview, checks out `{ReleaseBranchPrefix}{version}`, journals the plan, and writes the new version to all projects.
2. **Step 2 — AI Changelog:** Collects structured commit history (`--merged HEAD` tag resolution; stable releases aggregate across pre-release tags), streams release notes, appends the tool/model signature footer, and prompts for approval.
3. **Step 3 — Build & Sign:** If `instancium.publish.json` is present, builds targets, packages archives/drivers into temp staging, generates `manifest.json` + `manifest.sig` via `Instancium.Trust`, and journals asset paths.
4. **Step 4 — Merge & Publish:** Commits pending version updates (`chore(release): prepare version v{version}`), pulls `TargetBranch`, merges (fast-forward first, forced fallback on divergence), tags and pushes `v{version}`, backports to the source branch if different, publishes the GitHub Release with all signed assets via `gh`, cleans up the release branch, and clears the pipeline journal.

**GitHub CLI fallback:** If `gh` is unavailable or unauthenticated, tags and branches remain pushed and release notes are exported to `%TEMP%/Instancium.Publisher/RELEASE_NOTES_{tag}.md`.

## Architecture Overview

The solution follows a strict 4-layer Clean Architecture:

| Project | Target Frameworks | Responsibility |
| :--- | :--- | :--- |
| `Instancium.Publisher.Domain` | `net8.0;net10.0` | `SemanticVersion`, `RepositoryVersionPolicy`, `IVersionBumpStrategy` catalog, domain exceptions |
| `Instancium.Publisher.Application` | `net8.0;net10.0` | Workflows (`CommitWorkflow`, `ReleaseWorkflow`, `ReleaseRecoveryService`), pipeline steps (`BumpVersionStep`, `BuildAndSignStep`, `MergeAndPublishStep`), ports (`Abstractions`) |
| `Instancium.Publisher.Infrastructure` | `net10.0` | `GitService`, `SdkAiCommitAssistant`, `MsBuildProjectEcosystem`, `DeclarativeArtifactBuilder`, `LocalReleaseKeySigner` (`Instancium.Trust`), `GitHubCliReleasePublisher`, `PipelineStateManager` |
| `Instancium.Publisher.Tui` | `net10.0` | Spectre.Console host, `MainTuiOrchestrator`, `CommitWorkspace`, `ReleaseWorkspace`, `TuiSecretProvider`, `TuiProgressReporter` |

All build settings and NuGet package versions are declared explicitly in each `.csproj` without central `.props` files. `Instancium.Trust` is isolated to `Infrastructure` so `Domain` and `Application` remain multi-targeted (`net8.0;net10.0`).

## Roadmap

- **Document Signing Workspace:** Dedicated TUI workspace (`DocumentSigningWorkspace`) for signing standalone artifacts (such as `Instancium_Doctrine.pdf`) into `doctrine.manifest.json` + `doctrine.sig` with optional Git publication.
- **Additional Project Ecosystems:** `IProjectEcosystem` implementations for Node (`package.json`), Rust (`Cargo.toml`), and Python (`pyproject.toml`).
- **Optional Binary Code Signing:** Pluggable Authenticode signer for Windows executables alongside `Instancium.Trust` manifest signing.

## Versioning & Stability Policy

Instancium Publisher follows Semantic Versioning (1.x pre-2.0 stabilization track):

- **Major (`2.x.y`):** First stable 2.0.0 release and major architectural milestones.
- **Minor (`x.Y.0`):** Configuration schema changes, new workspaces, or external integration updates.
- **Patch (`x.y.Z`):** Backward-compatible bug fixes.

## License

Licensed under the Apache License 2.0 — see [LICENSE.txt](LICENSE.txt).

## Support

For issues, feature requests, or questions, please [open an issue](https://github.com/Instancium/Publisher/issues).