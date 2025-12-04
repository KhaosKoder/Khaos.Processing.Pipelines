# Khaos.Processing.Pipelines – User Guide

This guide is for application developers consuming the `KhaosCode.Processing.Pipelines` NuGet package.

## Installation

```powershell
# from your project directory
dotnet add package KhaosCode.Processing.Pipelines
```

After the first restore the package copies its documentation into your solution at:

```
<SolutionRoot>/docs/KhaosCode.Processing.Pipelines
```

Check that folder for the latest guides bundled with the package version you installed.

## Build Your First Pipeline

```csharp
var pipeline = Pipeline.Start<string>()
    .UseStep(new TrimStep())
    .UseStep(new HashStep())
    .Build();

var context = new PipelineContext();
var outcome = await pipeline.ProcessAsync(" record ", context, CancellationToken.None);
```

- Each step implements `IPipelineStep<TIn, TOut>` and can short-circuit downstream execution by returning `StepOutcome<T>.Abort()`.
- Use `PipelineContext` to share data across steps (for example `context.Set("correlation-id", Guid.NewGuid())`).

## Batch Execution

`BatchPipelineExecutor<TIn, TOut>` runs a pipeline across an entire batch:

```csharp
var executor = new BatchPipelineExecutor<Order, ProcessedOrder>("order-ingest");
var options = new PipelineExecutionOptions
{
    IsSequential = false,
    MaxDegreeOfParallelism = 8
};

await executor.ProcessBatchAsync(orders, pipeline, new PipelineContext(), options);
```

- Set `IsSequential` to `true` to process one record at a time.
- `MaxDegreeOfParallelism` must be ≥ 1; values > 1 enable concurrent step execution when `IsSequential` is `false`.
- Steps that also implement `IBatchAwareStep<TIn, TOut>` receive optimized batch callbacks through `InvokeBatchAsync`.

## Metrics & Instrumentation

Implement `IPipelineMetrics` to plug into existing observability stacks. Pass your implementation into `BatchPipelineExecutor` to capture per-batch and per-step scopes.

```csharp
var executor = new BatchPipelineExecutor<Order, ProcessedOrder>(
    pipelineName: "order-ingest",
    metrics: new PrometheusPipelineMetrics());
```

The default constructor uses no-op metrics.

## Working With PipelineContext

- `Set(key, value)`: stores arbitrary state for the current batch.
- `Get<T>(key)`: retrieves a required value (throws if missing).
- `TryGet<T>(key, out value)`: safe retrieval without exceptions.

Use descriptive keys (for example `"user:region"`) to avoid collisions between steps.

## Documentation in Your Solution

Every NuGet install copies the packaged docs into `Solution/docs/KhaosCode.Processing.Pipelines`. Keep this folder under source control if you want your teammates to have the same references, or regenerate it by clearing the folder and running `dotnet restore` again.

## Troubleshooting

- **Abort vs Continue**: A step returning `StepOutcome<T>.Abort()` stops processing for the current record but does not stop the batch.
- **Concurrency**: When parallel execution is enabled, ensure your steps are thread-safe or switch to sequential mode.
- **Context Lifetime**: Reuse a single `PipelineContext` per batch; allocate a new instance for each batch to avoid leaking state.

For contribution guidelines or release instructions, see the developer and versioning guides inside the same `docs/` folder.
