using Khaos.Processing.Pipelines.Commands;
using Xunit;

namespace Khaos.Processing.Pipelines.Tests.Commands;

public class BoundedCommandQueueTests
{
    private record TestCommand(string Value) : ICommand;

    [Fact]
    public void Constructor_ThrowsForInvalidCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedCommandQueue<TestCommand>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedCommandQueue<TestCommand>(-1));
    }

    [Fact]
    public void Constructor_SetsCapacity()
    {
        var queue = new BoundedCommandQueue<TestCommand>(100);
        Assert.Equal(100, queue.Capacity);
    }

    [Fact]
    public void TryEnqueue_ReturnsTrueWhenNotFull()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);
        var result = queue.TryEnqueue(new TestCommand("test"));
        Assert.True(result);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TryEnqueue_UpdatesCount()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);

        queue.TryEnqueue(new TestCommand("1"));
        queue.TryEnqueue(new TestCommand("2"));
        queue.TryEnqueue(new TestCommand("3"));

        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public void TryDequeue_ReturnsTrueWhenNotEmpty()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);
        queue.TryEnqueue(new TestCommand("test"));

        var result = queue.TryDequeue(out var command);

        Assert.True(result);
        Assert.NotNull(command);
        Assert.Equal("test", command.Value);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void TryDequeue_ReturnsFalseWhenEmpty()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);

        var result = queue.TryDequeue(out var command);

        Assert.False(result);
        Assert.Null(command);
    }

    [Fact]
    public void TryDequeue_PreservesOrder()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);
        queue.TryEnqueue(new TestCommand("1"));
        queue.TryEnqueue(new TestCommand("2"));
        queue.TryEnqueue(new TestCommand("3"));

        queue.TryDequeue(out var cmd1);
        queue.TryDequeue(out var cmd2);
        queue.TryDequeue(out var cmd3);

        Assert.Equal("1", cmd1!.Value);
        Assert.Equal("2", cmd2!.Value);
        Assert.Equal("3", cmd3!.Value);
    }

    [Fact]
    public void FillPercentage_CalculatesCorrectly()
    {
        var queue = new BoundedCommandQueue<TestCommand>(100);

        Assert.Equal(0, queue.FillPercentage);

        for (int i = 0; i < 50; i++)
            queue.TryEnqueue(new TestCommand(i.ToString()));

        Assert.Equal(50, queue.FillPercentage);

        for (int i = 50; i < 100; i++)
            queue.TryEnqueue(new TestCommand(i.ToString()));

        Assert.Equal(100, queue.FillPercentage);
    }

    [Fact]
    public async Task EnqueueAsync_EnqueuesSuccessfully()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);

        var result = await queue.EnqueueAsync(new TestCommand("async"));

        Assert.True(result);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task EnqueueAsync_ThrowsWhenCancelled()
    {
        var queue = new BoundedCommandQueue<TestCommand>(1);
        queue.TryEnqueue(new TestCommand("fill")); // Fill the queue

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Since queue is full and we're cancelled, should throw
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            queue.EnqueueAsync(new TestCommand("blocked"), cts.Token).AsTask());
    }

    [Fact]
    public async Task DequeueAsync_DequeuesSuccessfully()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);
        queue.TryEnqueue(new TestCommand("async"));

        var command = await queue.DequeueAsync();

        Assert.Equal("async", command.Value);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task DequeueAsync_ThrowsWhenCancelledWhileEmpty()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // TaskCanceledException inherits from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => queue.DequeueAsync(cts.Token).AsTask());
    }

    [Fact]
    public void DrainTo_ReturnsAllAvailableCommands()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);
        queue.TryEnqueue(new TestCommand("1"));
        queue.TryEnqueue(new TestCommand("2"));
        queue.TryEnqueue(new TestCommand("3"));

        var buffer = new TestCommand[10];
        var count = queue.DrainTo(buffer);

        Assert.Equal(3, count);
        Assert.Equal("1", buffer[0].Value);
        Assert.Equal("2", buffer[1].Value);
        Assert.Equal("3", buffer[2].Value);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void DrainTo_LimitsToBufferSize()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);
        for (int i = 0; i < 5; i++)
            queue.TryEnqueue(new TestCommand(i.ToString()));

        var buffer = new TestCommand[2];
        var count = queue.DrainTo(buffer);

        Assert.Equal(2, count);
        Assert.Equal(3, queue.Count); // Remaining commands
    }

    [Fact]
    public void DrainAll_ReturnsAllCommands()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);
        queue.TryEnqueue(new TestCommand("a"));
        queue.TryEnqueue(new TestCommand("b"));
        queue.TryEnqueue(new TestCommand("c"));

        var commands = queue.DrainAll();

        Assert.Equal(3, commands.Count);
        Assert.Equal("a", ((TestCommand)commands[0]).Value);
        Assert.Equal("b", ((TestCommand)commands[1]).Value);
        Assert.Equal("c", ((TestCommand)commands[2]).Value);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Complete_MarksQueueAsCompleted()
    {
        var queue = new BoundedCommandQueue<TestCommand>(10);
        queue.TryEnqueue(new TestCommand("test"));

        queue.Complete();

        // After completion, new enqueues should fail
        var result = queue.TryEnqueue(new TestCommand("after complete"));
        Assert.False(result);
    }

    [Fact]
    public async Task ConcurrentProducers_AllCommandsEnqueued()
    {
        const int producerCount = 10;
        const int commandsPerProducer = 100;
        var queue = new BoundedCommandQueue<TestCommand>(producerCount * commandsPerProducer);

        var tasks = Enumerable.Range(0, producerCount)
            .Select(async producerId =>
            {
                for (int i = 0; i < commandsPerProducer; i++)
                {
                    await queue.EnqueueAsync(new TestCommand($"P{producerId}-{i}"));
                }
            });

        await Task.WhenAll(tasks);

        Assert.Equal(producerCount * commandsPerProducer, queue.Count);
    }

    [Fact]
    public async Task ProducerConsumer_AllCommandsProcessed()
    {
        const int totalCommands = 1000;
        var queue = new BoundedCommandQueue<TestCommand>(100);
        var processedCommands = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var cts = new CancellationTokenSource();

        // Consumer task
        var consumerTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested || queue.Count > 0)
            {
                if (queue.TryDequeue(out var command))
                {
                    processedCommands.Add(command.Value);
                }
                else
                {
                    await Task.Delay(1, cts.Token).ContinueWith(_ => { }); // Small delay to avoid tight loop
                }

                if (processedCommands.Count >= totalCommands)
                    break;
            }
        });

        // Producer
        for (int i = 0; i < totalCommands; i++)
        {
            await queue.EnqueueAsync(new TestCommand($"cmd-{i}"));
        }

        // Wait for consumer to finish
        await Task.WhenAny(consumerTask, Task.Delay(5000));
        cts.Cancel();

        Assert.Equal(totalCommands, processedCommands.Count);
    }
}
