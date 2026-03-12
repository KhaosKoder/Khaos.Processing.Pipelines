// Licensed under the MIT License. See LICENSE file for details.

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Khaos.Processing.Pipelines.Persistence;

/// <summary>
/// Tracks dirty state and coordinates flush execution.
/// Thread-safe for concurrent dirty marking from multiple workers.
/// </summary>
/// <typeparam name="TKey">The type of key used to identify dirty items.</typeparam>
internal sealed class FlushCoordinatorImpl<TKey> : IFlushCoordinator<TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, byte> _dirtySet = new();
    private readonly IFlushPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger? _logger;
    private readonly Func<TKey, long>? _memoryEstimator;
    private readonly object _flushLock = new();

    private DateTimeOffset _lastFlushTime;
    private int _externalTrigger; // 0 = false, 1 = true (for Interlocked)
    private int _isShutdown; // 0 = false, 1 = true
    private int _totalFlushes;
    private long _totalItemsFlushed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlushCoordinatorImpl{TKey}"/> class.
    /// </summary>
    public FlushCoordinatorImpl(
        IFlushPolicy policy,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        Func<TKey, long>? memoryEstimator = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        _policy = policy;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
        _memoryEstimator = memoryEstimator;
        _lastFlushTime = _timeProvider.GetUtcNow();
    }

    /// <inheritdoc/>
    public int DirtyCount => _dirtySet.Count;

    /// <inheritdoc/>
    public void MarkDirty(TKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dirtySet.TryAdd(key, 0);
    }

    /// <inheritdoc/>
    public void MarkClean(TKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dirtySet.TryRemove(key, out _);
    }

    /// <inheritdoc/>
    public bool IsDirty(TKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _dirtySet.ContainsKey(key);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<TKey> GetDirtyKeys()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _dirtySet.Keys.ToArray();
    }

    /// <inheritdoc/>
    public async ValueTask<FlushResult> CheckAndFlushAsync(
        Func<IReadOnlyCollection<TKey>, CancellationToken, ValueTask> flushAction,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(flushAction);

        var context = BuildContext();

        if (!_policy.ShouldFlush(context))
            return FlushResult.NoFlush;

        return await ExecuteFlushAsync(flushAction, context, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<FlushResult> ForceFlushAsync(
        Func<IReadOnlyCollection<TKey>, CancellationToken, ValueTask> flushAction,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(flushAction);

        var context = BuildContext();
        return await ExecuteFlushAsync(flushAction, context, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void SetExternalTrigger()
    {
        Interlocked.Exchange(ref _externalTrigger, 1);
    }

    /// <inheritdoc/>
    public void ClearExternalTrigger()
    {
        Interlocked.Exchange(ref _externalTrigger, 0);
    }

    /// <inheritdoc/>
    public void SetShutdown()
    {
        Interlocked.Exchange(ref _isShutdown, 1);
    }

    /// <inheritdoc/>
    public FlushCoordinatorStatistics GetStatistics()
    {
        var now = _timeProvider.GetUtcNow();
        return new FlushCoordinatorStatistics
        {
            DirtyCount = _dirtySet.Count,
            TimeSinceLastFlush = now - _lastFlushTime,
            TotalFlushes = Volatile.Read(ref _totalFlushes),
            TotalItemsFlushed = Volatile.Read(ref _totalItemsFlushed),
            ExternalTriggerSet = Volatile.Read(ref _externalTrigger) == 1,
            IsShutdown = Volatile.Read(ref _isShutdown) == 1
        };
    }

    private FlushContext BuildContext()
    {
        var now = _timeProvider.GetUtcNow();
        var dirtyKeys = _dirtySet.Keys.ToArray();

        long dirtyMemory = 0;
        if (_memoryEstimator != null)
        {
            foreach (var key in dirtyKeys)
            {
                dirtyMemory += _memoryEstimator(key);
            }
        }

        return new FlushContext
        {
            DirtyCount = dirtyKeys.Length,
            TotalCount = dirtyKeys.Length, // Coordinator only tracks dirty items
            ElapsedSinceLastFlush = now - _lastFlushTime,
            Now = now,
            EstimatedDirtyMemoryBytes = dirtyMemory,
            ExternalTrigger = Interlocked.Exchange(ref _externalTrigger, 0) == 1,
            IsShutdown = Volatile.Read(ref _isShutdown) == 1
        };
    }

    private async ValueTask<FlushResult> ExecuteFlushAsync(
        Func<IReadOnlyCollection<TKey>, CancellationToken, ValueTask> flushAction,
        FlushContext context,
        CancellationToken ct)
    {
        // Get dirty keys snapshot
        var dirtyKeys = GetDirtyKeys();
        if (dirtyKeys.Count == 0)
            return FlushResult.NoFlush;

        var sw = Stopwatch.StartNew();

        try
        {
            // Execute flush with lock to prevent concurrent flushes
            lock (_flushLock)
            {
                // Re-check dirty keys in case they were flushed by another thread
                dirtyKeys = GetDirtyKeys();
                if (dirtyKeys.Count == 0)
                    return FlushResult.NoFlush;
            }

            _logger?.LogDebug(
                "Starting flush of {DirtyCount} items",
                dirtyKeys.Count);

            await flushAction(dirtyKeys, ct).ConfigureAwait(false);

            // Mark all as clean after successful flush
            foreach (var key in dirtyKeys)
            {
                MarkClean(key);
            }

            _lastFlushTime = _timeProvider.GetUtcNow();
            Interlocked.Increment(ref _totalFlushes);
            Interlocked.Add(ref _totalItemsFlushed, dirtyKeys.Count);

            sw.Stop();

            var triggeringPolicy = GetTriggeringPolicyName(context);

            _logger?.LogInformation(
                "Flush completed: {ItemCount} items in {Duration:N2}ms, triggered by {Policy}",
                dirtyKeys.Count,
                sw.Elapsed.TotalMilliseconds,
                triggeringPolicy);

            return FlushResult.Success(dirtyKeys.Count, sw.Elapsed, triggeringPolicy);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Flush cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Flush failed after {Duration:N2}ms", sw.Elapsed.TotalMilliseconds);
            return FlushResult.Failed(ex);
        }
    }

    private string GetTriggeringPolicyName(in FlushContext context)
    {
        if (_policy is CompositeFlushPolicy composite)
        {
            var triggering = composite.GetTriggeringPolicies(context);
            return string.Join(", ", triggering);
        }
        return _policy.Name;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dirtySet.Clear();
    }
}
