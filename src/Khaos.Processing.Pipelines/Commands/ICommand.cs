// Licensed under the MIT License. See LICENSE file for details.

namespace Khaos.Processing.Pipelines.Commands;

/// <summary>
/// Marker interface for commands that can be dispatched through command queues.
/// </summary>
public interface ICommand
{
}

/// <summary>
/// Command that can be routed to a partition by key.
/// </summary>
/// <typeparam name="TKey">The type of the routing key.</typeparam>
public interface IRoutableCommand<TKey> : ICommand
{
    /// <summary>
    /// Gets the key used to determine which partition handles this command.
    /// Commands with the same routing key are guaranteed to be processed by the same partition,
    /// ensuring ordering within that key.
    /// </summary>
    TKey RoutingKey { get; }
}
