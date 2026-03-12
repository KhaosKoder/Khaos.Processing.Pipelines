// Licensed under the MIT License. See LICENSE file for details.

namespace Khaos.Processing.Pipelines.Persistence;

/// <summary>
/// Determines whether a flush should occur based on current context.
/// Policies must be stateless and thread-safe.
/// </summary>
public interface IFlushPolicy
{
    /// <summary>
    /// Gets the unique name for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Evaluates whether a flush should occur.
    /// </summary>
    /// <param name="context">Current flush context.</param>
    /// <returns><c>true</c> if flush should be triggered; otherwise <c>false</c>.</returns>
    bool ShouldFlush(in FlushContext context);
}
