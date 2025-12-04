# Khaos.Processing.Pipelines – Developer Guide

This document explains how to extend and maintain the Khaos processing pipeline product. It complements the user-facing guide and the versioning reference found in `docs/versioning-guide.md`.

## Solution Layout

- `src/Khaos.Processing.Pipelines`: Primary production library. Keep public APIs stable and well-documented.
- `tests/Khaos.Processing.Pipelines.Tests`: xUnit test suite that exercises pipelines, steps, context, and the batch executor. All new code must be covered here.
- `scripts/`: PowerShell helper scripts for common workflows.
- `docs/`: Markdown documentation bundled inside the NuGet package and automatically copied into the consumer's solution under `SolutionName/docs/KhaosCode.Processing.Pipelines` when the package is restored.

## Coding Guidelines

1. **Pipelines & Steps**
   - Use the `Pipeline.Start<T>()` builder for composing new pipelines.
   - Prefer small, composable `IPipelineStep<TIn, TOut>` implementations. If a step can process entire batches efficiently, implement `IBatchAwareStep<TIn, TOut>` to unlock `InvokeBatchAsync`.
   - Keep `PipelineContext` keys scoped and namespaced (for example `"telemetry:batch-id"`).

2. **Batch Execution**
   - `BatchPipelineExecutor<TIn, TOut>` orchestrates per-step execution, optional parallelism, and instrumentation. Reuse it instead of crafting bespoke loops.
   - All new metrics integrations should go through `IPipelineMetrics` and its scope objects to keep instrumentation pluggable.

3. **Docs in Packages**
   - Every Markdown file inside `docs/` is packed into the NuGet package under `contentFiles/any/any/docs/...`.
   - The package exposes a `buildTransitive` target that copies those files into the consumer solution (`<SolutionDir>/docs/KhaosCode.Processing.Pipelines`). Add or update docs here—no extra configuration is required.

4. **Analyzers & Style**
   - Nullable reference types and implicit usings are enforced solution-wide via `Directory.Build.props`.
   - Keep files ASCII-only unless an existing file already uses non-ASCII content for a justified reason (for example, localized samples).

## Testing & Coverage

- Run all tests: `pwsh ./scripts/test.ps1` (from the `scripts` folder or via terminal).
- Run tests with coverage + HTML reports: `pwsh ./scripts/test-with-coverage.ps1`.
  - This script enforces a minimum **70% line coverage** via `coverlet.msbuild`.
  - Coverage data is written to `TestResults/coverage/coverage.cobertura.xml` and rendered by ReportGenerator into `TestResults/coverage-report`.
- Add targeted unit tests whenever you add new behaviors or bug fixes. Favor deterministic steps with well-defined inputs/outputs.

## Build & Packaging

- `pwsh ./scripts/build.ps1`: restore + build solution in Release.
- `pwsh ./scripts/clean.ps1`: remove `TestResults`, `artifacts`, and run `dotnet clean`.
- `dotnet pack -c Release` (run from the repo root) uses **MinVer** to stamp every packable project with the same SemVer derived from Git tags. No project should define `<Version>` manually.
- NuGet packages automatically include:
  - `README.md` at the package root.
  - All files under `docs/`.
  - `buildTransitive` targets that copy docs into the consuming solution.

## Versioning & Releases

- MinVer is configured in `Directory.Build.props` with prefix `Khaos.Processing.Pipelines/v`, defaulting to the `alpha` pre-release channel between tags.
- Follow the workflow in `docs/versioning-guide.md` for bumping versions, tagging releases, and packing artifacts.
- Never override MinVer-derived properties (`Version`, `PackageVersion`, `AssemblyVersion`, `FileVersion`) inside project files.

## Contribution Checklist

1. Update code + tests.
2. Run formatting/analyzers if needed (the SDK analyzers run during build).
3. Execute `scripts/test-with-coverage.ps1` and ensure coverage ≥ 70%.
4. Update `docs/` and reference new guidance in the user guide if behavior changes.
5. Tag the release per the versioning guide when publishing NuGet packages.
