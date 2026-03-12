# Khaos.Processing.Pipelines.Commands — Specification

**Version:** 1.0  
**Date:** 2026-03-12  
**Namespace:** `Khaos.Processing.Pipelines.Commands`  

---

## 1. Purpose

Provide a **generic, thread-safe command dispatch pattern** for routing commands to partitioned workers based on a routing key. This enables single-writer semantics per key partition while allowing concurrent command producers.

---

## 2. Use Cases

1. **Partitioned state management**: Route state-mutating commands to the worker owning that partition
2. **Actor-like dispatch**: Commands to logical actors routed by actor ID
3. **CQRS command side**: Route commands to aggregate handlers by aggregate ID
4. **Administrative operations**: Enqueue operations from REST APIs to background workers

---

## 3. Design Principles

### 3.1 Lock-Free Hot Path
- Command enqueue must be lock-free
- Use `ConcurrentQueue<T>` or similar per-partition structures
- Array-indexed partition lookup (no dictionary in hot path)

### 3.2 Bounded Queues with Backpressure
- Each partition queue has a configurable capacity
- When full, producers receive backpressure signal (async wait or reject)

### 3.3 Single Consumer per Partition
- Each partition queue has exactly one consumer
- Guarantees ordering within partition

### 3.4 Integration with Khaos Ecosystem
- Uses `Khaos.Metrics` for queue depth, dispatch latency
- Uses `Khaos.Logging` for dispatch events
- Configuration via `Khaos.MultiApp.Settings` when used in applications

---

## 4. Public API

### 4.1 Command Interfaces

```csharp
namespace Khaos.Processing.Pipelines.Commands;

/// <summary>
/// Marker interface for commands.
/// </summary>
public interface ICommand { }

/// <summary>
/// Command that can be routed to a partition by key.
/// </summary>
public interface IRoutableCommand<TKey> : ICommand
{
    /// <summary>
    /// The key used to determine which partition handles this command.
    /// </summary>
    TKey RoutingKey { get; }
}
```

### 4.2 Command Queue

```csharp
/// <summary>
/// Thread-safe bounded queue for commands to a single partition.
/// </summary>
public interface ICommandQueue<TCommand> where TCommand : ICommand
{
    /// <summary>
    /// Current number of commands in the queue.
    /// </summary>
    int Count { get; }
    
    /// <summary>
    /// Maximum capacity of the queue.
    /// </summary>
    int Capacity { get; }
    
    /// <summary>
    /// Percentage of capacity currently used (0-100).
    /// </summary>
    int FillPercentage { get; }
    
    /// <summary>
    /// Attempts to enqueue a command without blocking.
    /// </summary>
    /// <returns>True if enqueued, false if queue is full.</returns>
    bool TryEnqueue(TCommand command);
    
    /// <summary>
    /// Enqueues a command, waiting asynchronously if queue is full.
    /// </summary>
    /// <param name="command">The command to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if enqueued before cancellation.</returns>
    ValueTask<bool> EnqueueAsync(TCommand command, CancellationToken ct = default);
    
    /// <summary>
    /// Tries to dequeue a command without blocking.
    /// </summary>
    bool TryDequeue([MaybeNullWhen(false)] out TCommand command);
    
    /// <summary>
    /// Dequeues a command, waiting asynchronously if queue is empty.
    /// </summary>
    ValueTask<TCommand> DequeueAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Reads all available commands without blocking.
    /// </summary>
    /// <param name="buffer">Buffer to fill with commands.</param>
    /// <returns>Number of commands read.</returns>
    int DrainTo(Span<TCommand> buffer);
}
```

### 4.3 Routed Command Dispatcher

