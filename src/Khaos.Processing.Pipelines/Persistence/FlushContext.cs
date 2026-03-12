// Licensed under the MIT License. See LICENSE file for details.

namespace Khaos.Processing.Pipelines.Persistence;

/// <summary>
/// Context provided to flush policies for decision making.
/// Contains all relevant state for determining if a flush should occur.
/// </summary>
public readonly struct FlushContext
{
    /// <summary>
    /// Gets the number of items currently marked dirty.
    /// </summary>
    public int DirtyCount { get; init; }

    /// <summary>
    /// Gets the total number of items being tracked.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Gets the time elapsed since the last successful flush.
    /// </summary>
    public TimeSpan ElapsedSinceLastFlush { get; init; }

    /// <summary>
    /// Gets the current timestamp.
    /// </summary>
    public DateTimeOffset Now { get; init; }

    /// <summary>
    /// Gets the estimated memory used by dirty items in bytes.
    /// </summary>
    public long EstimatedDirtyMemoryBytes { get; init; }

    /// <summary>
    /// Gets the total estimated memory usage in bytes.
    /// </summary>
    public long EstimatedTotalMemoryBytes { get; init; }

    /// <summary>
    /// Gets a value indicating whether this check is being made during shutdown.
    /// </summary>
    public bool IsShutdown { get; init; }

    /// <summary>
    /// Gets a value indicating whether an external trigger is set
    /// (e.g., Kafka offset commit pending).
    /// </summary>
    public bool ExternalTrigger { get; init; }
}
