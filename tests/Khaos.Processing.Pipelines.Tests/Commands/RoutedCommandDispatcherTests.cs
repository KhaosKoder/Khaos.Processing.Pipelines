using Khaos.Processing.Pipelines.Commands;
using Xunit;

namespace Khaos.Processing.Pipelines.Tests.Commands;

public class RoutedCommandDispatcherTests
{
    private record TestCommand(string Key, string Data) : IRoutableCommand<string>
    {
        public string RoutingKey => Key;
    }

    private record IntKeyCommand(int Key, string Data) : IRoutableCommand<int>
    {
        public int RoutingKey => Key;
    }

    [Fact]
    public void Constructor_ValidatesOptions()
    {
        var invalidOptions = new RoutedDispatcherOptions { PartitionCount = 3 }; // Not power of 2

        Assert.Throws<ArgumentException>(() =>
            new RoutedCommandDispatcher<string, TestCommand>(invalidOptions));
    }

    [Fact]
    public void Constructor_ThrowsForNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RoutedCommandDispatcher<string, TestCommand>(null!));
    }

    [Fact]
    public void PartitionCount_ReturnsConfiguredValue()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 16 };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        Assert.Equal(16, dispatcher.PartitionCount);
    }

    [Fact]
    public void GetPartitionIndex_ReturnsConsistentResults()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 8 };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        var key = "test-key";
        var partition1 = dispatcher.GetPartitionIndex(key);
        var partition2 = dispatcher.GetPartitionIndex(key);

        Assert.Equal(partition1, partition2);
        Assert.InRange(partition1, 0, 7);
    }

    [Fact]
    public void GetPartitionIndex_DifferentKeysCanHaveDifferentPartitions()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 32 };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        // With 32 partitions and many different keys, we should see variation
        var partitions = Enumerable.Range(0, 100)
            .Select(i => dispatcher.GetPartitionIndex($"key-{i}"))
            .Distinct()
            .Count();

        Assert.True(partitions > 1, "Different keys should hash to different partitions");
    }

    [Fact]
    public void GetPartitionQueue_ReturnsValidQueue()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 4 };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        var queue = dispatcher.GetPartitionQueue(2);

        Assert.NotNull(queue);
        Assert.Equal(options.QueueCapacityPerPartition, queue.Capacity);
    }

    [Fact]
    public void GetPartitionQueue_ThrowsForInvalidIndex()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 4 };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        Assert.Throws<ArgumentOutOfRangeException>(() => dispatcher.GetPartitionQueue(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => dispatcher.GetPartitionQueue(4));
    }

    [Fact]
    public async Task DispatchAsync_EnqueuesCommand()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 4, QueueCapacityPerPartition = 100 };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        var command = new TestCommand("mykey", "mydata");
        var result = await dispatcher.DispatchAsync(command);

        Assert.Equal(DispatchResult.Success, result);

        // Command should be in the correct partition
        var partitionIndex = dispatcher.GetPartitionIndex("mykey");
        var queue = dispatcher.GetPartitionQueue(partitionIndex);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task DispatchAsync_ThrowsForNullCommand()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 4 };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dispatcher.DispatchAsync(null!).AsTask());
    }

    [Fact]
    public async Task DispatchAsync_RoutesCommandToCorrectPartition()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 8, QueueCapacityPerPartition = 100 };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        var cmd1 = new TestCommand("key-a", "data-a");
        var cmd2 = new TestCommand("key-b", "data-b");

        await dispatcher.DispatchAsync(cmd1);
        await dispatcher.DispatchAsync(cmd2);

        var partition1 = dispatcher.GetPartitionIndex("key-a");
        var partition2 = dispatcher.GetPartitionIndex("key-b");

        var queue1 = dispatcher.GetPartitionQueue(partition1);
        var queue2 = dispatcher.GetPartitionQueue(partition2);

        // If same partition, both commands should be there
        if (partition1 == partition2)
        {
            Assert.Equal(2, queue1.Count);
        }
        else
        {
            // Otherwise, one command in each queue
            Assert.Equal(1, queue1.Count);
            Assert.Equal(1, queue2.Count);
        }
    }

    [Fact]
    public async Task DispatchAsync_SameKeyAlwaysGoesToSamePartition()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 8, QueueCapacityPerPartition = 100 };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        var key = "consistent-key";

        // Dispatch multiple commands with the same key
        for (int i = 0; i < 5; i++)
        {
            await dispatcher.DispatchAsync(new TestCommand(key, $"data-{i}"));
        }

        // All commands should be in the same partition
        var expectedPartition = dispatcher.GetPartitionIndex(key);
        var queue = dispatcher.GetPartitionQueue(expectedPartition);
        Assert.Equal(5, queue.Count);

        // Verify order is preserved
        queue.TryDequeue(out var first);
        Assert.Equal("data-0", first!.Data);
    }

    [Fact]
    public async Task DispatchAsync_WithRejectBehavior_RejectsWhenFull()
    {
        var options = new RoutedDispatcherOptions
        {
            PartitionCount = 1, // Single partition for easy testing
            QueueCapacityPerPartition = 2,
            BackpressureBehavior = BackpressureBehavior.Reject
        };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        // Fill the queue
        await dispatcher.DispatchAsync(new TestCommand("k", "1"));
        await dispatcher.DispatchAsync(new TestCommand("k", "2"));

        // Third should be rejected
        var result = await dispatcher.DispatchAsync(new TestCommand("k", "3"));

        Assert.Equal(DispatchResult.Rejected, result);
    }

    [Fact]
    public async Task DispatchAsync_WithWaitBehavior_WaitsForSpace()
    {
        var options = new RoutedDispatcherOptions
        {
            PartitionCount = 1,
            QueueCapacityPerPartition = 1,
            BackpressureBehavior = BackpressureBehavior.Wait,
            BackpressureTimeout = TimeSpan.FromSeconds(5)
        };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        // Fill the queue
        await dispatcher.DispatchAsync(new TestCommand("k", "1"));

        // Start an async dispatch that will wait
        var dispatchTask = dispatcher.DispatchAsync(new TestCommand("k", "2")).AsTask();

        // Give it a moment to start waiting
        await Task.Delay(50);
        Assert.False(dispatchTask.IsCompleted);

        // Dequeue to make space
        var queue = dispatcher.GetPartitionQueue(0);
        queue.TryDequeue(out _);

        // Now the dispatch should complete
        var result = await dispatchTask;
        Assert.Equal(DispatchResult.Success, result);
    }

    [Fact]
    public async Task DispatchAsync_WithWaitBehavior_TimesOut()
    {
        var options = new RoutedDispatcherOptions
        {
            PartitionCount = 1,
            QueueCapacityPerPartition = 1,
            BackpressureBehavior = BackpressureBehavior.Wait,
            BackpressureTimeout = TimeSpan.FromMilliseconds(50)
        };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        // Fill the queue
        await dispatcher.DispatchAsync(new TestCommand("k", "1"));

        // This should timeout
        var result = await dispatcher.DispatchAsync(new TestCommand("k", "2"));

        Assert.Equal(DispatchResult.Timeout, result);
    }

    [Fact]
    public async Task DispatchAsync_CancellationReturnsCorrectResult()
    {
        var options = new RoutedDispatcherOptions
        {
            PartitionCount = 1,
            QueueCapacityPerPartition = 1,
            BackpressureBehavior = BackpressureBehavior.Wait,
            BackpressureTimeout = TimeSpan.FromSeconds(30)
        };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);
        using var cts = new CancellationTokenSource();

        // Fill the queue
        await dispatcher.DispatchAsync(new TestCommand("k", "1"));

        // Cancel immediately
        cts.Cancel();

        var result = await dispatcher.DispatchAsync(new TestCommand("k", "2"), cts.Token);

        Assert.Equal(DispatchResult.Cancelled, result);
    }

    [Fact]
    public void GetPartitionFillPercentages_ReturnsCorrectValues()
    {
        var options = new RoutedDispatcherOptions
        {
            PartitionCount = 4,
            QueueCapacityPerPartition = 100
        };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        var percentages = dispatcher.GetPartitionFillPercentages();

        Assert.Equal(4, percentages.Count);
        Assert.All(percentages, p => Assert.Equal(0, p));
    }

    [Fact]
    public async Task GetPartitionFillPercentages_UpdatesAfterDispatch()
    {
        var options = new RoutedDispatcherOptions
        {
            PartitionCount = 1,
            QueueCapacityPerPartition = 10
        };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        // Add 5 commands to single partition
        for (int i = 0; i < 5; i++)
        {
            await dispatcher.DispatchAsync(new TestCommand($"k{i}", $"d{i}"));
        }

        var percentages = dispatcher.GetPartitionFillPercentages();
        Assert.Equal(50, percentages[0]);
    }

    [Fact]
    public void GetStatistics_ReturnsCorrectValues()
    {
        var options = new RoutedDispatcherOptions
        {
            PartitionCount = 4,
            QueueCapacityPerPartition = 100
        };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        var stats = dispatcher.GetStatistics();

        Assert.Equal(0, stats.TotalQueuedCommands);
        Assert.Equal(400, stats.TotalCapacity);
        Assert.Equal(0, stats.OverallFillPercentage);
    }

    [Fact]
    public async Task GetStatistics_UpdatesAfterDispatch()
    {
        var options = new RoutedDispatcherOptions
        {
            PartitionCount = 2,
            QueueCapacityPerPartition = 100
        };
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        // Add 50 commands
        for (int i = 0; i < 50; i++)
        {
            await dispatcher.DispatchAsync(new TestCommand($"k{i}", $"d{i}"));
        }

        var stats = dispatcher.GetStatistics();

        Assert.Equal(50, stats.TotalQueuedCommands);
        Assert.Equal(200, stats.TotalCapacity);
        Assert.Equal(25, stats.OverallFillPercentage);
    }

    [Fact]
    public async Task Dispose_CompletesAllQueues()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 4, QueueCapacityPerPartition = 10 };
        var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(options);

        await dispatcher.DispatchAsync(new TestCommand("k", "d"));

        dispatcher.Dispose();

        // After disposal, dispatch should throw
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            dispatcher.DispatchAsync(new TestCommand("k2", "d2")).AsTask());
    }

    [Fact]
    public void CustomHashFunction_IsUsed()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 8 };
        
        // Custom hash that always returns partition 5
        Func<string, int> customHash = _ => 5;
        
        using var dispatcher = new RoutedCommandDispatcher<string, TestCommand>(
            options, hashFunction: customHash);

        // All keys should go to partition 5
        Assert.Equal(5, dispatcher.GetPartitionIndex("any-key"));
        Assert.Equal(5, dispatcher.GetPartitionIndex("another-key"));
    }

    [Fact]
    public async Task ConcurrentDispatch_AllCommandsRouted()
    {
        var options = new RoutedDispatcherOptions
        {
            PartitionCount = 8,
            QueueCapacityPerPartition = 10_000
        };
        using var dispatcher = new RoutedCommandDispatcher<int, IntKeyCommand>(options);

        const int totalCommands = 1000;

        var tasks = Enumerable.Range(0, totalCommands)
            .Select(i => dispatcher.DispatchAsync(new IntKeyCommand(i, $"data-{i}")).AsTask());

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(DispatchResult.Success, r));

        var stats = dispatcher.GetStatistics();
        Assert.Equal(totalCommands, stats.TotalQueuedCommands);
    }

    [Fact]
    public void PartitionDistribution_IsReasonablyBalanced()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 16 };
        using var dispatcher = new RoutedCommandDispatcher<int, IntKeyCommand>(options);

        const int totalCommands = 10_000;
        var partitionCounts = new int[16];

        for (int i = 0; i < totalCommands; i++)
        {
            var partition = dispatcher.GetPartitionIndex(i);
            partitionCounts[partition]++;
        }

        // Each partition should have roughly totalCommands / partitionCount
        var expected = totalCommands / 16.0;
        var variance = partitionCounts.Select(c => Math.Pow(c - expected, 2)).Average();
        var stdDev = Math.Sqrt(variance);

        // Standard deviation should be reasonable (not more than 50% of expected)
        Assert.True(stdDev < expected * 0.5, $"Standard deviation {stdDev} is too high for expected {expected}");
    }
}