```csharp
/// <summary>
/// Configuration for the routed command dispatcher.
/// </summary>
public sealed class RoutedDispatcherOptions
{
    /// <summary>
    /// Number of partitions (buckets). Must be power of 2 for efficient hashing.
    /// </summary>
    public int PartitionCount { get; set; } = 32;
    
    /// <summary>
    /// Capacity per partition queue.
    /// </summary>
    public int QueueCapacityPerPartition { get; set; } = 10_000;
    
    /// <summary>
    /// Backpressure behavior when queue is full.
    /// </summary>
    public BackpressureBehavior BackpressureBehavior { get; set; } = BackpressureBehavior.Wait;
    
    /// <summary>
    /// Maximum time to wait when backpressure behavior is Wait.
    /// </summary>
    public TimeSpan BackpressureTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public enum BackpressureBehavior
{
    /// <summary>Wait asynchronously until space is available.</summary>
    Wait,
    
    /// <summary>Reject immediately if full.</summary>
    Reject,
    
    /// <summary>Drop oldest command to make room.</summary>
    DropOldest
}

/// <summary>
/// Routes commands to partitioned queues based on routing key.
/// Thread-safe for concurrent dispatch from multiple producers.
/// </summary>
public interface IRoutedCommandDispatcher<TKey, TCommand> : IDisposable
    where TCommand : IRoutableCommand<TKey>
{
    /// <summary>
    /// Number of partitions.
    /// </summary>
    int PartitionCount { get; }
    
    /// <summary>
    /// Dispatches a command to the appropriate partition queue.
    /// </summary>
    ValueTask<DispatchResult> DispatchAsync(TCommand command, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the queue for a specific partition (for consumers).
    /// </summary>
    ICommandQueue<TCommand> GetPartitionQueue(int partitionIndex);
    
    /// <summary>
    /// Gets the partition index for a given routing key.
    /// </summary>
    int GetPartitionIndex(TKey routingKey);
    
    /// <summary>
    /// Returns fill percentage for each partition (for monitoring).
    /// </summary>
    IReadOnlyList<int> GetPartitionFillPercentages();
}

public enum DispatchResult
{
    /// <summary>Command was successfully enqueued.</summary>
    Success,
    
    /// <summary>Queue was full and behavior is Reject.</summary>
    Rejected,
    
    /// <summary>Wait timed out.</summary>
    Timeout,
    
    /// <summary>Operation was cancelled.</summary>
    Cancelled
}
```

### 4.4 Builder

```csharp
/// <summary>
/// Fluent builder for creating a routed command dispatcher.
/// </summary>
public sealed class RoutedDispatcherBuilder<TKey, TCommand>
    where TCommand : IRoutableCommand<TKey>
{
    public RoutedDispatcherBuilder<TKey, TCommand> WithPartitionCount(int count);
    
    public RoutedDispatcherBuilder<TKey, TCommand> WithQueueCapacity(int capacity);
    
    public RoutedDispatcherBuilder<TKey, TCommand> WithBackpressure(
        BackpressureBehavior behavior, 
        TimeSpan? timeout = null);
    
    public RoutedDispatcherBuilder<TKey, TCommand> WithHashFunction(
        Func<TKey, int> hashFunction);
    
    public RoutedDispatcherBuilder<TKey, TCommand> WithMetrics(
        IOperationMonitor monitor);
    
    public RoutedDispatcherBuilder<TKey, TCommand> WithLogger(
        ILogger logger);
    
    public IRoutedCommandDispatcher<TKey, TCommand> Build();
}

// Factory entry point
public static class CommandDispatcher
{
    public static RoutedDispatcherBuilder<TKey, TCommand> Create<TKey, TCommand>()
        where TCommand : IRoutableCommand<TKey>
        => new RoutedDispatcherBuilder<TKey, TCommand>();
}
```

---

## 5. Implementation Details

### 5.1 Partition Queue Implementation

```csharp
/// <summary>
/// Bounded command queue backed by Channel&lt;T&gt;.
/// </summary>
internal sealed class BoundedCommandQueue<TCommand> : ICommandQueue<TCommand>
    where TCommand : ICommand
{
    private readonly Channel<TCommand> _channel;
    private int _count; // Interlocked for approximate count
    
    public BoundedCommandQueue(int capacity)
    {
        _channel = Channel.CreateBounded<TCommand>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,    // Single consumer per partition
            SingleWriter = false,   // Multiple producers
            FullMode = BoundedChannelFullMode.Wait
        });
        Capacity = capacity;
    }
    
    // ... implementation using Channel reader/writer
}
```

### 5.2 Dispatcher Implementation

