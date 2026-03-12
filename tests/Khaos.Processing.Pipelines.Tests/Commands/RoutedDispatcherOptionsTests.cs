using Khaos.Processing.Pipelines.Commands;
using Xunit;

namespace Khaos.Processing.Pipelines.Tests.Commands;

public class RoutedDispatcherOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new RoutedDispatcherOptions();

        Assert.Equal(32, options.PartitionCount);
        Assert.Equal(10_000, options.QueueCapacityPerPartition);
        Assert.Equal(BackpressureBehavior.Wait, options.BackpressureBehavior);
        Assert.Equal(TimeSpan.FromSeconds(30), options.BackpressureTimeout);
    }

    [Fact]
    public void Validate_ThrowsForZeroPartitionCount()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 0 };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ThrowsForNegativePartitionCount()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = -1 };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ThrowsForNonPowerOfTwo()
    {
        var options = new RoutedDispatcherOptions { PartitionCount = 5 };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Validate_AcceptsPowerOfTwo(int partitionCount)
    {
        var options = new RoutedDispatcherOptions { PartitionCount = partitionCount };

        // Should not throw
        options.Validate();
    }

    [Fact]
    public void Validate_ThrowsForZeroQueueCapacity()
    {
        var options = new RoutedDispatcherOptions { QueueCapacityPerPartition = 0 };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ThrowsForNegativeQueueCapacity()
    {
        var options = new RoutedDispatcherOptions { QueueCapacityPerPartition = -100 };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ThrowsForNegativeBackpressureTimeout()
    {
        var options = new RoutedDispatcherOptions 
        { 
            BackpressureTimeout = TimeSpan.FromSeconds(-1) 
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_AcceptsZeroTimeout()
    {
        var options = new RoutedDispatcherOptions 
        { 
            BackpressureTimeout = TimeSpan.Zero 
        };

        // Should not throw
        options.Validate();
    }

    [Fact]
    public void AllBackpressureBehaviors_AreValid()
    {
        foreach (BackpressureBehavior behavior in Enum.GetValues<BackpressureBehavior>())
        {
            var options = new RoutedDispatcherOptions { BackpressureBehavior = behavior };

            // Should not throw
            options.Validate();
        }
    }
}
