# Khaos.Processing.Pipelines.Persistence — Flush Policy Specification

**Version:** 1.0  
**Date:** 2026-03-12  
**Namespace:** `Khaos.Processing.Pipelines.Persistence`  

---

## 1. Purpose

Provide a **generic, configurable write-behind flush policy system** for determining when dirty state should be persisted. Supports multiple trigger conditions (time, count, memory pressure) with composable policy combinations.

---

## 2. Use Cases

1. **Write-behind caching**: Batch state flushes for efficiency
2. **Memory-bounded systems**: Flush when memory pressure increases
3. **Kafka offset coupling**: Flush before committing offsets
4. **Periodic snapshots**: Time-based checkpointing
5. **Event-driven persistence**: Flush after N mutations

---

## 3. Design Principles

### 3.1 Policy Composition
- Multiple policies can be combined (any match triggers flush)
- Policies are stateless and reusable
- Easy to add custom policies

### 3.2 Separation of Concerns
- **Policy**: Decides *if* flush should happen
- **Coordinator**: Tracks dirty state and orchestrates flush execution
- **Executor**: Actually performs the persistence (provided by caller)

### 3.3 Integration with Khaos Ecosystem
- Uses `Khaos.Time.ISystemClock` for testable time
- Uses `Khaos.Metrics` for flush statistics
- Uses `Khaos.Logging` for flush events
- Configuration via `Khaos.MultiApp.Settings` when used in applications

---

## 4. Public API

### 4.1 Flush Context

```csharp
namespace Khaos.Processing.Pipelines.Persistence;

/// <summary>
/// Context provided to flush policies for decision making.
/// </summary>
public readonly struct FlushContext
{
    /// <summary>
    /// Number of items currently marked dirty.
    /// </summary>
    public int DirtyCount { get; init; }
    
    /// <summary>
    /// Total number of items being tracked.
    /// </summary>
    public int TotalCount { get; init; }
    
    /// <summary>
    /// Time since the last successful flush.
    /// </summary>
    public TimeSpan ElapsedSinceLastFlush { get; init; }
    
    /// <summary>
    /// Current timestamp (from ISystemClock).
    /// </summary>
    public DateTimeOffset Now { get; init; }
    
    /// <summary>
    /// Estimated memory used by dirty items in bytes.
    /// </summary>
    public long EstimatedDirtyMemoryBytes { get; init; }
    
    /// <summary>
    /// Total estimated memory usage in bytes.
    /// </summary>
    public long EstimatedTotalMemoryBytes { get; init; }
    
    /// <summary>
    /// True if this check is being made during shutdown.
    /// </summary>
    public bool IsShutdown { get; init; }
    
    /// <summary>
    /// Optional: external trigger flag (e.g., Kafka offset commit pending).
    /// </summary>
    public bool ExternalTrigger { get; init; }
}
```

### 4.2 Flush Policy Interface

```csharp
/// <summary>
/// Determines whether a flush should occur based on current context.
/// Policies must be stateless and thread-safe.
/// </summary>
public interface IFlushPolicy
{
    /// <summary>
    /// Unique name for logging and metrics.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Evaluates whether flush should occur.
    /// </summary>
    /// <param name="context">Current flush context.</param>
    /// <returns>True if flush should be triggered.</returns>
    bool ShouldFlush(in FlushContext context);
}
```

### 4.3 Built-in Policies

