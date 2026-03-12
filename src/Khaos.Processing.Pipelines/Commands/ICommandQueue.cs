// Licensed under the MIT License. See LICENSE file for details.

using System.Diagnostics.CodeAnalysis;

namespace Khaos.Processing.Pipelines.Commands;

/// <summary>
/// Thread-safe bounded queue for commands to a single partition.
/// Designed for single consumer, multiple producer usage patterns.
/// </summary>
/// <typeparam name="TCommand">The type of command to queue.</typeparam>
public interface ICommandQueue<TCommand> where TCommand : ICommand
{
    /// <summary>
    /// Gets the current number of commands in the queue.
    /// This value is approximate under concurrent access.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the maximum capacity of the queue.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets the percentage of capacity currently used (0-100).
    /// </summary>
    int FillPercentage { get; }

    /// <summary>
    /// Attempts to enqueue a command without blocking.
    /// </summary>
    /// <param name="command">The command to enqueue.</param>
    /// <returns><c>true</c> if the command was enqueued; <c>false</c> if the queue is full.</returns>
    bool TryEnqueue(TCommand command);

    /// <summary>
    /// Enqueues a command, waiting asynchronously if the queue is full.
    /// </summary>
    /// <param name="command">The command to enqueue.</param>
    /// <param name="ct">Cancellation token to cancel the wait.</param>
    /// <returns><c>true</c> if enqueued before cancellation; <c>false</c> if cancelled or failed.</returns>
    ValueTask<bool> EnqueueAsync(TCommand command, CancellationToken ct = default);

    /// <summary>
    /// Tries to dequeue a command without blocking.
    /// </summary>
    /// <param name="command">When successful, contains the dequeued command.</param>
    /// <returns><c>true</c> if a command was dequeued; <c>false</c> if the queue is empty.</returns>
    bool TryDequeue([MaybeNullWhen(false)] out TCommand command);

    /// <summary>
    /// Dequeues a command, waiting asynchronously if the queue is empty.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the wait.</param>
    /// <returns>The dequeued command.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="ChannelClosedException">Thrown when the channel is closed while waiting.</exception>
    ValueTask<TCommand> DequeueAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads all available commands without blocking, up to the buffer size.
    /// </summary>
    /// <param name="buffer">Buffer to fill with commands.</param>
    /// <returns>The number of commands read into the buffer.</returns>
    int DrainTo(Span<TCommand> buffer);

    /// <summary>
    /// Reads all available commands without blocking.
    /// </summary>
    /// <returns>A list of all available commands.</returns>
    IReadOnlyList<TCommand> DrainAll();
}
