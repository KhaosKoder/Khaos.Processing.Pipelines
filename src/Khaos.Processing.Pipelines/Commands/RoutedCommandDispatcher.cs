// Licensed under the MIT License. See LICENSE file for details.

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Khaos.Processing.Pipelines.Commands;

/// <summary>
/// Routes commands to partitioned queues based on routing key.
/// Thread-safe for concurrent dispatch from multiple producers.
/// </summary>
/// <typeparam name="TKey">The type of the routing key.</typeparam>
/// <typeparam name="TCommand">The type of command being dispatched.</typeparam>
internal sealed class RoutedCommandDispatcher<TKey, TCommand> : IRoutedCommandDispatcher<TKey, TCommand>
    where TCommand : IRoutableCommand<TKey>
{
    private readonly BoundedCommandQueue<TCommand>[] _partitions;
    private readonly Func<TKey, int> _hashFunction;
    private readonly int _partitionMask;
    private readonly RoutedDispatcherOptions _options;
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutedCommandDispatcher{TKey, TCommand}"/> class.
    /// </summary>
    /// <param name="options">The dispatcher options.</param>
    /// <param name="hashFunction">Optional custom hash function. If null, uses default hash.</param>
    /// <param name="logger">Optional logger.</param>
    public RoutedCommandDispatcher(
        RoutedDispatcherOptions options,
        Func<TKey, int>? hashFunction = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _hashFunction = hashFunction ?? DefaultHash;
        _logger = logger;
        _partitionMask = options.PartitionCount - 1;
        
        _partitions = new BoundedCommandQueue<TCommand>[options.PartitionCount];
        
        // For DropOldest, use Channel's built-in DropOldest mode.
        // For Wait and Reject, use Wait mode - we handle backpressure ourselves.
        var fullMode = options.BackpressureBehavior == BackpressureBehavior.DropOldest
            ? BoundedChannelFullMode.DropOldest
            : BoundedChannelFullMode.Wait;

        for (int i = 0; i < options.PartitionCount; i++)
        {
            _partitions[i] = new BoundedCommandQueue<TCommand>(
                options.QueueCapacityPerPartition, 
                fullMode);
        }
    }

    /// <inheritdoc/>
    public int PartitionCount => _partitions.Length;

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> DispatchAsync(TCommand command, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(command);

        int partitionIndex = GetPartitionIndex(command.RoutingKey);
        var queue = _partitions[partitionIndex];

        // Fast path: try to enqueue without waiting
        if (queue.TryEnqueue(command))
        {
            LogDispatchSuccess(partitionIndex, command);
            return DispatchResult.Success;
        }

        // Handle backpressure based on configured behavior
        return await HandleBackpressureAsync(queue, command, partitionIndex, ct).ConfigureAwait(false);
    }

    private async ValueTask<DispatchResult> HandleBackpressureAsync(
        BoundedCommandQueue<TCommand> queue,
        TCommand command,
        int partitionIndex,
        CancellationToken ct)
    {
        switch (_options.BackpressureBehavior)
        {
            case BackpressureBehavior.Reject:
                LogDispatchRejected(partitionIndex, command);
                return DispatchResult.Rejected;

            case BackpressureBehavior.DropOldest:
                // DropOldest mode in Channel automatically handles this
                // Try enqueue again - it should succeed now or drop oldest
                if (queue.TryEnqueue(command))
                {
                    LogDispatchSuccess(partitionIndex, command);
                    return DispatchResult.Success;
                }
                return DispatchResult.Rejected;

            case BackpressureBehavior.Wait:
            default:
                return await WaitAndEnqueueAsync(queue, command, partitionIndex, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask<DispatchResult> WaitAndEnqueueAsync(
        BoundedCommandQueue<TCommand> queue,
        TCommand command,
        int partitionIndex,
        CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(_options.BackpressureTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            if (await queue.EnqueueAsync(command, linkedCts.Token).ConfigureAwait(false))
            {
                LogDispatchSuccess(partitionIndex, command);
                return DispatchResult.Success;
            }
            return DispatchResult.Rejected;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogDispatchCancelled(partitionIndex, command);
            return DispatchResult.Cancelled;
        }
        catch (OperationCanceledException)
        {
            LogDispatchTimeout(partitionIndex, command);
            return DispatchResult.Timeout;
        }
    }

    /// <inheritdoc/>
    public ICommandQueue<TCommand> GetPartitionQueue(int partitionIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (partitionIndex < 0 || partitionIndex >= _partitions.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partitionIndex),
                $"Partition index must be between 0 and {_partitions.Length - 1}.");
        }

        return _partitions[partitionIndex];
    }

    /// <inheritdoc/>
    public int GetPartitionIndex(TKey routingKey)
    {
        // Bit-mask for fast modulo (works because partition count is power of 2)
        return _hashFunction(routingKey) & _partitionMask;
    }

    /// <inheritdoc/>
    public IReadOnlyList<int> GetPartitionFillPercentages()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var percentages = new int[_partitions.Length];
        for (int i = 0; i < _partitions.Length; i++)
        {
            percentages[i] = _partitions[i].FillPercentage;
        }
        return percentages;
    }

    /// <inheritdoc/>
    public DispatcherStatistics GetStatistics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int totalQueued = 0;
        int minFill = int.MaxValue;
        int maxFill = int.MinValue;

        foreach (var partition in _partitions)
        {
            totalQueued += partition.Count;
            var fill = partition.FillPercentage;
            if (fill < minFill) minFill = fill;
            if (fill > maxFill) maxFill = fill;
        }

        int totalCapacity = _partitions.Length * _options.QueueCapacityPerPartition;
        int overallFill = totalCapacity > 0 ? (int)((totalQueued * 100L) / totalCapacity) : 0;

        return new DispatcherStatistics
        {
            TotalQueuedCommands = totalQueued,
            TotalCapacity = totalCapacity,
            OverallFillPercentage = overallFill,
            MaxPartitionFillPercentage = maxFill == int.MinValue ? 0 : maxFill,
            MinPartitionFillPercentage = minFill == int.MaxValue ? 0 : minFill
        };
    }

    /// <summary>
    /// Default hash function that spreads keys across partitions evenly.
    /// Uses XOR-shift mixing to improve distribution.
    /// </summary>
    private static int DefaultHash(TKey key)
    {
        // Use XOR-shift to spread hash bits
        int hash = key?.GetHashCode() ?? 0;
        
        // Mix the bits using multiplication and XOR-shift (MurmurHash3 finalizer)
        unchecked
        {
            hash ^= hash >> 16;
            hash *= unchecked((int)0x85ebca6b);
            hash ^= hash >> 13;
            hash *= unchecked((int)0xc2b2ae35);
            hash ^= hash >> 16;
        }
        
        return hash & int.MaxValue; // Ensure positive
    }

    #region Logging

    private void LogDispatchSuccess(int partitionIndex, TCommand command)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Command {CommandType} dispatched to partition {PartitionIndex}",
                typeof(TCommand).Name,
                partitionIndex);
        }
    }

    private void LogDispatchRejected(int partitionIndex, TCommand command)
    {
        _logger?.LogWarning(
            "Command {CommandType} rejected - partition {PartitionIndex} queue full",
            typeof(TCommand).Name,
            partitionIndex);
    }

    private void LogDispatchTimeout(int partitionIndex, TCommand command)
    {
        _logger?.LogWarning(
            "Command {CommandType} timed out waiting for partition {PartitionIndex}",
            typeof(TCommand).Name,
            partitionIndex);
    }

    private void LogDispatchCancelled(int partitionIndex, TCommand command)
    {
        _logger?.LogInformation(
            "Command {CommandType} dispatch cancelled for partition {PartitionIndex}",
            typeof(TCommand).Name,
            partitionIndex);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Complete all queues to signal consumers to stop
        foreach (var partition in _partitions)
        {
            partition.Complete();
        }
    }
}