```csharp
/// <summary>
/// Triggers flush when elapsed time exceeds threshold.
/// </summary>
public sealed class TimeBasedFlushPolicy : IFlushPolicy
{
    public string Name => "TimeBased";
    
    public TimeBasedFlushPolicy(TimeSpan interval);
    
    public bool ShouldFlush(in FlushContext context)
        => context.ElapsedSinceLastFlush >= _interval;
}

/// <summary>
/// Triggers flush when dirty count exceeds threshold.
/// </summary>
public sealed class CountBasedFlushPolicy : IFlushPolicy
{
    public string Name => "CountBased";
    
    public CountBasedFlushPolicy(int threshold);
    
    public bool ShouldFlush(in FlushContext context)
        => context.DirtyCount >= _threshold;
}

/// <summary>
/// Triggers flush when estimated dirty memory exceeds threshold.
/// </summary>
public sealed class MemoryPressureFlushPolicy : IFlushPolicy
{
    public string Name => "MemoryPressure";
    
    public MemoryPressureFlushPolicy(long thresholdBytes);
    
    public bool ShouldFlush(in FlushContext context)
        => context.EstimatedDirtyMemoryBytes >= _thresholdBytes;
}

/// <summary>
/// Triggers flush when dirty count exceeds percentage of total.
/// </summary>
public sealed class DirtyRatioFlushPolicy : IFlushPolicy
{
    public string Name => "DirtyRatio";
    
    public DirtyRatioFlushPolicy(double threshold); // 0.0 - 1.0
    
    public bool ShouldFlush(in FlushContext context)
        => context.TotalCount > 0 && 
           (double)context.DirtyCount / context.TotalCount >= _threshold;
}

/// <summary>
/// Triggers flush when external trigger is set (e.g., offset commit).
/// </summary>
public sealed class ExternalTriggerFlushPolicy : IFlushPolicy
{
    public string Name => "ExternalTrigger";
    
    public bool ShouldFlush(in FlushContext context)
        => context.ExternalTrigger;
}

/// <summary>
/// Always triggers flush during shutdown if any dirty items exist.
/// </summary>
public sealed class ShutdownFlushPolicy : IFlushPolicy
{
    public string Name => "Shutdown";
    
    public bool ShouldFlush(in FlushContext context)
        => context.IsShutdown && context.DirtyCount > 0;
}

/// <summary>
/// Combines multiple policies; triggers if ANY policy triggers.
/// </summary>
public sealed class CompositeFlushPolicy : IFlushPolicy
{
    public string Name { get; }
    
    public CompositeFlushPolicy(string name, params IFlushPolicy[] policies);
    public CompositeFlushPolicy(string name, IEnumerable<IFlushPolicy> policies);
    
    public bool ShouldFlush(in FlushContext context)
        => _policies.Any(p => p.ShouldFlush(context));
    
    /// <summary>
    /// Returns which policies triggered (for diagnostics).
    /// </summary>
    public IEnumerable<string> GetTriggeringPolicies(in FlushContext context);
}
```

### 4.4 Flush Coordinator

```csharp
/// <summary>
/// Tracks dirty state and coordinates flush execution.
/// Thread-safe for concurrent dirty marking from multiple workers.
/// </summary>
public interface IFlushCoordinator<TKey> : IDisposable
{
    /// <summary>
    /// Current number of items marked dirty.
    /// </summary>
    int DirtyCount { get; }
    
    /// <summary>
    /// Marks an item as dirty.
    /// </summary>
    void MarkDirty(TKey key);
    
    /// <summary>
    /// Marks an item as clean (after successful persistence).
    /// </summary>
    void MarkClean(TKey key);
    
    /// <summary>
    /// Checks if an item is currently dirty.
    /// </summary>
    bool IsDirty(TKey key);
    
    /// <summary>
    /// Gets all dirty keys.
    /// </summary>
    IReadOnlyCollection<TKey> GetDirtyKeys();
    
    /// <summary>
    /// Checks policies and executes flush if triggered.
    /// </summary>
    /// <param name="flushAction">Action that persists the dirty items.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Flush result indicating what happened.</returns>
    ValueTask<FlushResult> CheckAndFlushAsync(
        Func<IReadOnlyCollection<TKey>, CancellationToken, ValueTask> flushAction,
        CancellationToken ct = default);
    
    /// <summary>
    /// Forces immediate flush regardless of policy.
    /// </summary>
    ValueTask<FlushResult> ForceFlushAsync(
        Func<IReadOnlyCollection<TKey>, CancellationToken, ValueTask> flushAction,
        CancellationToken ct = default);
    
    /// <summary>
    /// Sets external trigger flag for next check.
    /// </summary>
    void SetExternalTrigger();
    
    /// <summary>
    /// Sets shutdown mode for next check.
    /// </summary>
    void SetShutdown();
}

public readonly struct FlushResult
{
    public bool Flushed { get; init; }
    public int ItemsFlushed { get; init; }
    public TimeSpan Duration { get; init; }
    public string? TriggeringPolicy { get; init; }
    public Exception? Error { get; init; }
    
    public static FlushResult NoFlush => new() { Flushed = false };
    
    public static FlushResult Success(int items, TimeSpan duration, string policy)
        => new() { Flushed = true, ItemsFlushed = items, Duration = duration, TriggeringPolicy = policy };
    
    public static FlushResult Failed(Exception error)
        => new() { Flushed = false, Error = error };
}
```

