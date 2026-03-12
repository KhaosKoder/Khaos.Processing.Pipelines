using Khaos.Processing.Pipelines.Persistence;
using Xunit;

namespace Khaos.Processing.Pipelines.Tests.Persistence;

public class FlushCoordinatorTests
{
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);

        public void SetTime(DateTimeOffset time) => _now = time;
    }

    #region Dirty Tracking

    [Fact]
    public void MarkDirty_AddsKeyToDirtySet()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        coordinator.MarkDirty("key1");

        Assert.Equal(1, coordinator.DirtyCount);
        Assert.True(coordinator.IsDirty("key1"));
    }

    [Fact]
    public void MarkDirty_DoesNotDuplicate()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        coordinator.MarkDirty("key1");
        coordinator.MarkDirty("key1");

        Assert.Equal(1, coordinator.DirtyCount);
    }

    [Fact]
    public void MarkClean_RemovesKeyFromDirtySet()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        coordinator.MarkDirty("key1");
        coordinator.MarkClean("key1");

        Assert.Equal(0, coordinator.DirtyCount);
        Assert.False(coordinator.IsDirty("key1"));
    }

    [Fact]
    public void MarkClean_DoesNotThrowForNonExistentKey()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        // Should not throw
        coordinator.MarkClean("nonexistent");

        Assert.Equal(0, coordinator.DirtyCount);
    }

    [Fact]
    public void GetDirtyKeys_ReturnsAllDirtyKeys()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        coordinator.MarkDirty("key1");
        coordinator.MarkDirty("key2");
        coordinator.MarkDirty("key3");

        var dirtyKeys = coordinator.GetDirtyKeys();

        Assert.Equal(3, dirtyKeys.Count);
        Assert.Contains("key1", dirtyKeys);
        Assert.Contains("key2", dirtyKeys);
        Assert.Contains("key3", dirtyKeys);
    }

    [Fact]
    public void IsDirty_ReturnsTrueForDirtyKey()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        coordinator.MarkDirty("key1");

        Assert.True(coordinator.IsDirty("key1"));
    }

    [Fact]
    public void IsDirty_ReturnsFalseForCleanKey()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        Assert.False(coordinator.IsDirty("key1"));
    }

    #endregion

    #region CheckAndFlushAsync

    [Fact]
    public async Task CheckAndFlushAsync_NoFlush_WhenPolicyNotTriggered()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        coordinator.MarkDirty("key1");

        var result = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);

        Assert.False(result.Flushed);
        Assert.Equal(FlushResult.NoFlush, result);
        Assert.Equal(1, coordinator.DirtyCount); // Still dirty
    }

    [Fact]
    public async Task CheckAndFlushAsync_Flushes_WhenPolicyTriggered()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(2)
            .Build();

        coordinator.MarkDirty("key1");
        coordinator.MarkDirty("key2");

        var flushedKeys = new List<string>();
        var result = await coordinator.CheckAndFlushAsync(
            (keys, _) =>
            {
                flushedKeys.AddRange(keys.Cast<string>());
                return ValueTask.CompletedTask;
            });

        Assert.True(result.Flushed);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.ItemsFlushed);
        Assert.NotNull(result.TriggeringPolicy);
        Assert.Equal(0, coordinator.DirtyCount); // Now clean
        Assert.Equal(2, flushedKeys.Count);
    }

    [Fact]
    public async Task CheckAndFlushAsync_NoFlush_WhenNoDirtyItems()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(1)
            .Build();

        var result = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);

        Assert.False(result.Flushed);
    }

    [Fact]
    public async Task CheckAndFlushAsync_TimePolicy_TriggersAfterInterval()
    {
        var timeProvider = new TestTimeProvider();
        using var coordinator = FlushCoordinator.Create<string>()
            .WithTimeBasedFlush(TimeSpan.FromMinutes(5))
            .WithTimeProvider(timeProvider)
            .Build();

        coordinator.MarkDirty("key1");

        // Before interval - should not flush
        var result1 = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);
        Assert.False(result1.Flushed);

        // Advance time past interval
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        var result2 = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);
        Assert.True(result2.Flushed);
    }

    [Fact]
    public async Task CheckAndFlushAsync_HandlesFlushActionError()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(1)
            .Build();

        coordinator.MarkDirty("key1");

        var expectedException = new InvalidOperationException("Flush failed");
        var result = await coordinator.CheckAndFlushAsync(
            (keys, _) => throw expectedException);

        Assert.False(result.Flushed);
        Assert.False(result.IsSuccess);
        Assert.Same(expectedException, result.Error);
        Assert.Equal(1, coordinator.DirtyCount); // Still dirty after failure
    }

    [Fact]
    public async Task CheckAndFlushAsync_ThrowsForNullFlushAction()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.CheckAndFlushAsync(null!).AsTask());
    }

    #endregion

    #region ForceFlushAsync

    [Fact]
    public async Task ForceFlushAsync_FlushesRegardlessOfPolicy()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(1000) // High threshold
            .Build();

        coordinator.MarkDirty("key1");

        var result = await coordinator.ForceFlushAsync(
            (keys, _) => ValueTask.CompletedTask);

        Assert.True(result.Flushed);
        Assert.Equal(1, result.ItemsFlushed);
        Assert.Equal(0, coordinator.DirtyCount);
    }

    [Fact]
    public async Task ForceFlushAsync_NoFlush_WhenNoDirtyItems()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        var result = await coordinator.ForceFlushAsync(
            (keys, _) => ValueTask.CompletedTask);

        Assert.False(result.Flushed);
    }

    #endregion

    #region External Trigger

    [Fact]
    public async Task SetExternalTrigger_TriggersFlush()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithExternalTriggerFlush()
            .Build();

        coordinator.MarkDirty("key1");

        // Without trigger - no flush
        var result1 = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);
        Assert.False(result1.Flushed);

        // Set trigger
        coordinator.SetExternalTrigger();

        var result2 = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);
        Assert.True(result2.Flushed);
    }

    [Fact]
    public async Task ExternalTrigger_ClearsAfterCheck()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithExternalTriggerFlush()
            .Build();

        coordinator.MarkDirty("key1");
        coordinator.SetExternalTrigger();

        // First check flushes and clears trigger
        await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);

        // Mark dirty again
        coordinator.MarkDirty("key2");

        // Second check should not flush (trigger was cleared)
        var result = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);
        Assert.False(result.Flushed);
    }

    [Fact]
    public void ClearExternalTrigger_ClearsTrigger()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithExternalTriggerFlush()
            .Build();

        coordinator.SetExternalTrigger();
        var stats1 = coordinator.GetStatistics();
        Assert.True(stats1.ExternalTriggerSet);

        coordinator.ClearExternalTrigger();
        var stats2 = coordinator.GetStatistics();
        Assert.False(stats2.ExternalTriggerSet);
    }

    #endregion

    #region Shutdown

    [Fact]
    public async Task SetShutdown_TriggersFlush()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithShutdownFlush()
            .Build();

        coordinator.MarkDirty("key1");

        // Without shutdown - no flush
        var result1 = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);
        Assert.False(result1.Flushed);

        // Set shutdown
        coordinator.SetShutdown();

        var result2 = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);
        Assert.True(result2.Flushed);
    }

    [Fact]
    public void SetShutdown_UpdatesStatistics()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        var stats1 = coordinator.GetStatistics();
        Assert.False(stats1.IsShutdown);

        coordinator.SetShutdown();

        var stats2 = coordinator.GetStatistics();
        Assert.True(stats2.IsShutdown);
    }

    #endregion

    #region Statistics

    [Fact]
    public void GetStatistics_ReturnsCorrectDirtyCount()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        coordinator.MarkDirty("key1");
        coordinator.MarkDirty("key2");

        var stats = coordinator.GetStatistics();
        Assert.Equal(2, stats.DirtyCount);
    }

    [Fact]
    public async Task GetStatistics_TracksFlushCounts()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(1)
            .Build();

        coordinator.MarkDirty("key1");
        await coordinator.CheckAndFlushAsync((_, _) => ValueTask.CompletedTask);

        coordinator.MarkDirty("key2");
        await coordinator.CheckAndFlushAsync((_, _) => ValueTask.CompletedTask);

        var stats = coordinator.GetStatistics();
        Assert.Equal(2, stats.TotalFlushes);
        Assert.Equal(2, stats.TotalItemsFlushed);
    }

    [Fact]
    public void GetStatistics_TracksTimeSinceLastFlush()
    {
        var timeProvider = new TestTimeProvider();
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .WithTimeProvider(timeProvider)
            .Build();

        timeProvider.Advance(TimeSpan.FromMinutes(5));

        var stats = coordinator.GetStatistics();
        Assert.True(stats.TimeSinceLastFlush >= TimeSpan.FromMinutes(5));
    }

    #endregion

    #region Builder

    [Fact]
    public void Builder_ThrowsWhenNoPoliciesConfigured()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FlushCoordinator.Create<string>().Build());
    }

    [Fact]
    public void Builder_CanAddMultiplePolicies()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .WithTimeBasedFlush(TimeSpan.FromMinutes(5))
            .WithShutdownFlush()
            .Build();

        Assert.NotNull(coordinator);
    }

    [Fact]
    public void Builder_CanAddCustomPolicy()
    {
        var customPolicy = new CountBasedFlushPolicy(50);

        using var coordinator = FlushCoordinator.Create<string>()
            .AddPolicy(customPolicy)
            .Build();

        Assert.NotNull(coordinator);
    }

    [Fact]
    public async Task Builder_MemoryEstimator_IsUsedForMemoryPolicy()
    {
        using var coordinator = FlushCoordinator.Create<string>()
            .WithMemoryPressureFlush(1000) // 1000 bytes threshold
            .WithMemoryEstimator(key => key.Length * 100) // Each char = 100 bytes
            .Build();

        // Each "key" has 3 chars = 300 bytes, need 4 keys to exceed 1000
        coordinator.MarkDirty("key1"); // 300 bytes
        coordinator.MarkDirty("key2"); // 600 bytes total

        var result1 = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);
        Assert.False(result1.Flushed);

        coordinator.MarkDirty("key3"); // 900 bytes
        coordinator.MarkDirty("key4"); // 1200 bytes - exceeds threshold

        var result2 = await coordinator.CheckAndFlushAsync(
            (keys, _) => ValueTask.CompletedTask);
        Assert.True(result2.Flushed);
    }

    #endregion

    #region Disposal

    [Fact]
    public void Dispose_ClearsDirtySet()
    {
        var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        coordinator.MarkDirty("key1");
        coordinator.Dispose();

        Assert.Equal(0, coordinator.DirtyCount);
    }

    [Fact]
    public void Dispose_ThrowsOnSubsequentOperations()
    {
        var coordinator = FlushCoordinator.Create<string>()
            .WithCountBasedFlush(100)
            .Build();

        coordinator.Dispose();

        Assert.Throws<ObjectDisposedException>(() => coordinator.MarkDirty("key1"));
        Assert.Throws<ObjectDisposedException>(() => coordinator.MarkClean("key1"));
        Assert.Throws<ObjectDisposedException>(() => coordinator.IsDirty("key1"));
        Assert.Throws<ObjectDisposedException>(() => coordinator.GetDirtyKeys());
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task ConcurrentMarkDirty_IsThreadSafe()
    {
        using var coordinator = FlushCoordinator.Create<int>()
            .WithCountBasedFlush(10_000)
            .Build();

        var tasks = Enumerable.Range(0, 1000)
            .Select(i => Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    coordinator.MarkDirty(i * 100 + j);
                }
            }));

        await Task.WhenAll(tasks);

        // Should have exactly 100,000 unique dirty keys
        Assert.Equal(100_000, coordinator.DirtyCount);
    }

    [Fact]
    public async Task ConcurrentMarkDirtyAndClean_IsThreadSafe()
    {
        using var coordinator = FlushCoordinator.Create<int>()
            .WithCountBasedFlush(10_000)
            .Build();

        // Mark keys dirty
        for (int i = 0; i < 1000; i++)
        {
            coordinator.MarkDirty(i);
        }

        // Concurrently mark some dirty and some clean
        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    if (j % 2 == 0)
                        coordinator.MarkDirty(i * 10 + j);
                    else
                        coordinator.MarkClean(i * 10 + j);
                }
            }));

        await Task.WhenAll(tasks);

        // Should not throw, exact count depends on timing
        var count = coordinator.DirtyCount;
        Assert.True(count >= 0);
    }

    #endregion
}
