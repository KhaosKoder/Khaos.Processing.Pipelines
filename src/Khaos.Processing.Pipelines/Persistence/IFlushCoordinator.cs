// Licensed under the MIT License. See LICENSE file for details.

namespace Khaos.Processing.Pipelines.Persistence;

/// <summary>
/// Result of a flush operation.
/// </summary>
public readonly struct FlushResult
{
    /// <summary>
    /// Gets a value indicating whether a flush was performed.
    /// </summary>
    public bool Flushed { get; init; }

    /// <summary>
    /// Gets the number of items that were flushed.
    /// </summary>
    public int ItemsFlushed { get; init; }

    /// <summary>
    /// Gets the duration of the flush operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the name of the policy that triggered the flush.
    /// </summary>
    public string? TriggeringPolicy { get; init; }

    /// <summary>
    /// Gets the exception if the flush failed.
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// Gets a value indicating whether the flush was successful.
    /// </summary>
    public bool IsSuccess => Flushed && Error is null;

    /// <summary>
    /// Gets a result indicating no flush was performed.
    /// </summary>
    public static FlushResult NoFlush => new() { Flushed = false };

    /// <summary>
    /// Creates a successful flush result.
    /// </summary>
    /// <param name="itemsFlushed">Number of items flushed.</param>
    /// <param name="duration">Duration of the flush operation.</param>
    /// <param name="triggeringPolicy">Name of the policy that triggered the flush.</param>
    /// <returns>A successful flush result.</returns>
    public static FlushResult Success(int itemsFlushed, TimeSpan duration, string triggeringPolicy)
        => new()
        {
            Flushed = true,
            ItemsFlushed = itemsFlushed,
            Duration = duration,
            TriggeringPolicy = triggeringPolicy
        };

    /// <summary>
    /// Creates a failed flush result.
    /// </summary>
    /// <param name="error">The exception that caused the failure.</param>
    /// <returns>A failed flush result.</returns>
    public static FlushResult Failed(Exception error)
        => new()
        {
            Flushed = false,
            Error = error
        };
}

/// <summary>
/// Tracks dirty state and coordinates flush execution.
/// Thread-safe for concurrent dirty marking from multiple workers.
/// </summary>
/// <typeparam name="TKey">The type of key used to identify dirty items.</typeparam>
public interface IFlushCoordinator<TKey> : IDisposable
{
    /// <summary>
    /// Gets the current number of items marked dirty.
    /// </summary>
    int DirtyCount { get; }

    /// <summary>
    /// Marks an item as dirty.
    /// </summary>
    /// <param name="key">The key of the item to mark dirty.</param>
    void MarkDirty(TKey key);

    /// <summary>
    /// Marks an item as clean (after successful persistence).
    /// </summary>
    /// <param name="key">The key of the item to mark clean.</param>
    void MarkClean(TKey key);

    /// <summary>
    /// Checks if an item is currently dirty.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns><c>true</c> if the item is dirty; otherwise <c>false</c>.</returns>
    bool IsDirty(TKey key);

    /// <summary>
    /// Gets all dirty keys.
    /// </summary>
    /// <returns>A collection of all dirty keys.</returns>
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
    /// <param name="flushAction">Action that persists the dirty items.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Flush result indicating what happened.</returns>
    ValueTask<FlushResult> ForceFlushAsync(
        Func<IReadOnlyCollection<TKey>, CancellationToken, ValueTask> flushAction,
        CancellationToken ct = default);

    /// <summary>
    /// Sets external trigger flag for next check.
    /// </summary>
    void SetExternalTrigger();

    /// <summary>
    /// Clears the external trigger flag.
    /// </summary>
    void ClearExternalTrigger();

    /// <summary>
    /// Sets shutdown mode for next check.
    /// </summary>
    void SetShutdown();

    /// <summary>
    /// Gets statistics about the coordinator.
    /// </summary>
    FlushCoordinatorStatistics GetStatistics();
}

/// <summary>
/// Statistics about the flush coordinator.
/// </summary>
public readonly struct FlushCoordinatorStatistics
{
    /// <summary>
    /// Gets the current dirty count.
    /// </summary>
    public int DirtyCount { get; init; }

    /// <summary>
    /// Gets the time since last flush.
    /// </summary>
    public TimeSpan TimeSinceLastFlush { get; init; }

    /// <summary>
    /// Gets the total number of flushes performed.
    /// </summary>
    public int TotalFlushes { get; init; }

    /// <summary>
    /// Gets the total number of items flushed.
    /// </summary>
    public long TotalItemsFlushed { get; init; }

    /// <summary>
    /// Gets whether external trigger is set.
    /// </summary>
    public bool ExternalTriggerSet { get; init; }

    /// <summary>
    /// Gets whether shutdown mode is set.
    /// </summary>
    public bool IsShutdown { get; init; }
}
