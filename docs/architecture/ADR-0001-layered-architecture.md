# ADR-0001: Layered Architecture

## Context
Instancium.Publisher originally combined domain versioning rules, application pipeline steps, git/AI/MSBuild I/O, and Spectre.Console UI in a single executable assembly. This prevented non-console hosts (Visual Studio extensions, MAUI apps) from reusing core commit and release workflows without pulling in console dependencies.

## Decision
Split the solution into four projects with strictly inward dependencies:
- `Instancium.Publisher.Domain` (zero external dependencies)
- `Instancium.Publisher.Application` (depends only on `Domain` and `Microsoft.Extensions.DependencyInjection.Abstractions`)
- `Instancium.Publisher.Infrastructure` (implements Application ports for Git, AI, MSBuild, process execution, state, configuration)
- `Instancium.Publisher.Tui` (thin console host using Spectre.Console; depends on Application and Infrastructure)

Application never references Infrastructure or Spectre.Console. TUI workspaces act as thin adapters driving `CommitWorkflow`, `ReleaseWorkflow`, and `ReleaseRecoveryService`.

## Consequences
- Core versioning and release orchestration can be hosted in any .NET UI or CLI without console coupling.
- All external I/O and process execution are isolated behind Application ports, enabling unit testing with in-memory doubles.
