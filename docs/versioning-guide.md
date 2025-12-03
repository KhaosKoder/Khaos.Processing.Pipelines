# Versioning Guide – Khaos.Processing.Pipelines

This solution uses **Semantic Versioning 2.0.0**, Git tags, and **MinVer** to guarantee consistent package versions across every packable project. The canonical version number is derived from Git history, never from hard-coded `<Version>` elements inside `.csproj` files.

## 1. Overview

- **SemVer 2.0.0** (`MAJOR.MINOR.PATCH`) governs every public release.
- **Git tags** are the single source of truth. Tags follow the product-specific prefix `Khaos.Processing.Pipelines/v`.
- **MinVer** (configured in `Directory.Build.props`) computes `Version`, `PackageVersion`, `AssemblyVersion`, and `FileVersion` for all packable projects in the solution. Non-packable projects (tests, samples) inherit the build logic but do not emit packages.
- Every packable library built from the same commit receives the **same version number**.

## 2. Semantic Versioning Rules

| Segment | When to bump | Example in this solution |
| --- | --- | --- |
| **MAJOR** | Public API or behavioral breaking changes: removing/renaming public types, changing method signatures, altering default execution semantics. | Removing `BatchPipelineExecutor.ProcessBatchAsync` overloads, changing default parallelism semantics. |
| **MINOR** | Backwards-compatible features: new pipeline steps, additional options, non-breaking improvements. | Adding a new `IBatchAwareStep` helper or optional metrics hooks. |
| **PATCH** | Bug fixes and safe tweaks: performance optimizations, analyzer fixes, documentation updates, refactors that do not change behavior. | Fixing `PipelineContext.TryGet` edge cases or tightening exception messages. |

## 3. Tagging and Releasing

1. Ensure the working tree is clean and all tests pass:
   ```powershell
   pwsh ./scripts/clean.ps1
   pwsh ./scripts/build.ps1
   pwsh ./scripts/test-with-coverage.ps1
   ```
2. Choose the new SemVer per the rules above (for example `1.4.0`).
3. Create and push the Git tag (replace the version with your chosen number):
   ```powershell
   git tag Khaos.Processing.Pipelines/v1.4.0
   git push origin Khaos.Processing.Pipelines/v1.4.0
   ```
4. Build and pack from the repo root:
   ```powershell
   dotnet pack -c Release
   ```
5. Verify `artifacts/` (or `bin/Release`) contains `.nupkg` files whose names all include the same version (for example `KhaosCode.Processing.Pipelines.xxx.1.4.0.nupkg`).
6. Publish to NuGet (or another feed) via `dotnet nuget push` or GitHub Actions as preferred.

## 4. Pre-release and Development Builds

- Commits after the last release tag automatically produce pre-release versions like `1.5.0-alpha.3` thanks to MinVer’s `alpha` default phase.
- These identifiers are suitable for internal testing feeds but should not be considered official releases.
- To cut a new stable release, tag the commit; MinVer will drop the pre-release suffix.

## 5. Do’s and Don’ts

**DO**
- Change the version only by creating Git tags with the `Khaos.Processing.Pipelines/v` prefix.
- Follow the SemVer guidelines when choosing MAJOR vs MINOR vs PATCH.
- Keep the working tree clean and tested before tagging.

**DO NOT**
- Edit `<Version>`, `<PackageVersion>`, `<AssemblyVersion>`, or `<FileVersion>` in any `.csproj`.
- Override MinVer properties to “force” a version for local experiments.
- Create ad-hoc tags without the product prefix; downstream automation relies on the namespace.

If you create a wrong tag, delete and re-create it:
```powershell
git tag -d Khaos.Processing.Pipelines/v1.2.3
git push origin :refs/tags/Khaos.Processing.Pipelines/v1.2.3
```
Then tag again with the correct number.

## 6. Cheat Sheet

- **Breaking change**: removed a public method → tag `Khaos.Processing.Pipelines/v2.0.0`.
- **New feature**: added a non-breaking pipeline step → tag `Khaos.Processing.Pipelines/v1.3.0`.
- **Bug fix**: resolved a null-reference in an existing step → tag `Khaos.Processing.Pipelines/v1.2.1`.

## 7. Relation to Other Libraries

Khaos ships multiple independent libraries. Each repository manages its own Git tags and versions. Downstream aggregators or meta-packages should depend on explicit version ranges (for example `KhaosCode.Processing.Pipelines.xxx >= 1.2.0`) and update them as needed. A release in this repo does **not** automatically bump versions in other Khaos products.
