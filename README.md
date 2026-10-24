# Instancium Publisher

[![License: Apache 2.0](https://img.shields.io/badge/license-Apache2.0-blue.svg)](LICENSE)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Client for InstantAIGate](https://img.shields.io/badge/Client%20for-InstantAIGate-007acc.svg)](https://github.com/Instancium/instant-ai-gate)

**Autonomous TUI tool for .NET release automation with AI-powered commit messages and changelogs.**

Instancium Publisher eliminates the friction between writing code and shipping releases. It orchestrates Git workflows, semantic versioning, and publishing pipelines — all backed by real-time LLM inference through [InstantAIGate](https://github.com/Instancium/instant-ai-gate).

---

## 🎯 What It Does

- **Generates commit messages** from staged diffs using AI, streaming tokens in real-time
- **Creates release changelogs** by analyzing commit history and filtering out noise (version bumps, backports, merge commits)
- **Manages semantic versions** with full SemVer 2.0 support (major/minor/patch, pre-releases, custom bumps)
- **Publishes releases** end-to-end: branch creation, merging, tagging, GitHub Release creation
- **Recovers from failures** with transactional state persistence — resume or rollback interrupted releases

## 🚀 Why Use It

### The Problem

Release engineering is repetitive and error-prone:
- Writing meaningful commit messages takes time
- Changelogs require manual curation to exclude noise
- Version bumping follows predictable patterns but is still manual
- Publishing involves multiple Git operations that can fail mid-way
- Recovering from interrupted releases requires manual cleanup

### The Solution

Instancium Publisher automates the entire release workflow:

1. **Stage changes** → AI analyzes the diff and generates a Conventional Commit message
2. **Bump version** → Choose from 9 strategies (Patch, Minor, Major, Pre-Release variants, Custom)
3. **Generate changelog** → AI filters commit history and produces clean release notes
4. **Publish** → Automated merge to `main`, tag creation, GitHub Release, backport to source branch
5. **Recover** → If anything fails, resume from the last checkpoint or rollback safely

All AI inference happens through InstantAIGate — no local model weights, no GPU requirements, just an API key.

## ✨ Key Features

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

### AI Changelog Sanitization
Built-in filters automatically strip release-engineering noise:
- Version bump commits
- Backport operations
- Merge commits from release branches
- Generated files (`.min.js`, lock files, binaries)

## 📦 Installation

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git 2.30+ in `PATH`
- GitHub CLI (`gh`) authenticated in `PATH` (for release publishing)
- Running InstantAIGate instance with `/hub/gateway` endpoint accessible

### Build from Source

```bash
git clone https://github.com/instancium/publisher.git
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
    "RegisteredProjects": {
      "MyAwesomeApp": "C:\\path\\to\\MyAwesomeApp.csproj",
      "AnotherRepo": "D:\\repos\\another-repo"
    }
  }
}
```

### Configuration Options

| Setting | Default | Description |
|---------|---------|-------------|
| `AiGatewayUrl` | `http://localhost:5000` | InstantAIGate server URL |
| `AiModelId` | `qwen3-vl-8b-instruct` | Model identifier on the gateway |
| `AiApiKey` | `""` | Bearer token for SignalR authentication |
| `MaxChunkLength` | `3000` | Configuration chunk size limit |
| `RegisteredProjects` | `{}` | Dictionary of project name → `.csproj` path |

## 💻 Usage

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
   - **Commit Control Center** — Stage files, generate AI commit message, push to remote
   - **Release Master & Recovery** — End-to-end release pipeline with rollback capability

### Commit Control Center

1. Stages all changes (`git add -A`)
2. Analyzes the diff and sends to AI
3. Streams generated commit message in real-time
4. User approves or aborts
5. Commits and pushes to current branch

### Release Master & Recovery

1. Reads current version from `.csproj`
2. User selects version bump strategy
3. Bumps version in project file
4. Creates release branch (`release-prep/v{version}`)
5. Generates changelog from commit history (filtered by AI)
6. User approves changelog
7. Merges to `main`, creates tag, pushes
8. Creates GitHub Release via `gh` CLI
9. Backports to source branch if different from `main`
10. Cleans up release branch

**Recovery**: If the process crashes or is interrupted, state is saved to disk. On restart, you can:
- **Resume** — Continue from the last checkpoint
- **Rollback** — Destructive reset to clean state
- **Start Fresh** — Discard state and begin new release

## 🔒 Safety & Validation

- **Clean Working Tree**: Publishing pipelines reject execution if uncommitted changes are present
- **State Persistence**: Interrupted releases can be resumed or rolled back
- **Ephemeral AI Sessions**: All inference sessions are marked ephemeral — KV-cache is released on disconnect
- **Destructive Rollback**: Requires explicit user confirmation; performs `git reset --hard` and `git clean -fd`

## 🏗 Architecture Overview

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

## 🧪 .NET Ecosystem Focus

**Important**: Instancium Publisher is designed exclusively for the .NET ecosystem.

The internal `VersionEngine` parses XML-based project manifests (`.csproj`, `Directory.Build.props`) to read and update the `<Version>` tag. Support for non-XML manifests (e.g., `package.json` for Node.js) is **not implemented**.

## 📄 License

This project is licensed under the Apache License 2.0 — see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

Contributions are welcome! Please open an issue or submit a pull request.

## 🐛 Known Limitations

- No automated test suite in the current release
- No structured logging (`ILogger<T>`) — diagnostic output goes to TUI
- State file path is tied to `Environment.CurrentDirectory` — running from different directories creates separate recovery points
- Destructive rollback requires a clean working tree
- GitHub Release creation depends on external `gh` CLI

## 📞 Support

For issues, feature requests, or questions, please [open an issue](https://github.com/instancium/Publisher/issues).