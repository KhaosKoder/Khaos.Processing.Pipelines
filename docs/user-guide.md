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

## Command Queue Dispatch

For scenarios requiring partitioned command routing (actor-like dispatch, CQRS command side, partitioned state management), use the `Commands` namespace:

```csharp
using Khaos.Processing.Pipelines.Commands;

// Define a routable command
public record CloseSessionCommand(string NID, Guid SessionGuid) 
    : IRoutableCommand<string>
{
    public string RoutingKey => NID;
}

// Create a dispatcher
var dispatcher = CommandDispatcher.Create<string, CloseSessionCommand>()
    .WithPartitionCount(64)           // Must be power of 2
    .WithQueueCapacity(10_000)         // Per-partition capacity
    .WithBackpressure(BackpressureBehavior.Wait, TimeSpan.FromSeconds(30))
    .Build();

// Producer: dispatch commands (thread-safe from multiple threads)
var result = await dispatcher.DispatchAsync(command, ct);
if (result == DispatchResult.Success)
    // Command enqueued
else if (result == DispatchResult.Rejected)
    // Queue full, behavior was Reject
else if (result == DispatchResult.Timeout)
    // Wait timed out

// Consumer: process commands from a partition
var queue = dispatcher.GetPartitionQueue(partitionIndex);
while (!ct.IsCancellationRequested)
{
    var cmd = await queue.DequeueAsync(ct);
    await ProcessAsync(cmd);
}
```

### Key Concepts

- **Routing Key Affinity**: Commands with the same `RoutingKey` always route to the same partition, preserving ordering per key.
- **Single Consumer per Partition**: Each partition queue is designed for exactly one consumer, guaranteeing FIFO order within the partition.
- **Lock-Free Enqueue**: Uses `Channel<T>` internally for high-throughput, lock-free dispatch from multiple producers.
- **Backpressure Modes**:
  - `Wait`: Block asynchronously until space is available (with configurable timeout).
  - `Reject`: Return `DispatchResult.Rejected` immediately when full.
  - `DropOldest`: Drop the oldest command to make room.

### Monitoring

```csharp
// Get fill percentages for all partitions
IReadOnlyList<int> fills = dispatcher.GetPartitionFillPercentages();

// Get aggregate statistics
var stats = dispatcher.GetStatistics();
console.WriteLine($"Queued: {stats.TotalQueuedCommands}, Fill: {stats.OverallFillPercentage}%");
```

## Flush Policy & Persistence

For scenarios requiring coordinated persistence of dirty state (aggregators, caches, actor state), use the `Persistence` namespace:

```csharp
using Khaos.Processing.Pipelines.Persistence;

// Create a flush coordinator with time and count policies
var coordinator = FlushCoordinator.Create<string>()
    .WithTimeBasedFlush(TimeSpan.FromMinutes(5))
    .WithCountBasedFlush(1000)
    .WithShutdownFlush()
    .Build();
```

### Marking Dirty State

```csharp
// Mark keys as dirty when their state changes
coordinator.MarkDirty("session-123");
coordinator.MarkDirty("session-456");

// Check if a specific key is dirty
if (coordinator.IsDirty("session-123"))
    Console.WriteLine("Session has uncommitted changes");

// Get all dirty keys
IReadOnlyCollection<string> dirtyKeys = coordinator.GetDirtyKeys();
```

### Checking and Flushing

```csharp
// Periodic check - evaluates policies and flushes if needed
var result = await coordinator.CheckAndFlushAsync(
    async keys =>
    {
        foreach (var key in keys)
            await PersistAsync(key);
    },
    cancellationToken);

if (result.Flushed)
    Console.WriteLine($"Flushed {result.ItemCount} items, triggered by: {result.TriggeringPolicy}");
```

### Force Flush & Shutdown

```csharp
// Force immediate flush regardless of policies
await coordinator.ForceFlushAsync(PersistAllAsync, ct);

// Signal shutdown for graceful persistence
coordinator.SetShutdown();
var shutdownResult = await coordinator.CheckAndFlushAsync(PersistAllAsync, ct);
```

### Available Policies

| Policy | Trigger Condition |
|--------|-------------------|
| `TimeBasedFlushPolicy` | Elapsed time since last flush exceeds threshold |
| `CountBasedFlushPolicy` | Dirty count meets or exceeds threshold |
| `MemoryPressureFlushPolicy` | Estimated dirty memory exceeds threshold |
| `DirtyRatioFlushPolicy` | Dirty/total ratio exceeds threshold |
| `ExternalTriggerFlushPolicy` | External signal via `SetExternalTrigger()` |
| `ShutdownFlushPolicy` | Shutdown signal via `SetShutdown()` |

### Memory Estimation

For memory-pressure policies, provide a memory estimator:

```csharp
var coordinator = FlushCoordinator.Create<string>()
    .WithMemoryEstimator(() => _cache.EstimatedMemoryBytes)
    .WithMemoryPressureFlush(512 * 1024 * 1024) // 512 MB
    .Build();
```

### Statistics

```csharp
var stats = coordinator.GetStatistics();
Console.WriteLine($"Total flushes: {stats.TotalFlushes}");
Console.WriteLine($"Total items flushed: {stats.TotalItemsFlushed}");
Console.WriteLine($"Last flush: {stats.LastFlushTime}");
```

## Documentation in Your Solution

Every NuGet install copies the packaged docs into `Solution/docs/KhaosCode.Processing.Pipelines`. Keep this folder under source control if you want your teammates to have the same references, or regenerate it by clearing the folder and running `dotnet restore` again.

## Troubleshooting

- **Abort vs Continue**: A step returning `StepOutcome<T>.Abort()` stops processing for the current record but does not stop the batch.
- **Concurrency**: When parallel execution is enabled, ensure your steps are thread-safe or switch to sequential mode.
- **Context Lifetime**: Reuse a single `PipelineContext` per batch; allocate a new instance for each batch to avoid leaking state.

For contribution guidelines or release instructions, see the developer and versioning guides inside the same `docs/` folder.
