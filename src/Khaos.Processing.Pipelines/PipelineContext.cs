using System.Collections.Concurrent;
using Khaos.Pipeline.Abstractions;

namespace Khaos.Processing.Pipelines;

/// <summary>
/// Provides per-batch shared state visible to every pipeline step.
/// </summary>
public sealed class PipelineContext : IPipelineContext
{
    private readonly ConcurrentDictionary<string, object?> _items = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets a snapshot view of the stored items.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Items => _items;

    /// <summary>
    /// Retrieves a value previously stored in the context.
    /// </summary>
    /// <typeparam name="T">The expected type of the stored value.</typeparam>
    /// <param name="key">The lookup key.</param>
    /// <returns>The stored value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the key has not been stored.</exception>
    public T Get<T>(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (!_items.TryGetValue(key, out var value))
        {
            throw new KeyNotFoundException($"PipelineContext value '{key}' was not found.");
        }

        return (T)value!;
    }

    /// <summary>
    /// Attempts to retrieve a value from the shared context.
    /// </summary>
    public bool TryGet<T>(string key, out T? value)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (_items.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Stores or replaces a value in the context.
    /// </summary>
    public void Set(string key, object? value)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        _items[key] = value;
    }

    /// <summary>
    /// Checks whether a key exists in the context.
    /// </summary>
    public bool Contains(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return _items.ContainsKey(key);
    }

    /// <summary>
    /// Removes a key from the context.
    /// </summary>
    public bool Remove(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return _items.TryRemove(key, out _);
    }

    /// <summary>
    /// Clears all items from the context.
    /// </summary>
    public void Clear()
    {
        _items.Clear();
    }
}
