// Licensed under the MIT License. See LICENSE file for details.

namespace Khaos.Processing.Pipelines.Persistence;

/// <summary>
/// Triggers flush when elapsed time since last flush exceeds threshold.
/// </summary>
public sealed class TimeBasedFlushPolicy : IFlushPolicy
{
    private readonly TimeSpan _interval;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeBasedFlushPolicy"/> class.
    /// </summary>
    /// <param name="interval">The time interval after which flush should be triggered.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when interval is negative.</exception>
    public TimeBasedFlushPolicy(TimeSpan interval)
    {
        if (interval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval cannot be negative.");
        _interval = interval;
    }

    /// <inheritdoc/>
    public string Name => "TimeBased";

    /// <inheritdoc/>
    public bool ShouldFlush(in FlushContext context)
        => context.DirtyCount > 0 && context.ElapsedSinceLastFlush >= _interval;
}

/// <summary>
/// Triggers flush when dirty count exceeds threshold.
/// </summary>
public sealed class CountBasedFlushPolicy : IFlushPolicy
{
    private readonly int _threshold;

    /// <summary>
    /// Initializes a new instance of the <see cref="CountBasedFlushPolicy"/> class.
    /// </summary>
    /// <param name="threshold">The dirty count threshold that triggers a flush.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when threshold is less than 1.</exception>
    public CountBasedFlushPolicy(int threshold)
    {
        if (threshold < 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be at least 1.");
        _threshold = threshold;
    }

    /// <inheritdoc/>
    public string Name => "CountBased";

    /// <inheritdoc/>
    public bool ShouldFlush(in FlushContext context)
        => context.DirtyCount >= _threshold;
}

/// <summary>
/// Triggers flush when estimated dirty memory exceeds threshold.
/// </summary>
public sealed class MemoryPressureFlushPolicy : IFlushPolicy
{
    private readonly long _thresholdBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryPressureFlushPolicy"/> class.
    /// </summary>
    /// <param name="thresholdBytes">The memory threshold in bytes that triggers a flush.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when threshold is less than 1.</exception>
    public MemoryPressureFlushPolicy(long thresholdBytes)
    {
        if (thresholdBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(thresholdBytes), "Threshold must be at least 1 byte.");
        _thresholdBytes = thresholdBytes;
    }

    /// <inheritdoc/>
    public string Name => "MemoryPressure";

    /// <inheritdoc/>
    public bool ShouldFlush(in FlushContext context)
        => context.EstimatedDirtyMemoryBytes >= _thresholdBytes;
}

/// <summary>
/// Triggers flush when dirty count exceeds a percentage of total count.
/// </summary>
public sealed class DirtyRatioFlushPolicy : IFlushPolicy
{
    private readonly double _threshold;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirtyRatioFlushPolicy"/> class.
    /// </summary>
    /// <param name="threshold">The dirty ratio threshold (0.0 to 1.0) that triggers a flush.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when threshold is outside [0, 1] range.</exception>
    public DirtyRatioFlushPolicy(double threshold)
    {
        if (threshold < 0.0 || threshold > 1.0)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be between 0.0 and 1.0.");
        _threshold = threshold;
    }

    /// <inheritdoc/>
    public string Name => "DirtyRatio";

    /// <inheritdoc/>
    public bool ShouldFlush(in FlushContext context)
        => context.TotalCount > 0 &&
           (double)context.DirtyCount / context.TotalCount >= _threshold;
}

/// <summary>
/// Triggers flush when external trigger is set (e.g., offset commit pending).
/// </summary>
public sealed class ExternalTriggerFlushPolicy : IFlushPolicy
{
    /// <inheritdoc/>
    public string Name => "ExternalTrigger";

    /// <inheritdoc/>
    public bool ShouldFlush(in FlushContext context)
        => context.ExternalTrigger && context.DirtyCount > 0;
}

/// <summary>
/// Always triggers flush during shutdown if any dirty items exist.
/// </summary>
public sealed class ShutdownFlushPolicy : IFlushPolicy
{
    /// <inheritdoc/>
    public string Name => "Shutdown";

    /// <inheritdoc/>
    public bool ShouldFlush(in FlushContext context)
        => context.IsShutdown && context.DirtyCount > 0;
}

/// <summary>
/// Combines multiple policies; triggers if ANY policy triggers.
/// </summary>
public sealed class CompositeFlushPolicy : IFlushPolicy
{
    private readonly IFlushPolicy[] _policies;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeFlushPolicy"/> class.
    /// </summary>
    /// <param name="name">The name for this composite policy.</param>
    /// <param name="policies">The policies to combine.</param>
    /// <exception cref="ArgumentNullException">Thrown when name or policies is null.</exception>
    /// <exception cref="ArgumentException">Thrown when policies is empty.</exception>
    public CompositeFlushPolicy(string name, params IFlushPolicy[] policies)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(policies);
        if (policies.Length == 0)
            throw new ArgumentException("At least one policy is required.", nameof(policies));

        Name = name;
        _policies = policies;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeFlushPolicy"/> class.
    /// </summary>
    /// <param name="name">The name for this composite policy.</param>
    /// <param name="policies">The policies to combine.</param>
    public CompositeFlushPolicy(string name, IEnumerable<IFlushPolicy> policies)
        : this(name, policies?.ToArray() ?? throw new ArgumentNullException(nameof(policies)))
    {
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public bool ShouldFlush(in FlushContext context)
    {
        foreach (var policy in _policies)
        {
            if (policy.ShouldFlush(context))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the names of policies that would trigger a flush (for diagnostics).
    /// </summary>
    /// <param name="context">The flush context to evaluate.</param>
    /// <returns>Names of policies that triggered.</returns>
    public IEnumerable<string> GetTriggeringPolicies(in FlushContext context)
    {
        var triggering = new List<string>();
        foreach (var policy in _policies)
        {
            if (policy.ShouldFlush(context))
                triggering.Add(policy.Name);
        }
        return triggering;
    }

    /// <summary>
    /// Gets the policies in this composite.
    /// </summary>
    public IReadOnlyList<IFlushPolicy> Policies => _policies;
}
