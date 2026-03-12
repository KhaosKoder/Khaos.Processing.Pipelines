// Licensed under the MIT License. See LICENSE file for details.

namespace Khaos.Processing.Pipelines.Commands;

/// <summary>
/// Configuration for the routed command dispatcher.
/// </summary>
public sealed class RoutedDispatcherOptions
{
    /// <summary>
    /// Gets or sets the number of partitions (buckets).
    /// Must be a power of 2 for efficient hashing.
    /// Default is 32.
    /// </summary>
    public int PartitionCount { get; set; } = 32;

    /// <summary>
    /// Gets or sets the capacity per partition queue.
    /// Default is 10,000.
    /// </summary>
    public int QueueCapacityPerPartition { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the backpressure behavior when a queue is full.
    /// Default is <see cref="BackpressureBehavior.Wait"/>.
    /// </summary>
    public BackpressureBehavior BackpressureBehavior { get; set; } = BackpressureBehavior.Wait;

    /// <summary>
    /// Gets or sets the maximum time to wait when backpressure behavior is <see cref="BackpressureBehavior.Wait"/>.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan BackpressureTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Validates the options and throws if invalid.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when options are invalid.</exception>
    public void Validate()
    {
        if (PartitionCount <= 0)
            throw new ArgumentException("PartitionCount must be positive.", nameof(PartitionCount));

        if (!IsPowerOfTwo(PartitionCount))
            throw new ArgumentException("PartitionCount must be a power of 2.", nameof(PartitionCount));

        if (QueueCapacityPerPartition <= 0)
            throw new ArgumentException("QueueCapacityPerPartition must be positive.", nameof(QueueCapacityPerPartition));

        if (BackpressureTimeout < TimeSpan.Zero)
            throw new ArgumentException("BackpressureTimeout cannot be negative.", nameof(BackpressureTimeout));
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}

/// <summary>
/// Defines the behavior when a command queue is full.
/// </summary>
public enum BackpressureBehavior
{
    /// <summary>
    /// Wait asynchronously until space is available or timeout occurs.
    /// </summary>
    Wait,

    /// <summary>
    /// Reject immediately if the queue is full.
    /// </summary>
    Reject,

    /// <summary>
    /// Drop the oldest command to make room for the new one.
    /// </summary>
    DropOldest
}

/// <summary>
/// Result of a command dispatch operation.
/// </summary>
public enum DispatchResult
{
    /// <summary>
    /// Command was successfully enqueued.
    /// </summary>
    Success,

    /// <summary>
    /// Queue was full and backpressure behavior is <see cref="BackpressureBehavior.Reject"/>.
    /// </summary>
    Rejected,

    /// <summary>
    /// Wait timed out before space became available.
    /// </summary>
    Timeout,

    /// <summary>
    /// Operation was cancelled via cancellation token.
    /// </summary>
    Cancelled
}
