// Licensed under the MIT License. See LICENSE file for details.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace Khaos.Processing.Pipelines.Commands;

/// <summary>
/// Bounded command queue backed by <see cref="Channel{T}"/>.
/// Designed for single consumer, multiple producer usage patterns.
/// </summary>
/// <typeparam name="TCommand">The type of command to queue.</typeparam>
internal sealed class BoundedCommandQueue<TCommand> : ICommandQueue<TCommand>
    where TCommand : ICommand
{
    private readonly Channel<TCommand> _channel;
    private readonly ChannelWriter<TCommand> _writer;
    private readonly ChannelReader<TCommand> _reader;
    private int _approximateCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="BoundedCommandQueue{TCommand}"/> class.
    /// </summary>
    /// <param name="capacity">The maximum capacity of the queue.</param>
    /// <param name="fullMode">The behavior when the queue is full.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when capacity is less than 1.</exception>
    public BoundedCommandQueue(int capacity, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");

        _channel = Channel.CreateBounded<TCommand>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,    // Single consumer per partition
            SingleWriter = false,   // Multiple producers
            FullMode = fullMode,
            AllowSynchronousContinuations = false // Avoid deadlocks
        });

        _writer = _channel.Writer;
        _reader = _channel.Reader;
        Capacity = capacity;
    }

    /// <inheritdoc/>
    public int Count => Volatile.Read(ref _approximateCount);

    /// <inheritdoc/>
    public int Capacity { get; }

    /// <inheritdoc/>
    public int FillPercentage
    {
        get
        {
            var count = Count;
            return Capacity > 0 ? (int)((count * 100L) / Capacity) : 0;
        }
    }

    /// <inheritdoc/>
    public bool TryEnqueue(TCommand command)
    {
        if (_writer.TryWrite(command))
        {
            Interlocked.Increment(ref _approximateCount);
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> EnqueueAsync(TCommand command, CancellationToken ct = default)
    {
        try
        {
            await _writer.WriteAsync(command, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _approximateCount);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
        // Let OperationCanceledException propagate for proper timeout/cancellation handling
    }

    /// <inheritdoc/>
    public bool TryDequeue([MaybeNullWhen(false)] out TCommand command)
    {
        if (_reader.TryRead(out command))
        {
            Interlocked.Decrement(ref _approximateCount);
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public async ValueTask<TCommand> DequeueAsync(CancellationToken ct = default)
    {
        var command = await _reader.ReadAsync(ct).ConfigureAwait(false);
        Interlocked.Decrement(ref _approximateCount);
        return command;
    }

    /// <inheritdoc/>
    public int DrainTo(Span<TCommand> buffer)
    {
        int count = 0;
        while (count < buffer.Length && _reader.TryRead(out var command))
        {
            buffer[count++] = command;
            Interlocked.Decrement(ref _approximateCount);
        }
        return count;
    }

    /// <inheritdoc/>
    public IReadOnlyList<TCommand> DrainAll()
    {
        var list = new List<TCommand>();
        while (_reader.TryRead(out var command))
        {
            list.Add(command);
            Interlocked.Decrement(ref _approximateCount);
        }
        return list;
    }

    /// <summary>
    /// Marks the queue as complete, preventing further enqueues.
    /// </summary>
    public void Complete()
    {
        _writer.TryComplete();
    }

    /// <summary>
    /// Gets a value indicating whether the queue has been completed.
    /// </summary>
    public bool IsCompleted => _reader.Completion.IsCompleted;
}