### 4.5 Builder

```csharp
/// <summary>
/// Fluent builder for creating a flush coordinator.
/// </summary>
public sealed class FlushCoordinatorBuilder<TKey>
{
    public FlushCoordinatorBuilder<TKey> AddPolicy(IFlushPolicy policy);
    
    public FlushCoordinatorBuilder<TKey> WithTimeBasedFlush(TimeSpan interval);
    
    public FlushCoordinatorBuilder<TKey> WithCountBasedFlush(int threshold);
    
    public FlushCoordinatorBuilder<TKey> WithMemoryPressureFlush(long thresholdBytes);
    
    public FlushCoordinatorBuilder<TKey> WithDirtyRatioFlush(double threshold);
    
    public FlushCoordinatorBuilder<TKey> WithExternalTriggerFlush();
    
    public FlushCoordinatorBuilder<TKey> WithShutdownFlush();
    
    public FlushCoordinatorBuilder<TKey> WithClock(ISystemClock clock);
    
    public FlushCoordinatorBuilder<TKey> WithMetrics(IOperationMonitor monitor);
    
    public FlushCoordinatorBuilder<TKey> WithLogger(ILogger logger);
    
    public FlushCoordinatorBuilder<TKey> WithMemoryEstimator(
        Func<TKey, long> estimator);
    
    public IFlushCoordinator<TKey> Build();
}

// Factory entry point
public static class FlushCoordinator
{
    public static FlushCoordinatorBuilder<TKey> Create<TKey>()
        => new FlushCoordinatorBuilder<TKey>();
}
```

---

## 5. Implementation Details

### 5.1 Coordinator Implementation

```csharp
internal sealed class FlushCoordinatorImpl<TKey> : IFlushCoordinator<TKey>
{
    private readonly ConcurrentDictionary<TKey, byte> _dirtySet = new();
    private readonly IFlushPolicy _policy;
    private readonly ISystemClock _clock;
    private readonly IOperationMonitor? _monitor;
    private readonly Func<TKey, long>? _memoryEstimator;
    
    private DateTimeOffset _lastFlushTime;
    private volatile bool _externalTrigger;
    private volatile bool _isShutdown;
    
    public void MarkDirty(TKey key)
    {
        _dirtySet.TryAdd(key, 0);
    }
    
    public void MarkClean(TKey key)
    {
        _dirtySet.TryRemove(key, out _);
    }
    
    public async ValueTask<FlushResult> CheckAndFlushAsync(
        Func<IReadOnlyCollection<TKey>, CancellationToken, ValueTask> flushAction,
        CancellationToken ct)
    {
        var context = BuildContext();
        
        if (!_policy.ShouldFlush(context))
            return FlushResult.NoFlush;
        
        return await ExecuteFlushAsync(flushAction, context, ct);
    }
    
    private FlushContext BuildContext()
    {
        var now = _clock.UtcNow;
        var dirtyKeys = _dirtySet.Keys.ToArray();
        
        long dirtyMemory = 0;
        if (_memoryEstimator != null)
        {
            foreach (var key in dirtyKeys)
                dirtyMemory += _memoryEstimator(key);
        }
        
        return new FlushContext
        {
            DirtyCount = dirtyKeys.Length,
            TotalCount = dirtyKeys.Length, // Coordinator only tracks dirty
            ElapsedSinceLastFlush = now - _lastFlushTime,
            Now = now,
            EstimatedDirtyMemoryBytes = dirtyMemory,
            ExternalTrigger = Interlocked.Exchange(ref _externalTrigger, false),
            IsShutdown = _isShutdown
        };
    }
    
    private async ValueTask<FlushResult> ExecuteFlushAsync(
        Func<IReadOnlyCollection<TKey>, CancellationToken, ValueTask> flushAction,
        FlushContext context,
        CancellationToken ct)
    {
        var dirtyKeys = GetDirtyKeys();
        if (dirtyKeys.Count == 0)
            return FlushResult.NoFlush;
        
        var sw = Stopwatch.StartNew();
        
        using var scope = _monitor?.Begin("flush", new OperationTags
        {
            { "count", dirtyKeys.Count.ToString() }
        });
        
        try
        {
            await flushAction(dirtyKeys, ct);
            
            // Mark all as clean after successful flush
            foreach (var key in dirtyKeys)
                MarkClean(key);
            
            _lastFlushTime = _clock.UtcNow;
            
            var triggeringPolicy = (_policy as CompositeFlushPolicy)?
                .GetTriggeringPolicies(context).FirstOrDefault() ?? _policy.Name;
            
            return FlushResult.Success(dirtyKeys.Count, sw.Elapsed, triggeringPolicy);
        }
        catch (Exception ex)
        {
            scope?.MarkFailed();
            return FlushResult.Failed(ex);
        }
    }
}
```