```csharp
internal sealed class RoutedCommandDispatcher<TKey, TCommand> 
    : IRoutedCommandDispatcher<TKey, TCommand>
    where TCommand : IRoutableCommand<TKey>
{
    private readonly ICommandQueue<TCommand>[] _partitions; // Array, not dictionary
    private readonly Func<TKey, int> _hashFunction;
    private readonly int _partitionMask; // For power-of-2 modulo
    
    public RoutedCommandDispatcher(RoutedDispatcherOptions options, ...)
    {
        // Validate partition count is power of 2
        if (!IsPowerOfTwo(options.PartitionCount))
            throw new ArgumentException("PartitionCount must be power of 2");
        
        _partitionMask = options.PartitionCount - 1;
        _partitions = new ICommandQueue<TCommand>[options.PartitionCount];
        
        for (int i = 0; i < options.PartitionCount; i++)
            _partitions[i] = new BoundedCommandQueue<TCommand>(options.QueueCapacityPerPartition);
    }
    
    public int GetPartitionIndex(TKey routingKey)
    {
        // Bit-mask for fast modulo (works because partition count is power of 2)
        return _hashFunction(routingKey) & _partitionMask;
    }
    
    public ValueTask<DispatchResult> DispatchAsync(TCommand command, CancellationToken ct)
    {
        int partition = GetPartitionIndex(command.RoutingKey);
        return _partitions[partition].EnqueueAsync(command, ct)
            .ContinueWith(success => success ? DispatchResult.Success : DispatchResult.Rejected);
    }
}
```

### 5.3 Hash Function

Default hash function spreads keys evenly:

```csharp
private static int DefaultHash<TKey>(TKey key)
{
    // Use XOR-shift to spread hash bits
    int hash = key?.GetHashCode() ?? 0;
    hash ^= hash >> 16;
    hash *= 0x85ebca6b;
    hash ^= hash >> 13;
    hash *= 0xc2b2ae35;
    hash ^= hash >> 16;
    return hash & int.MaxValue; // Ensure positive
}
```

---

## 6. Metrics Integration

When `IOperationMonitor` is provided:

| Metric | Type | Description |
|--------|------|-------------|
| `command.dispatch` | Operation | Command dispatch timing |
| `command.queue.depth` | Gauge | Per-partition queue depth |
| `command.queue.full` | Counter | Queue full events |
| `command.backpressure` | Counter | Backpressure events (waits/rejects) |

Tags: `partition`, `command_type`

---

## 7. Logging Integration

When logger is provided, uses Khaos.Logging patterns:

```csharp
[LogEventSource(LoggerRootTypeName = "CommandLogger", BasePath = "Commands")]
public enum CommandLogEvents
{
    DISPATCH_Enqueued = 1000,
    DISPATCH_Rejected = 1001,
    DISPATCH_Timeout = 1002,
    QUEUE_Full = 2000,
    QUEUE_Drained = 2001,
}
```

---

## 8. Usage Example

```csharp
// Define a command
public record CloseSessionCommand(string NID, Guid SessionGuid) 
    : IRoutableCommand<string>
{
    public string RoutingKey => NID;
}

// Create dispatcher
var dispatcher = CommandDispatcher.Create<string, CloseSessionCommand>()
    .WithPartitionCount(64)
    .WithQueueCapacity(10_000)
    .WithBackpressure(BackpressureBehavior.Wait, TimeSpan.FromSeconds(30))
    .WithMetrics(operationMonitor)
    .Build();

// Producer (e.g., REST API or Janitor)
await dispatcher.DispatchAsync(new CloseSessionCommand("NID123", sessionGuid), ct);

// Consumer (bucket worker)
var queue = dispatcher.GetPartitionQueue(partitionIndex);
while (!ct.IsCancellationRequested)
{
    var command = await queue.DequeueAsync(ct);
    await ProcessCommandAsync(command);
}
```

---

## 9. Testing

- Unit tests for queue bounds and backpressure
- Unit tests for partition distribution uniformity
- Concurrency tests: multiple producers, single consumer per partition
- Performance benchmarks: throughput under contention

---

## 10. Dependencies

- `System.Threading.Channels` (Channel<T>)
- `Khaos.Metrics` (optional, for observability)
- `Khaos.Logging` (optional, for structured logging)
