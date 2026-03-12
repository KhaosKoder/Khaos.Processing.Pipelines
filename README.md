# Khaos.Processing.Pipelines

Composable, testable, high-throughput processing pipelines for .NET. This project ships the NuGet package `KhaosCode.Processing.Pipelines`, which contains:

- A fluent builder for chaining `IPipelineStep<TIn, TOut>` implementations.
- Optional batch-aware steps (`IBatchAwareStep<TIn, TOut>`) for more efficient bulk operations.
- A parallel-ready `BatchPipelineExecutor<TIn, TOut>` with pluggable metrics instrumentation.
- A documentation delivery system: every package install copies the contents of `docs/` into `Solution/docs/KhaosCode.Processing.Pipelines` inside the consuming solution.

Use it when you need deterministic, instrumented workflows that can run record-by-record or as batches, without dragging a full workflow engine into your process.

## Installation

```powershell
dotnet add package KhaosCode.Processing.Pipelines
```

After the first restore, check `<SolutionRoot>/docs/KhaosCode.Processing.Pipelines` for the bundled guides (`developer-guide.md`, `user-guide.md`, `versioning-guide.md`).

## Quick Start

```csharp
using Khaos.Processing.Pipelines;

var pipeline = Pipeline
	.Start<Order>()
	.UseStep(new ValidateStep())
	.UseStep(new EnrichStep())
	.UseStep(new PersistStep())
	.Build();

var context = new PipelineContext();
var outcome = await pipeline.ProcessAsync(order, context, cancellationToken);

if (outcome.Kind == StepOutcomeKind.Abort)
{
	// Current record stopped early; continue with next input.
}
```

- Each step implements `IPipelineStep<TIn, TOut>` and returns `StepOutcome<T>` to signal `Continue` or `Abort`.
- `PipelineContext` is a shared per-batch state bag (backed by `ConcurrentDictionary`) with `Set`, `Get<T>`, and `TryGet<T>` helpers.

## Batch Execution & Parallelism

```csharp
var executor = new BatchPipelineExecutor<Order, ProcessedOrder>(
	pipelineName: "order-intake",
	metrics: new PrometheusPipelineMetrics());

var options = new PipelineExecutionOptions
{
	IsSequential = false,
	MaxDegreeOfParallelism = Environment.ProcessorCount
};

await executor.ProcessBatchAsync(orders, pipeline, context, options, cancellationToken);
```

- Batch steps that also implement `IBatchAwareStep<TIn, TOut>` receive `InvokeBatchAsync`, allowing you to materialize results or call external systems once per batch.
- Metrics hooks (`IPipelineMetrics`, `IPipelineBatchScope`, `IPipelineStepScope`) let you emit telemetry per batch/step.

## Command Queue Dispatch

For scenarios requiring partitioned command routing with backpressure, use the `Commands` namespace:

```csharp
using Khaos.Processing.Pipelines.Commands;

// Define a routable command
public record CloseSessionCommand(string NID, Guid SessionGuid) 
    : IRoutableCommand<string>
{
    public string RoutingKey => NID;
}

// Create a dispatcher with 64 partitions
var dispatcher = CommandDispatcher.Create<string, CloseSessionCommand>()
    .WithPartitionCount(64)
    .WithQueueCapacity(10_000)
    .WithBackpressure(BackpressureBehavior.Wait, TimeSpan.FromSeconds(30))
    .Build();

// Producer: dispatch commands (thread-safe)
var result = await dispatcher.DispatchAsync(new CloseSessionCommand("NID123", sessionGuid), ct);
if (result != DispatchResult.Success)
    // Handle rejection, timeout, or cancellation

// Consumer: process commands from a partition
var queue = dispatcher.GetPartitionQueue(partitionIndex);
while (!ct.IsCancellationRequested)
{
    var command = await queue.DequeueAsync(ct);
    await ProcessCommandAsync(command);
}
```

Key features:
- **Partition-based routing**: Commands with the same routing key always go to the same partition, preserving ordering.
- **Lock-free dispatch**: Uses `Channel<T>` internally for high-throughput, lock-free enqueue operations.
- **Configurable backpressure**: Choose between Wait (with timeout), Reject, or DropOldest behaviors.
- **Power-of-2 partitions**: Partition count must be a power of 2 for efficient bit-masked modulo routing.

