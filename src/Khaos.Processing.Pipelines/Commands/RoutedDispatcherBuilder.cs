// Licensed under the MIT License. See LICENSE file for details.

using Microsoft.Extensions.Logging;

namespace Khaos.Processing.Pipelines.Commands;

/// <summary>
/// Fluent builder for creating a routed command dispatcher.
/// </summary>
/// <typeparam name="TKey">The type of the routing key.</typeparam>
/// <typeparam name="TCommand">The type of command being dispatched.</typeparam>
public sealed class RoutedDispatcherBuilder<TKey, TCommand>
    where TCommand : IRoutableCommand<TKey>
{
    private int _partitionCount = 32;
    private int _queueCapacity = 10_000;
    private BackpressureBehavior _backpressureBehavior = BackpressureBehavior.Wait;
    private TimeSpan _backpressureTimeout = TimeSpan.FromSeconds(30);
    private Func<TKey, int>? _hashFunction;
    private ILogger? _logger;

    /// <summary>
    /// Sets the number of partitions.
    /// Must be a power of 2 for efficient hashing.
    /// </summary>
    /// <param name="count">The partition count (must be power of 2).</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when count is not a power of 2.</exception>
    public RoutedDispatcherBuilder<TKey, TCommand> WithPartitionCount(int count)
    {
        if (count <= 0 || !IsPowerOfTwo(count))
        {
            throw new ArgumentException("Partition count must be a positive power of 2.", nameof(count));
        }
        _partitionCount = count;
        return this;
    }

    /// <summary>
    /// Sets the capacity per partition queue.
    /// </summary>
    /// <param name="capacity">The queue capacity per partition.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when capacity is less than 1.</exception>
    public RoutedDispatcherBuilder<TKey, TCommand> WithQueueCapacity(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
        }
        _queueCapacity = capacity;
        return this;
    }

    /// <summary>
    /// Sets the backpressure behavior when queues are full.
    /// </summary>
    /// <param name="behavior">The backpressure behavior.</param>
    /// <param name="timeout">Optional timeout for wait behavior. Default is 30 seconds.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public RoutedDispatcherBuilder<TKey, TCommand> WithBackpressure(
        BackpressureBehavior behavior,
        TimeSpan? timeout = null)
    {
        _backpressureBehavior = behavior;
        if (timeout.HasValue)
        {
            if (timeout.Value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout cannot be negative.");
            }
            _backpressureTimeout = timeout.Value;
        }
        return this;
    }

    /// <summary>
    /// Sets a custom hash function for routing keys.
    /// The hash function should return consistent results for equal keys
    /// and distribute values evenly.
    /// </summary>
    /// <param name="hashFunction">The custom hash function.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public RoutedDispatcherBuilder<TKey, TCommand> WithHashFunction(Func<TKey, int> hashFunction)
    {
        _hashFunction = hashFunction ?? throw new ArgumentNullException(nameof(hashFunction));
        return this;
    }

    /// <summary>
    /// Sets the logger for dispatch operations.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public RoutedDispatcherBuilder<TKey, TCommand> WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Builds the routed command dispatcher with the configured options.
    /// </summary>
    /// <returns>A new <see cref="IRoutedCommandDispatcher{TKey, TCommand}"/> instance.</returns>
    public IRoutedCommandDispatcher<TKey, TCommand> Build()
    {
        var options = new RoutedDispatcherOptions
        {
            PartitionCount = _partitionCount,
            QueueCapacityPerPartition = _queueCapacity,
            BackpressureBehavior = _backpressureBehavior,
            BackpressureTimeout = _backpressureTimeout
        };

        return new RoutedCommandDispatcher<TKey, TCommand>(options, _hashFunction, _logger);
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}

/// <summary>
/// Factory entry point for creating routed command dispatchers.
/// </summary>
public static class CommandDispatcher
{
    /// <summary>
    /// Creates a new dispatcher builder for the specified key and command types.
    /// </summary>
    /// <typeparam name="TKey">The type of the routing key.</typeparam>
    /// <typeparam name="TCommand">The type of command being dispatched.</typeparam>
    /// <returns>A new builder instance.</returns>
    public static RoutedDispatcherBuilder<TKey, TCommand> Create<TKey, TCommand>()
        where TCommand : IRoutableCommand<TKey>
        => new();

    /// <summary>
    /// Creates a dispatcher with default options.
    /// </summary>
    /// <typeparam name="TKey">The type of the routing key.</typeparam>
    /// <typeparam name="TCommand">The type of command being dispatched.</typeparam>
    /// <returns>A new dispatcher with default configuration.</returns>
    public static IRoutedCommandDispatcher<TKey, TCommand> CreateDefault<TKey, TCommand>()
        where TCommand : IRoutableCommand<TKey>
        => new RoutedDispatcherBuilder<TKey, TCommand>().Build();
}
