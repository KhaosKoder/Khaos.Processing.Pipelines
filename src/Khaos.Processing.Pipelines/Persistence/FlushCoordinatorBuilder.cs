// Licensed under the MIT License. See LICENSE file for details.

using Microsoft.Extensions.Logging;

namespace Khaos.Processing.Pipelines.Persistence;

/// <summary>
/// Fluent builder for creating a flush coordinator.
/// </summary>
/// <typeparam name="TKey">The type of key used to identify dirty items.</typeparam>
public sealed class FlushCoordinatorBuilder<TKey>
    where TKey : notnull
{
    private readonly List<IFlushPolicy> _policies = new();
    private TimeProvider? _timeProvider;
    private ILogger? _logger;
    private Func<TKey, long>? _memoryEstimator;

    /// <summary>
    /// Adds a custom flush policy.
    /// </summary>
    /// <param name="policy">The policy to add.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public FlushCoordinatorBuilder<TKey> AddPolicy(IFlushPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policies.Add(policy);
        return this;
    }

    /// <summary>
    /// Adds a time-based flush policy.
    /// </summary>
    /// <param name="interval">The time interval after which flush should be triggered.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public FlushCoordinatorBuilder<TKey> WithTimeBasedFlush(TimeSpan interval)
    {
        _policies.Add(new TimeBasedFlushPolicy(interval));
        return this;
    }

    /// <summary>
    /// Adds a count-based flush policy.
    /// </summary>
    /// <param name="threshold">The dirty count threshold that triggers a flush.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public FlushCoordinatorBuilder<TKey> WithCountBasedFlush(int threshold)
    {
        _policies.Add(new CountBasedFlushPolicy(threshold));
        return this;
    }

    /// <summary>
    /// Adds a memory pressure flush policy.
    /// </summary>
    /// <param name="thresholdBytes">The memory threshold in bytes.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public FlushCoordinatorBuilder<TKey> WithMemoryPressureFlush(long thresholdBytes)
    {
        _policies.Add(new MemoryPressureFlushPolicy(thresholdBytes));
        return this;
    }

    /// <summary>
    /// Adds a dirty ratio flush policy.
    /// </summary>
    /// <param name="threshold">The dirty ratio threshold (0.0 to 1.0).</param>
    /// <returns>This builder for fluent chaining.</returns>
    public FlushCoordinatorBuilder<TKey> WithDirtyRatioFlush(double threshold)
    {
        _policies.Add(new DirtyRatioFlushPolicy(threshold));
        return this;
    }

    /// <summary>
    /// Adds an external trigger flush policy.
    /// </summary>
    /// <returns>This builder for fluent chaining.</returns>
    public FlushCoordinatorBuilder<TKey> WithExternalTriggerFlush()
    {
        _policies.Add(new ExternalTriggerFlushPolicy());
        return this;
    }

    /// <summary>
    /// Adds a shutdown flush policy.
    /// </summary>
    /// <returns>This builder for fluent chaining.</returns>
    public FlushCoordinatorBuilder<TKey> WithShutdownFlush()
    {
        _policies.Add(new ShutdownFlushPolicy());
        return this;
    }

    /// <summary>
    /// Sets the time provider for time-based operations.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public FlushCoordinatorBuilder<TKey> WithTimeProvider(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        return this;
    }

    /// <summary>
    /// Sets the logger for flush operations.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public FlushCoordinatorBuilder<TKey> WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Sets the memory estimator function for memory-based policies.
    /// </summary>
    /// <param name="estimator">Function that estimates memory usage for a key.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public FlushCoordinatorBuilder<TKey> WithMemoryEstimator(Func<TKey, long> estimator)
    {
        _memoryEstimator = estimator;
        return this;
    }

    /// <summary>
    /// Builds the flush coordinator with the configured policies.
    /// </summary>
    /// <returns>A new <see cref="IFlushCoordinator{TKey}"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no policies are configured.</exception>
    public IFlushCoordinator<TKey> Build()
    {
        if (_policies.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one flush policy must be configured.");
        }

        IFlushPolicy policy = _policies.Count == 1
            ? _policies[0]
            : new CompositeFlushPolicy("Composite", _policies);

        return new FlushCoordinatorImpl<TKey>(
            policy,
            _timeProvider,
            _logger,
            _memoryEstimator);
    }
}

/// <summary>
/// Factory entry point for creating flush coordinators.
/// </summary>
public static class FlushCoordinator
{
    /// <summary>
    /// Creates a new flush coordinator builder.
    /// </summary>
    /// <typeparam name="TKey">The type of key used to identify dirty items.</typeparam>
    /// <returns>A new builder instance.</returns>
    public static FlushCoordinatorBuilder<TKey> Create<TKey>()
        where TKey : notnull
        => new();
}
