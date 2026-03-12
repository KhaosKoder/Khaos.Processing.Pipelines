// Licensed under the MIT License. See LICENSE file for details.

namespace Khaos.Processing.Pipelines.Commands;

/// <summary>
/// Routes commands to partitioned queues based on routing key.
/// Thread-safe for concurrent dispatch from multiple producers.
/// </summary>
/// <typeparam name="TKey">The type of the routing key.</typeparam>
/// <typeparam name="TCommand">The type of command being dispatched.</typeparam>
public interface IRoutedCommandDispatcher<TKey, TCommand> : IDisposable
    where TCommand : IRoutableCommand<TKey>
{
    /// <summary>
    /// Gets the number of partitions.
    /// </summary>
    int PartitionCount { get; }

    /// <summary>
    /// Dispatches a command to the appropriate partition queue based on its routing key.
    /// </summary>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the dispatch operation.</returns>
    ValueTask<DispatchResult> DispatchAsync(TCommand command, CancellationToken ct = default);

    /// <summary>
    /// Gets the queue for a specific partition (for consumers).
    /// </summary>
    /// <param name="partitionIndex">The zero-based partition index.</param>
    /// <returns>The command queue for the specified partition.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when partition index is out of range.</exception>
    ICommandQueue<TCommand> GetPartitionQueue(int partitionIndex);

    /// <summary>
    /// Gets the partition index for a given routing key.
    /// </summary>
    /// <param name="routingKey">The routing key.</param>
    /// <returns>The partition index (0 to PartitionCount - 1).</returns>
    int GetPartitionIndex(TKey routingKey);

    /// <summary>
    /// Returns fill percentage for each partition (for monitoring).
    /// </summary>
    /// <returns>A list of fill percentages, one per partition.</returns>
    IReadOnlyList<int> GetPartitionFillPercentages();

    /// <summary>
    /// Gets aggregate statistics across all partitions.
    /// </summary>
    DispatcherStatistics GetStatistics();
}

/// <summary>
/// Statistics for the command dispatcher.
/// </summary>
public readonly struct DispatcherStatistics
{
    /// <summary>
    /// Gets the total number of commands currently queued across all partitions.
    /// </summary>
    public int TotalQueuedCommands { get; init; }

    /// <summary>
    /// Gets the total capacity across all partitions.
    /// </summary>
    public int TotalCapacity { get; init; }

    /// <summary>
    /// Gets the overall fill percentage (0-100).
    /// </summary>
    public int OverallFillPercentage { get; init; }

    /// <summary>
    /// Gets the maximum fill percentage among all partitions.
    /// </summary>
    public int MaxPartitionFillPercentage { get; init; }

    /// <summary>
    /// Gets the minimum fill percentage among all partitions.
    /// </summary>
    public int MinPartitionFillPercentage { get; init; }
}
