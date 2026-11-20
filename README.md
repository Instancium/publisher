# Instancium Publisher

[![License: Apache 2.0](https://img.shields.io/badge/license-Apache2.0-blue.svg)](LICENSE.txt)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Client for InstantAIGate](https://img.shields.io/badge/Client%20for-InstantAIGate-007acc.svg)](https://github.com/Instancium/instant-ai-gate)

**Autonomous TUI tool for .NET release automation with AI-powered commit messages and changelogs.**

Instancium Publisher eliminates the friction between writing code and shipping releases. It orchestrates Git workflows, repository-wide semantic versioning, and GitHub publishing — all backed by real-time local LLM inference through [InstantAIGate](https://github.com/Instancium/instant-ai-gate).

## What It Does

> Instancium Publisher replaces fragile, black-box server-side release scripts with a robust **Local-First Orchestration** model. By shifting AI generation and workflow validation directly to the developer's workstation, it guarantees human-in-the-loop oversight before a single remote ref is altered.

- **Shift-Left AI Validation:** Optimizes and cleanses raw Git diffs directly on the source machine, preventing "garbage-in, garbage-out" scenarios before payloads ever reach the LLM gateway.
- **Prevents Upstream Desyncs:** Version bumps and tags are created locally on top of your actual working state. The pipeline pulls the target branch before merging and retries transient push failures — no detached-HEAD surprises caused by remote runners pushing back into your repo.
- **Interactive AI Commit Generation:** Streams LLM tokens in real-time **at the exact moment you prepare a commit**. Your explicit approval is required before the AI-generated Conventional Commit message is written to your local Git history.
- **Noise-Filtered Changelogs:** Analyzes local commit logs, automatically stripping version bumps, merge commits, and backport chores to compile clean, human-readable release notes.
- **Deterministic SemVer 2.0 Management:** Full lifecycle version bumping (stable, pre-release, and custom strategies) with deep validation of XML project manifests across the entire repository.
- **Stateful Git Recovery:** Operates as a local transactional state machine. If a network drop or API error interrupts a multi-branch release, the tool safely resumes from the last checkpoint or executes an explicit rollback.

## Why Use It

### The Problem with Server-Side Release Automation

Entrusting AI generation and multi-branch Git orchestration to remote CI/CD runners introduces significant operational hurdles:

- **Blind AI Commits:** Server-side LLM steps often produce out-of-context commit messages that bypass human review until they hit the main branch.
- **Brittle Bash Orchestration:** CI pipelines rely on long, unmaintainable shell scripts that lack proper state management, turning mid-release failures into manual cleanup nightmares.
- **Distributed State Conflicts:** When a server-side runner bumps versions and pushes tags back to the repository, it frequently breaks local developer working trees due to branch desynchronization.
- **Context Window Dilution:** Pushing raw, unoptimized diffs (including lockfiles and minified assets) to AI endpoints inflates token costs and degrades LLM output quality.
- **Version Bumping is a Chore:** Synchronizing versions across multiple `.csproj` files relies on tedious manual updates, error-prone find-and-replace, or over-engineered build scripts.

### The Solution: Local-First Control

1. **Stage & Optimize** → The tool slices noise out of your diff (`-w`, `-U0`, hunk truncation, generated-file skipping) and feeds a clean payload to the local AI client.
2. **Review In-Flight** → You validate and approve the AI-generated Conventional Commit message *before* it gets baked into Git history.
3. **Transactional Bump & Prep** → The state machine journals a checkpoint, creates a `release-prep/v{version}` branch, and stamps the new version into every project manifest deterministically.
4. **Isolated Release Execution** → Merging, tagging, backporting, and GitHub Release creation happen as a sequence of local operations with retry support.
5. **Fail-Safe Recovery** → If a step fails, your environment isn't corrupted. **Resume** precisely where you left off, or trigger an explicit **Rollback**.

All heavy lifting stays on your machine: an internal versioning engine plus an ephemeral connection to a local InstantAIGate instance — serverless, deterministic automation right in your terminal.

## Key Features

### Zero-Toil Repository Versioning

Version management across multi-project .NET solutions is painful and error-prone. Publisher discovers every `.csproj` under the repository root (skipping `bin`, `obj`, `.git`, `.vs`, `node_modules`, and hidden directories), resolves the repository version as the **highest declared SemVer** among projects, and aligns **every** project to the new version on release — including projects that never declared a version. No brittle MSBuild scripts, no regex hacks.

### Interactive TUI Navigation

Hierarchical Spectre.Console interface for switching between registered repositories and workspaces. Active branch, pending changes, unpushed commits, and project context at a glance.

### InstantAIGate Integration

Built on the official `InstantAIGate.Client` SDK (SignalR, `/hub/gateway`). Streams inference tokens for commit messages and changelogs, with automatic reconnect and SDK-managed KV-cache checkpointed Map-Reduce for diffs and logs that exceed the model context window.

### Rename-Aware, Status-Accurate AI Payloads

Diffs are collected with rename detection (`-M`) and per-file status extraction (`ADDED` / `MODIFIED` / `DELETED` / `RENAMED`). Prompt construction enforces status accuracy: a modified file is never described as "newly added" in generated messages.

### Deterministic SemVer 2.0 Engine

Full lifecycle version management:

- Stable bumps: `1.0.0` → `1.0.1` (Patch), `1.1.0` (Minor), `2.0.0` (Major)
- Pre-release progression: `1.0.0` → `1.0.1-rc.1` → `1.0.1-rc.2` → `1.0.1`
- Pre-release Patch / Minor / Major starts, iteration bumps, promotion to stable
- Custom version strings for edge cases (strict SemVer 2.0 validation)

### Transactional Execution & Safe Rollbacks

Release state is journaled to `%TEMP%/Instancium.Publisher/` (one state file per repository, outside your Git tree). Interrupted releases can be resumed, rolled back, or restarted from scratch.

### Network Resilience

Remote Git operations retry up to 4 attempts with exponential backoff on transient errors (HTTP 5xx, RPC failures, connection resets). Hung processes are terminated by a 30-second idle timeout, and stale `.git/index.lock` files are cleaned up automatically before retrying.

### AI Changelog Sanitization & Payload Optimization

- **Diff payload optimization:** strips verbose Git headers, ignores whitespace, limits context lines, truncates massive hunks, and skips generated/binary payloads (lockfiles, `.min.js`, images, binaries).
- **Commit log sanitization:** ignores version bump commits, backport operations, and release merge chores.
- **Output cleanup:** strips stray Markdown fences from release notes and appends a signature footer with the tool version and the actual active model id reported by the gateway.

## Installation

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git 2.30+ in `PATH`
- A running [InstantAIGate](https://github.com/Instancium/instant-ai-gate) instance with `/hub/gateway` accessible
- GitHub CLI (`gh`) authenticated in `PATH` — **optional**. Without it, tags are still pushed and release notes are exported to a local file (see below)

### Build from Source

```bash
git clone https://github.com/Instancium/Publisher.git
cd Publisher
dotnet build Instancium.Publisher.slnx -c Release
```

### Run Tests

```bash
dotnet test tests/Instancium.Publisher.Tests/Instancium.Publisher.Tests.csproj -c Release
```

## ⚙️ Configuration

Publisher loads `appsettings.json` from the **executable's directory** (the file is required). When building from source, edit `src/Instancium.Publisher/appsettings.json` — it is copied to the output directory on build.

```json
{
  "PublisherSettings": {
    "AiGatewayUrl": "http://localhost:5000",
    "AiModelId": "qwen3-vl-8b-instruct",
    "AiApiKey": "",
    "MaxChunkLength": 3000,
    "DiffOptimization": {
      "Enabled": true,
      "ContextLines": 0,
      "IgnoreWhitespace": true,
      "MaxHunkLines": 80
    },
    "RegisteredProjects": {
      "MyAwesomeApp": "C:\\path\\to\\MyAwesomeApp",
      "AnotherRepo": "D:\\repos\\another-repo"
    }
  }
}
```

**Keep secrets out of source control.** Any setting can be overridden via environment variables using the `Section__Property` convention:

```powershell
# Windows (PowerShell)
$env:PublisherSettings__AiApiKey = "your-instantaigate-access-token"
```

```bash
# Linux / macOS
export PublisherSettings__AiApiKey="your-instantaigate-access-token"
```

### Configuration Options

| Setting | Default | Description |
| --- | --- | --- |
| `AiGatewayUrl` | `http://localhost:5000` | InstantAIGate server URL |
| `AiModelId` | `qwen3-vl-8b-instruct` | Model identifier on the gateway (display fallback; the footer uses the actual active model reported by telemetry) |
| `AiApiKey` | `""` | Bearer token for SignalR authentication |
| `MaxChunkLength` | `3000` | Upper bound (characters) for a single AI payload chunk. Effective limit = min(this value, model-context-derived budget, 15000-char transport cap) |
| `DiffOptimization.Enabled` | `true` | Enables Git diff payload sanitization |
| `DiffOptimization.ContextLines` | `0` | Context lines in Git diff (`-U0`) |
| `DiffOptimization.IgnoreWhitespace` | `true` | Ignores whitespace changes (`-w --ignore-blank-lines`) |
| `DiffOptimization.MaxHunkLines` | `80` | Maximum continuous hunk lines before truncation |
| `RegisteredProjects` | `{}` | Dictionary of display name → repository root directory. A path to a `.csproj` file is also accepted (its directory is used). The name `Exit` is reserved by the navigation menu |
| `TargetBranch` | `main` | **Reserved.** Present in the schema but not yet enforced: the current pipeline always merges into `main` |
| `ReleaseBranchPrefix` | `release-prep/v` | **Reserved.** The current pipeline always uses `release-prep/v` |
| `AllowedBranchPrefixes` | `dev, feat/, fix/, hotfix/, rc/, rfc/` | **Reserved.** Branch policy enforcement is not yet implemented |

## Usage

Instancium Publisher is an **interactive TUI application** — no CLI subcommands.

```bash
# Launch the interactive dashboard
dotnet run --project src/Instancium.Publisher/Instancium.Publisher.csproj -c Release

# Or run the compiled binary
./src/Instancium.Publisher/bin/Release/net10.0/Instancium.Publisher      # Linux/macOS
.\src\Instancium.Publisher\bin\Release\net10.0\Instancium.Publisher.exe  # Windows
```

### Workflow

1. **Select Target Repository** — choose from `RegisteredProjects`
2. **View Dashboard** — active branch, pending changes, unpushed commits, target path
3. **Select Workspace**:
   - **Commit Control Center** — stage files, generate an AI commit message, push to remote
   - **Release Master & Recovery** — end-to-end release pipeline with rollback capability

### Commit Control Center

1. Stages **all** changes (`git add -A`) — commit or stash unrelated work first
2. Analyzes the cached diff, applies sanitization rules, and streams it to the AI
3. Streams the generated Conventional Commit message in real-time
4. You approve or abort (abort keeps files staged)
5. Commits locally, then pushes with an interactive retry loop — a failed push never loses your local commit
6. If the tree is clean but unpushed commits exist, offers a push-only path

### Release Master & Recovery

1. Discovers all `.csproj` files under the repository root and reads declared versions; the repository version is the highest declared one
2. You select a version bump strategy (with a live `current → next` preview)
3. Creates/checks out the release branch `release-prep/v{version}` (an existing branch is reused)
4. Journals a checkpoint, then stamps the new version into **every** project file
5. Generates the AI changelog from commit history: stable releases aggregate across pre-release tags; pre-releases cover only the delta since the previous tag; repositories without tags fall back to the last 20 commits
6. You approve, abort (state kept), or roll back
7. Any pending changes are auto-committed as `chore(release): prepare version v{version}`; the release branch is merged into `main`, an annotated tag `v{version}` is created and pushed
8. Backports the release to the source branch if it differs from `main` (fast-forward first, forced merge as fallback) and pushes it
9. Creates the GitHub Release via `gh` (with `--prerelease` for pre-release versions)
10. Returns to the source branch, deletes the release branch locally and remotely, and clears the state journal

**GitHub CLI fallback:** if `gh` is unavailable or unauthenticated, the tag and branches are already pushed — the tool exports the release notes to `%TEMP%/Instancium.Publisher/RELEASE_NOTES_{tag}.md` and warns, so you can create the release manually without losing anything.

**Recovery:** state is checkpointed to disk before every mutating phase. On restart after a crash you can:

- **Resume** — continue from the last checkpoint (the planned version is never re-bumped)
- **Rollback** — explicit destructive reset (see Safety)
- **Start Fresh** — roll back and begin a new release

## Safety & Validation

- **Human-in-the-Loop Gates:** Nothing is committed, merged, tagged, or published without your explicit approval at the corresponding prompt.
- **Pending Changes Handling:** Uncommitted changes present at publish time are auto-committed as `chore(release): prepare version v{version}` on the release branch — review your working tree before starting a release.
- **State Persistence:** Interrupted releases can be resumed or rolled back; the journal lives outside your Git tree, per repository.
- **Stateless AI Sessions:** Each inference request runs in a stateless chat session; no conversation state persists on the gateway, and KV-cache resources are released on disconnect.
- **Destructive Rollback:** Requires explicit menu selection. Restores the working tree (`git reset --hard` + `git clean -fd`), returns to the source branch, deletes the release branch locally and remotely, and clears the state journal.
- **Deterministic Merge Strategy:** During release and backport operations, conflicts are resolved in favor of the incoming release branch. The exact versions and file states prepared by the pipeline are enforced without manual intervention, while preserving the Git merge history graph.
- **Hung Process Protection:** A 30-second idle timeout terminates stalled Git/network processes, cleans stale `index.lock` files, and retries with backoff.

## Architecture Overview

Instancium Publisher is **decoupled from inference runtimes**. It does not embed `llama.cpp` or model weight loaders. All AI tasks are delegated to InstantAIGate through the official `InstantAIGate.Client` SDK: the SDK owns transport, session lifecycle, reconnect, and checkpointed Map-Reduce recovery; Publisher owns diff optimization, chunking, prompt construction, and output sanitization.

```text
┌───────────────────────────────────────┐          ┌────────────────────────────────────┐
│          Instancium.Publisher         │          │        InstantAIGate.Server        │
│      (Local release orchestration)    │          │      (Local LLM inference host)    │
│                                       │          │                                    │
│  TUI workspaces (Spectre.Console)     │          │  /hub/gateway  (SignalR hub)       │
│  Version engine · Git service         │          │  Stateless chat sessions           │
│  Pipeline state journal               │          │  KV-cache checkpointed Map-Reduce  │
│  InstantAIGate.Client SDK             │          │  Model telemetry                   │
└───────────────────┬───────────────────┘          └─────────────────┬──────────────────┘
                    │                                                │
                    │  1. Connect (Bearer token, auto-reconnect)     │
                    ├───────────────────────────────────────────────>│
                    │  2. StreamAsync(optimized diff / commit log)   │
                    ├───────────────────────────────────────────────>│
                    │  3. Token stream (real-time TUI rendering)     │
                    │<─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─┤
                    │  4. Session disposed (stateless, KV released)  │
                    ├───────────────────────────────────────────────>│
```

For oversized payloads, the chunk budget is resolved dynamically from the model's actual context size (via gateway telemetry), the configured `MaxChunkLength`, and a transport-safe ceiling — then processed through checkpointed Map-Reduce with live progress in the TUI.

## .NET Ecosystem Focus

**Important:** Instancium Publisher currently targets the .NET ecosystem exclusively.

The versioning engine discovers and updates the `<Version>` element in SDK-style **`.csproj` files only**. `Directory.Build.props`, `Directory.Packages.props`, and non-XML manifests (e.g., `package.json` for Node.js) are **not** read or modified. Keep the authoritative `<Version>` in your project files.

## Roadmap

Planned for the upcoming 1.x line (subject to change before 2.0.0):

- Local build step: `dotnet publish -c Release --self-contained` artifacts per release
- Artifact packaging: `.zip` archives with SHA-256 checksums
- Release registry: `manifest.json` + Ed25519 signature (`manifest.sig`), Authenticode signing of executables — verified client-side by the Instancium installer (zero-trust distribution)
- Layered core (Domain / Application / Infrastructure / UI) enabling additional front-ends (Visual Studio, MAUI) and pluggable bump strategies / project ecosystems

## ⚠️ Versioning & Stability Policy

Instancium Publisher follows a pragmatic variant of Semantic Versioning.

### Pre-2.0 Status

Until version 2.0.0 is released, the tool is on a stabilization track (1.x line) while the product ecosystem reaches its goals. Public CLI behavior and configuration schemas are not strictly frozen.

### Version Components

| Segment | Meaning |
| :--- | :--- |
| **Major** (2.x.y) | Reserved for the first stable release (2.0.0). Subsequent major updates reflect fundamental architectural shifts or major roadmap milestones. |
| **Minor** (x.Y.0) | Incremented when configuration schemas (e.g., `appsettings.json`), launch strategies, or external integrations change. Review the release notes before upgrading. |
| **Patch** (x.y.Z) | Bug fixes that do not alter integration rules. Existing configurations remain safe to use. |

## 📄 License

This project is licensed under the Apache License 2.0 — see the [LICENSE.txt](LICENSE.txt) file for details.

## 📞 Support

For issues, feature requests, or questions, please [open an issue](https://github.com/Instancium/Publisher/issues).