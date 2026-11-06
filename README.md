# Instancium Publisher

[![License: Apache 2.0](https://img.shields.io/badge/license-Apache2.0-blue.svg)](LICENSE)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Client for InstantAIGate](https://img.shields.io/badge/Client%20for-InstantAIGate-007acc.svg)](https://github.com/Instancium/instant-ai-gate)

**Autonomous TUI tool for .NET release automation with AI-powered commit messages and changelogs.**

Instancium Publisher eliminates the friction between writing code and shipping releases. It orchestrates Git workflows, semantic versioning, and publishing pipelines — all backed by real-time LLM inference through [InstantAIGate](https://github.com/Instancium/instant-ai-gate).


## What It Does

>
> Instancium Publisher replaces fragile, black-box server-side release scripts with a robust **Local-First Orchestration** model. By shifting AI generation and workflow validation directly to the developer's workstation, it guarantees complete human-in-the-loop oversight before a single remote ref is altered.
>

- **Shift-Left AI Validation:** Optimizes and cleanses raw Git diffs directly on the source machine, preventing "garbage-in, garbage-out" scenarios before sending payloads to the LLM gateway.
- **Elimotes Upstream Desyncs:** Generates and validates AI commits locally, completely eliminating the "non-fast-forward" push errors and detached-HEAD states common with autonomous server-side CI tagging.
- **Interactive AI Commit Generation:** Streams LLM tokens in real-time **at the exact moment you prepare a commit**. This guarantees strict human-in-the-loop validation, requiring your explicit approval before the AI-generated Conventional Commit message is ever written to your local Git history.
- **Noise-Filtered Changelogs:** Analyzes local commit logs, automatically stripping out version bumps, merge commits, and dependency updates to compile clean, human-readable release notes.
- **Deterministic SemVer 2.0 Management:** Provides full lifecycle version bumping (including stable, pre-release, and custom strategies) with deep validation of XML project manifests.
- **Stateful Git Recovery:** Operates as a local transactional State Machine. If a network drop or API error interrupts a multi-branch release, the tool safely resumes from the last checkpoint or executes an atomic rollback.

## Why Use It

### The Problem with Server-Side Release Automation

Entrusting AI generation and multi-branch Git orchestration to remote CI/CD runners introduces significant operational hurdles:
- **Blind AI Commits:** Server-side LLM steps often produce out-of-context or inaccurate commit messages that bypass human review until they hit the main branch.
- **Brittle Bash Orchestration:** CI pipelines rely on long, unmaintainable shell scripts that lack proper state management, turning mid-release failures into manual cleanup nightmares.
- **Distributed State Conflicts:** When a server-side runner bumps versions and pushes tags back to the repository, it frequently breaks local developer working trees due to branch desynchronization.
- **Context Window Dilution:** Pushing raw, unoptimized diffs (including lockfiles and minified assets) to remote AI endpoints inflates token costs and degrades LLM performance.
- **Version Bumping is a Chore:** Synchronizing versions across multiple `.csproj` files relies on tedious manual updates, error-prone find-and-replace, or over-engineered build scripts.

### The Solution: Local-First Control

Instancium Publisher brings predictability, transactionality, and human verification back to the release process:

1. **Stage & Optimize** → The tool slices out noise from your diff (`-w`, `-U0`, hunk truncation) and feeds a clean payload to the local AI client.
2. **Review In-Flight** → You validate and approve the AI-generated conventional commit message *before* it gets baked into the Git history.
3. **Transactional Bump & Prep** → The internal state machine locks the session, creates a `release-prep/` branch, and updates internal manifests deterministically.
4. **Isolated Release Execution** → Merging, tag creation, and GitHub Release compilation happen through a sequence of atomic local operations.
5. **Fail-Safe Recovery** → If a step fails (e.g., GitHub API timeout), your environment isn't corrupted. You can **Resume** precisely where you left off, or trigger a **Rollback** to perform an atomic `git reset --hard`.

All heavy lifting is delegated to an internal `VersionEngine` and an ephemeral connection to InstantAIGate — giving you serverless, deterministic automation right in your TUI.


## Key Features

### Zero-Toil Repository Versioning
Version management across multi-project .NET solutions is notoriously painful and error-prone. Instancium Publisher eliminates this headache entirely. Its internal `VersionEngine` natively parses XML project manifests (`.csproj`) and aligns versions across the entire repository in milliseconds. 
It mathematically handles complex SemVer 2.0 transitions—from pre-release increments (`-rc.1` → `-rc.2`) to stable promotions—guaranteeing zero version drift without the need for brittle MSBuild scripts or regex hacks.

### Interactive TUI Navigation
Hierarchical Spectre.Console interface for switching between repositories and workspaces. View active branch, pending changes, and project context at a glance.

### InstantAIGate Integration
Native SignalR client for `/hub/gateway`. Streams inference tokens for commit messages and changelogs. Supports KV-cache checkpointing for large diffs that exceed context windows.

### Deterministic SemVer 2.0 Engine
Full lifecycle version management:
- Stable bumps: `1.0.0` → `1.0.1` (Patch), `1.1.0` (Minor), `2.0.0` (Major)
- Pre-release progression: `1.0.0` → `1.0.1-rc.1` → `1.0.1-rc.2` → `1.0.1`
- Custom version strings for edge cases

### Transactional Execution & Safe Rollbacks
Session state persistence allows interrupted releases to resume or safely perform destructive rollbacks. Your Git history is protected.

### AI Changelog Sanitization & Payload Optimization
Built-in filters automatically strip release-engineering noise and optimize payloads before LLM processing:
- **Diff payload optimization:** Strips whitespace, limits context lines, and truncates massive hunks to prevent LLM attention dilution.
- **Commit log sanitization:** Ignores version bump commits, backport operations, merge commits from release branches, and generated files (`.min.js`, lock files, binaries).


## Installation

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git 2.30+ in `PATH`
- GitHub CLI (`gh`) authenticated in `PATH` (for release publishing)
- Running InstantAIGate instance with `/hub/gateway` endpoint accessible

### Build from Source

```bash
git clone [https://github.com/instancium/publisher.git](https://github.com/instancium/publisher.git)
cd Instancium.Publisher
dotnet build src/Instancium.Publisher/Instancium.Publisher.csproj -c Release

```

## ⚙️ Configuration

Create `appsettings.json` in the project root:

```json
{
  "PublisherSettings": {
    "AiGatewayUrl": "http://localhost:5000",
    "AiModelId": "qwen3-vl-8b-instruct",
    "AiApiKey": "your-instantaigate-access-token",
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

### Configuration Options

| Setting | Default | Description |
| --- | --- | --- |
| `AiGatewayUrl` | `http://localhost:5000` | InstantAIGate server URL |
| `AiModelId` | `qwen3-vl-8b-instruct` | Model identifier on the gateway |
| `AiApiKey` | `""` | Bearer token for SignalR authentication |
| `MaxChunkLength` | `3000` | Configuration chunk size limit |
| `DiffOptimization.Enabled` | `true` | Enables Git diff payload sanitization |
| `DiffOptimization.ContextLines` | `0` | Number of context lines in Git diff (`-U0`) |
| `DiffOptimization.IgnoreWhitespace` | `true` | Ignores whitespace changes (`-w --ignore-blank-lines`) |
| `DiffOptimization.MaxHunkLines` | `80` | Maximum continuous lines to show before truncating the hunk |
| `RegisteredProjects` | `{}` | Dictionary of project name → repository root directory path |

## Usage

InstantPublisher is an **interactive TUI application** — no CLI subcommands.

```bash
# Launch the interactive dashboard
dotnet run --project src/Instancium.Publisher/Instancium.Publisher.csproj

# Or run the compiled binary
./Instancium.Publisher

```

### Workflow

1. **Select Target Repository** — Choose from `RegisteredProjects` in `appsettings.json`
2. **View Dashboard** — Active branch, pending changes, target path
3. **Select Workspace**:
* **Commit Control Center** — Stage files, generate AI commit message, push to remote
* **Release Master & Recovery** — End-to-end release pipeline with rollback capability



### Commit Control Center

1. Stages all changes (`git add -A`)
2. Analyzes the diff, applies configured sanitization rules, and sends to AI
3. Streams generated commit message in real-time
4. User approves or aborts
5. Commits and pushes to current branch

### Release Master & Recovery

1. Reads current version by discovering all `.csproj` files in the repository root
2. User selects version bump strategy
3. Bumps version in project files across the repository
4. Creates release branch (`release-prep/v{version}`)
5. Generates changelog from commit history (filtered by AI)
6. User approves changelog
7. Merges to `main`, creates tag, pushes
8. Creates GitHub Release via `gh` CLI
9. Backports to source branch if different from `main`
10. Cleans up release branch

**Recovery**: If the process crashes or is interrupted, state is saved to disk. On restart, you can:

* **Resume** — Continue from the last checkpoint
* **Rollback** — Destructive reset to clean state
* **Start Fresh** — Discard state and begin new release

## Safety & Validation

* **Clean Working Tree**: Publishing pipelines reject execution if uncommitted changes are present
* **State Persistence**: Interrupted releases can be resumed or rolled back
* **Ephemeral AI Sessions**: All inference sessions are marked ephemeral — KV-cache is released on disconnect
* **Destructive Rollback**: Requires explicit user confirmation; performs `git reset --hard` and `git clean -fd`
* **Deterministic Merge Strategy**: During release and backport operations, the pipeline automatically resolves any Git conflicts in favor of the incoming release branch. This ensures that the exact versions and file states prepared by the pipeline are enforced without manual intervention, while perfectly preserving the Git merge history graph.

## Architecture Overview

Instancium Publisher is **decoupled from inference runtimes**. It does not embed `llama.cpp` or local weight loaders. All AI tasks are delegated to InstantAIGate via SignalR.

```
┌─────────────────────────────────┐                 ┌──────────────────────────────────┐
│        Instancium.Publisher     │                 │       InstantAIGate.Server       │
│  (Isolated Release & CLI Tool)  │                 │  (Inference Engine & LLM Host)   │
└────────────────┬────────────────┘                 └────────────────┬─────────────────┘
                 │                                                   │
                 │ 1. Connect via HubConnection (Bearer Auth)        │
                 ├──────────────────────────────────────────────────>│
                 │ 2. JoinSession(sessionId, modelId)                │
                 ├──────────────────────────────────────────────────>│
                 │ 3. SendPromptDelta(sessionId, gitDiffOrLog)       │
                 ├──────────────────────────────────────────────────>│
                 │                                                   │
                 │ 4. ReceiveTokenDelta (Streamed chunks)            │
                 │< - - - - - - - - - - - - - - - - - - - - - - - - -┤
                 │ 5. LeaveSession / DestroySession                  │
                 ├──────────────────────────────────────────────────>│

```

## .NET Ecosystem Focus

**Important**: Instancium Publisher is designed exclusively for the .NET ecosystem.

The internal `VersionEngine` parses XML-based project manifests (`.csproj`, `Directory.Build.props`) to read and update the `<Version>` tag. Support for non-XML manifests (e.g., `package.json` for Node.js) is **not implemented**.


## ⚠️ Versioning & Stability Policy

Instancium Publisher follows a pragmatic variant of Semantic Versioning.

### Pre-2.0 Status
Until version 2.0.0 is released, the tool is considered to be in a stabilization track (1.x line) until the entire product ecosystem reaches its goals. The public CLI behavior and configuration schemas are not strictly frozen.

### Version Components

| Segment | Meaning |
| :--- | :--- |
| **Major** (2.x.y) | Reserved for the first stable release (2.0.0). Subsequent major updates reflect fundamental architectural shifts or major roadmap milestones. |
| **Minor** (x.Y.0) | Will be incremented when changes are made to configuration files (e.g., `appsettings.json`), launch strategies, or integrations with external systems. Review the release notes before upgrading. |
| **Patch** (x.y.Z) | Bug fixes that do not alter the integration rules. Existing configurations remain safe to use. |


## 📄 License

This project is licensed under the Apache License 2.0 — see the [LICENSE](https://www.google.com/search?q=LICENSE) file for details.

## 📞 Support

For issues, feature requests, or questions, please [open an issue](https://www.google.com/search?q=https://github.com/instancium/Publisher/issues).