---

## 6. Metrics Integration

When `IOperationMonitor` is provided:

| Metric | Type | Description |
|--------|------|-------------|
| `flush.executed` | Operation | Flush execution with timing |
| `flush.items_flushed` | Counter | Number of items flushed |
| `flush.dirty_count` | Gauge | Current dirty count |
| `flush.skipped` | Counter | Checks that didn't trigger flush |
| `flush.failed` | Counter | Failed flush attempts |

Tags: `trigger_policy`

---

## 7. Logging Integration

```csharp
[LogEventSource(LoggerRootTypeName = "FlushLogger", BasePath = "Flush")]
public enum FlushLogEvents
{
    FLUSH_Started = 1000,
    FLUSH_Completed = 1001,
    FLUSH_Failed = 1002,
    FLUSH_Skipped = 1003,
    POLICY_Triggered = 2000,
}
```

---

## 8. Usage Example

```csharp
// Create coordinator with multiple policies
var flushCoordinator = FlushCoordinator.Create<string>()
    .WithTimeBasedFlush(TimeSpan.FromSeconds(30))
    .WithCountBasedFlush(1000)
    .WithMemoryPressureFlush(512 * 1024 * 1024) // 512MB
    .WithShutdownFlush()
    .WithExternalTriggerFlush()
    .WithClock(systemClock)
    .WithMetrics(operationMonitor)
    .Build();

// In processing loop
flushCoordinator.MarkDirty(clientId);
// ... process more events ...

// Periodic check
var result = await flushCoordinator.CheckAndFlushAsync(
    async (dirtyKeys, ct) =>
    {
        foreach (var key in dirtyKeys)
        {
            var state = await _hotset.GetAsync(key);
            await _fasterStore.UpsertAsync(key, state, ct);
        }
    },
    cancellationToken);

if (result.Flushed)
    _log.Flush.Completed.LogInformation("Flushed {Count} items via {Policy}", 
        result.ItemsFlushed, result.TriggeringPolicy);

// Before Kafka offset commit
flushCoordinator.SetExternalTrigger();
await flushCoordinator.CheckAndFlushAsync(flushAction, ct);
await _consumer.CommitAsync();

// During shutdown (via AppLifeCycle shutdown flow)
flushCoordinator.SetShutdown();
await flushCoordinator.ForceFlushAsync(flushAction, ct);
```

---

## 9. Configuration via Khaos.MultiApp.Settings

When used in an application, policies can be configured via settings:

| Key | Type | Description |
|-----|------|-------------|
| `Flush:TimeIntervalSeconds` | int | Time-based flush interval |
| `Flush:CountThreshold` | int | Count-based threshold |
| `Flush:MemoryThresholdMB` | int | Memory pressure threshold |
| `Flush:DirtyRatio` | double | Dirty ratio threshold (0.0-1.0) |

```csharp
// Load policies from settings
var settings = serviceProvider.GetRequiredService<IOptionsMonitor<FlushSettings>>();

var builder = FlushCoordinator.Create<string>()
    .WithClock(clock)
    .WithMetrics(monitor);

if (settings.CurrentValue.TimeIntervalSeconds > 0)
    builder.WithTimeBasedFlush(TimeSpan.FromSeconds(settings.CurrentValue.TimeIntervalSeconds));

if (settings.CurrentValue.CountThreshold > 0)
    builder.WithCountBasedFlush(settings.CurrentValue.CountThreshold);

// ... etc
```

---

## 10. Testing

- Unit tests for each policy in isolation
- Unit tests for composite policy combinations
- Unit tests for coordinator dirty tracking
- Integration tests with virtual clock
- Concurrency tests: multiple workers marking dirty

---

## 11. Dependencies

- `Khaos.Time` (ISystemClock)
- `Khaos.Metrics` (optional, for observability)
- `Khaos.Logging` (optional, for structured logging)
