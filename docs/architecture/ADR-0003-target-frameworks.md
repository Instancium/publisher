# ADR-0003: Target Frameworks and Versioning

## Context
Future hosts have different runtime constraints: VisualStudio.Extensibility (out-of-proc) requires `.NET 8`, while MAUI and the TUI host target `.NET 10`. Classic `.NET Framework 4.7.2` VSIX is out of scope. Additionally, the CLI executable name `Instancium.Publisher` is a public contract, and Publisher itself stamps repository-wide `<Version>` across all `.csproj` files.

## Decision
- `Instancium.Publisher.Domain` and `Instancium.Publisher.Application` multi-target `net8.0;net10.0`.
- `Instancium.Publisher.Infrastructure` and `Instancium.Publisher.Tui` target `net10.0` until a host requires otherwise.
- `Instancium.Publisher.Tui` sets `<AssemblyName>Instancium.Publisher</AssemblyName>` to preserve the CLI executable name.
- `<Version>` remains per-`.csproj` (not in `Directory.Build.props`) so repository-wide version stamping continues to work across every project file.

## Consequences
- Domain and Application compile for both `.NET 8` and `.NET 10` with zero extra runtime dependencies.
- Existing CLI invocations and dogfooding releases remain backward-compatible.
