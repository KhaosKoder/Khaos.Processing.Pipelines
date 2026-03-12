using Khaos.Processing.Pipelines.Commands;
using Xunit;

namespace Khaos.Processing.Pipelines.Tests.Commands;

public class RoutedDispatcherBuilderTests
{
    private record TestCommand(string Key) : IRoutableCommand<string>
    {
        public string RoutingKey => Key;
    }

    [Fact]
    public void Build_CreatesDispatcherWithDefaults()
    {
        using var dispatcher = CommandDispatcher.Create<string, TestCommand>().Build();

        Assert.NotNull(dispatcher);
        Assert.Equal(32, dispatcher.PartitionCount); // Default partition count
    }

    [Fact]
    public void WithPartitionCount_SetsPartitionCount()
    {
        using var dispatcher = CommandDispatcher.Create<string, TestCommand>()
            .WithPartitionCount(64)
            .Build();

        Assert.Equal(64, dispatcher.PartitionCount);
    }

    [Fact]
    public void WithPartitionCount_ThrowsForNonPowerOfTwo()
    {
        Assert.Throws<ArgumentException>(() =>
            CommandDispatcher.Create<string, TestCommand>()
                .WithPartitionCount(10));
    }

    [Fact]
    public void WithPartitionCount_ThrowsForZero()
    {
        Assert.Throws<ArgumentException>(() =>
            CommandDispatcher.Create<string, TestCommand>()
                .WithPartitionCount(0));
    }

    [Fact]
    public void WithPartitionCount_ThrowsForNegative()
    {
        Assert.Throws<ArgumentException>(() =>
            CommandDispatcher.Create<string, TestCommand>()
                .WithPartitionCount(-4));
    }

    [Fact]
    public void WithQueueCapacity_SetsCapacity()
    {
        using var dispatcher = CommandDispatcher.Create<string, TestCommand>()
            .WithPartitionCount(1)
            .WithQueueCapacity(500)
            .Build();

        var queue = dispatcher.GetPartitionQueue(0);
        Assert.Equal(500, queue.Capacity);
    }

    [Fact]
    public void WithQueueCapacity_ThrowsForZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CommandDispatcher.Create<string, TestCommand>()
                .WithQueueCapacity(0));
    }

    [Fact]
    public void WithQueueCapacity_ThrowsForNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CommandDispatcher.Create<string, TestCommand>()
                .WithQueueCapacity(-100));
    }

    [Fact]
    public void WithBackpressure_SetsBehavior()
    {
        // We can't directly test this, but we can verify it doesn't throw
        using var dispatcher = CommandDispatcher.Create<string, TestCommand>()
            .WithBackpressure(BackpressureBehavior.Reject)
            .Build();

        Assert.NotNull(dispatcher);
    }

    [Fact]
    public void WithBackpressure_SetsTimeout()
    {
        using var dispatcher = CommandDispatcher.Create<string, TestCommand>()
            .WithBackpressure(BackpressureBehavior.Wait, TimeSpan.FromMinutes(1))
            .Build();

        Assert.NotNull(dispatcher);
    }

    [Fact]
    public void WithBackpressure_ThrowsForNegativeTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CommandDispatcher.Create<string, TestCommand>()
                .WithBackpressure(BackpressureBehavior.Wait, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void WithHashFunction_ThrowsForNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CommandDispatcher.Create<string, TestCommand>()
                .WithHashFunction(null!));
    }

    [Fact]
    public void WithHashFunction_UsesCustomHash()
    {
        // Custom hash that always returns 7
        using var dispatcher = CommandDispatcher.Create<string, TestCommand>()
            .WithPartitionCount(8)
            .WithHashFunction(_ => 7)
            .Build();

        Assert.Equal(7, dispatcher.GetPartitionIndex("any-key"));
        Assert.Equal(7, dispatcher.GetPartitionIndex("different-key"));
    }

    [Fact]
    public void WithLogger_AcceptsNull()
    {
        // Null logger should be accepted
        using var dispatcher = CommandDispatcher.Create<string, TestCommand>()
            .WithLogger(null!)
            .Build();

        Assert.NotNull(dispatcher);
    }

    [Fact]
    public void FluentChaining_WorksCorrectly()
    {
        using var dispatcher = CommandDispatcher.Create<string, TestCommand>()
            .WithPartitionCount(16)
            .WithQueueCapacity(5_000)
            .WithBackpressure(BackpressureBehavior.Wait, TimeSpan.FromSeconds(10))
            .WithHashFunction(key => key?.GetHashCode() ?? 0)
            .Build();

        Assert.Equal(16, dispatcher.PartitionCount);
        Assert.Equal(5_000, dispatcher.GetPartitionQueue(0).Capacity);
    }

    [Fact]
    public void CreateDefault_BuildsWithDefaults()
    {
        using var dispatcher = CommandDispatcher.CreateDefault<string, TestCommand>();

        Assert.Equal(32, dispatcher.PartitionCount);
        Assert.Equal(10_000, dispatcher.GetPartitionQueue(0).Capacity);
    }

    [Fact]
    public async Task BuiltDispatcher_FunctionsCorrectly()
    {
        using var dispatcher = CommandDispatcher.Create<string, TestCommand>()
            .WithPartitionCount(8)
            .WithQueueCapacity(100)
            .Build();

        var result = await dispatcher.DispatchAsync(new TestCommand("test-key"));

        Assert.Equal(DispatchResult.Success, result);
    }
}