## Flush Policy & Persistence

For stateful aggregation scenarios, the `Persistence` namespace provides flush coordination:

```csharp
using Khaos.Processing.Pipelines.Persistence;

// Create a flush coordinator with multiple policies
var coordinator = FlushCoordinator.Create<string>()
    .WithTimeBasedFlush(TimeSpan.FromMinutes(5))   // Flush every 5 minutes
    .WithCountBasedFlush(1000)                      // Or when 1000 items are dirty
    .WithMemoryPressureFlush(512 * 1024 * 1024)     // Or at 512 MB memory
    .WithShutdownFlush()                            // Always flush on shutdown
    .Build();

// Mark items as dirty when state changes
coordinator.MarkDirty("session-123");
coordinator.MarkDirty("session-456");

// Check and flush periodically
var result = await coordinator.CheckAndFlushAsync(
    async keys => { /* persist logic */ },
    cancellationToken);

if (result.Flushed)
    Console.WriteLine($"Flushed {result.ItemCount} items via {result.TriggeringPolicy}");
```

Key features:
- **Policy-based decisions**: Time-based, count-based, memory-pressure, dirty-ratio, external-trigger, and shutdown policies.
- **Composite policies**: Combine multiple policies with OR semantics via `CompositeFlushPolicy`.
- **Thread-safe tracking**: Uses `ConcurrentDictionary` for lock-free dirty key tracking.
- **Testable time**: Accepts `TimeProvider` for deterministic unit testing.
- **Diagnostics**: `FlushCoordinatorStatistics` provides flush counts and timing.

## Extending the Package

1. **Create steps** by implementing `IPipelineStep<TIn, TOut>`; use `StepOutcome<T>.Abort()` to short-circuit.
2. **Batch-aware logic**: implement `IBatchAwareStep<TIn, TOut>` alongside `IPipelineStep` to get optimized batch callbacks.
3. **Instrumentation**: implement `IPipelineMetrics` to integrate with your observability stack (OpenTelemetry, Prometheus, etc.).
4. **Docs**: add Markdown/diagrams under `docs/`. They automatically ship inside the NuGet package and get copied into consuming solutions.

## Scripts & Tooling

- `scripts/build.ps1`: restore + build (Release).
- `scripts/test.ps1`: run xUnit tests, drop TRX into `TestResults/tests.trx`.
- `scripts/test-with-coverage.ps1`: enforces ≥70% line coverage via Coverlet, emits Cobertura + HTML (`TestResults/coverage` & `TestResults/coverage-report`).
- `scripts/clean.ps1`: remove `TestResults`, `artifacts`, and run `dotnet clean`.

Install tools via `dotnet tool restore` (invoked automatically by the coverage script) to get the ReportGenerator global tool.

## Versioning Model

- All packable projects derive their version from Git tags using **MinVer** with prefix `Khaos.Processing.Pipelines/v` (Semantic Versioning 2.0.0).
- Tagging example:

```powershell
git tag Khaos.Processing.Pipelines/v1.2.0
git push origin Khaos.Processing.Pipelines/v1.2.0
dotnet pack -c Release
```

Consult `docs/versioning-guide.md` for the full workflow, tagging rules, and how pre-release builds (`1.3.0-alpha.1`) are produced between tags.

## Testing

```powershell
pwsh ./scripts/test.ps1
pwsh ./scripts/test-with-coverage.ps1
```

Unit tests cover the pipeline builder, context, and batch executor (sequential, parallel, batch-aware, invalid inputs). Add or update tests whenever you extend the surface area.

## Why This Package

- **Focused scope**: It does one thing—build and execute pipelines—without imposing entire app frameworks.
- **Batch-first**: Steps optionally opt into batch processing without separate code paths.
- **Docs-as-artifact**: Consumers get the exact documentation version that matches the package they installed.
- **CI-friendly**: Pure SDK + Git tag driven versioning; no external services required.

> Need to integrate with another system or extend the builder? Check `docs/developer-guide.md` in this repo (and in the NuGet package) for deeper implementation notes.
