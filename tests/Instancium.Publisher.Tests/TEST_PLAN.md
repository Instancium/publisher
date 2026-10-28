# Test Plan — Instancium.Publisher

Scope: repository-wide (repository-scoped) versioning introduced for the Release pipeline.
All tests run without network, git or AI gateway dependencies. File-system tests operate inside
isolated temporary directories (`TemporaryDirectoryScope`) and clean up automatically.
All temporary paths are canonical (platform separators), enabling exact string assertions
against `DirectoryInfo`/`FileInfo` FullName values produced by the discovery service.

Run:

```bash
dotnet test
```

## Suites

### SemanticVersionTests (Unit)
- Subject: `Core/Domain/SemanticVersion.cs`.
- Verified: SemVer 2.0 parsing (`v` prefix, pre-release tag, iteration, build metadata),
  rejection of malformed strings, `ToString` round-trip, SemVer ordering rules,
  constructor argument guards.
- Expected: components parsed exactly; `ArgumentNullException` for null input (exact type,
  xUnit `Assert.Throws` semantics); `ArgumentException` for empty/whitespace;
  `FormatException` for malformed input; `1.0.0-rc.1 < 1.0.0`, `rc.1 < rc.2`,
  numeric (not lexical) component comparison.

### RepositoryVersionPolicyTests (Unit)
- Subject: `Core/Domain/RepositoryVersionPolicy.cs`.
- Verified: repository version resolution (highest declared version wins, numeric comparison),
  initial versioning baseline (`1.0.0` for stable strategies, `1.0.0-<tag>.1` for pre-release),
  custom tag and `Custom` strategy handling, divergent project counting.
- Expected: `null` when no project declares a version; `1.10.0` beats `1.9.3`;
  missing and mismatched projects counted as divergent; `Custom` without a string throws.

### VersionEngineTests (Integration, temp files)
- Subject: `Services/Versioning/VersionEngine.cs`.
- Verified: snapshot reading (declared vs missing `<Version>`), batch reading,
  repository-wide writing (update existing element, add missing element,
  add missing `PropertyGroup`), bump strategy matrix, `Custom` strategy validation.
- Expected: missing `<Version>` yields `DeclaredVersion == null` (regression guard against the
  legacy silent `1.0.0` default); after write every project file carries the new version;
  strategy matrix matches SemVer semantics.

### ProjectDiscoveryServiceTests (Integration, temp directory tree)
- Subject: `Services/Versioning/ProjectDiscoveryService.cs`.
- Verified: recursive `*.csproj` discovery, exclusion of `bin`, `obj`, `.git`, `.vs`,
  `node_modules`, deterministic sorted output, empty root and missing root behavior,
  cancellation propagation.
- Expected: only valid projects returned in sorted order with canonical absolute paths;
  excluded directories ignored; empty result for empty repository;
  `DirectoryNotFoundException` for missing root; `ArgumentException` for null/whitespace root;
  `OperationCanceledException` for a cancelled token.

### BumpVersionStepTests (Integration, temp files + in-memory state manager)
- Subject: `Pipeline/Steps/BumpVersionStep.cs` with the real `VersionEngine`.
- Verified: repository-wide stamping of the new version into every project,
  initial versioning baseline, bump from the highest declared version,
  journaling of the version plan before any file mutation (save-before-mutation),
  idempotent resume (planned version is completed, never re-bumped),
  argument validation.
- Expected: no declared versions + `Patch` → `1.0.0` in all projects and exactly one journal save;
  `1.2.0 / 1.5.3 / none` + `Minor` → current `1.5.3`, new `1.6.0` in all projects;
  resume with planned `2.0.0` and null strategy → all files aligned to `2.0.0` with zero saves;
  null strategy without a plan → `InvalidOperationException`; empty snapshots → `ArgumentException`.

## Regression Guards

1. No per-project version selection: every write targets all discovered projects.
2. Missing `<Version>` is distinguishable from an explicit `1.0.0`.
3. Interrupted releases resume without double bumping (journal + idempotent completion).