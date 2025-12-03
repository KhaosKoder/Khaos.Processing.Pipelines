using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Khaos.Processing.Pipelines;
using Xunit;

namespace Khaos.Processing.Pipelines.Tests;

public class ProcessingPipelineTests
{
    [Fact]
    public async Task BuildWithoutSteps_ReturnsInput()
    {
        var pipeline = Pipeline.Start<int>().Build();
        var context = new PipelineContext();

        var result = await pipeline.ProcessAsync(7, context, CancellationToken.None);

        Assert.Equal(StepOutcomeKind.Continue, result.Kind);
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public async Task Pipeline_ComposesMultipleSteps()
    {
        var log = new List<string>();
        var pipeline = Pipeline
            .Start<int>()
            .UseStep(new RecordingTransformStep(x => x + 1, "first", log))
            .UseStep(new RecordingTransformStep(x => x * 2, "second", log))
            .Build();

        var context = new PipelineContext();
        var result = await pipeline.ProcessAsync(3, context, CancellationToken.None);

        Assert.Equal(StepOutcomeKind.Continue, result.Kind);
        Assert.Equal(8, result.Value);
        Assert.Equal(new[] { "first:3", "second:4" }, log);
    }

    [Fact]
    public async Task Pipeline_StopsWhenStepAborts()
    {
        var log = new List<string>();
        var pipeline = Pipeline
            .Start<int>()
            .UseStep(new RecordingTransformStep(x => x + 1, "first", log))
            .UseStep(new AbortWhenValueStep(5, log))
            .UseStep(new RecordingTransformStep(x => x * 10, "third", log))
            .Build();

        var context = new PipelineContext();
        var result = await pipeline.ProcessAsync(4, context, CancellationToken.None);

        Assert.Equal(StepOutcomeKind.Abort, result.Kind);
        Assert.Equal(new[] { "first:4", "abort" }, log);
    }

    [Fact]
    public async Task ProcessAsync_ThrowsWhenContextMissing()
    {
        var pipeline = Pipeline.Start<int>().Build();
        await Assert.ThrowsAsync<ArgumentNullException>(() => pipeline.ProcessAsync(1, null!, CancellationToken.None).AsTask());
    }

    private sealed class RecordingTransformStep : IPipelineStep<int, int>
    {
        private readonly Func<int, int> _transform;
        private readonly string _name;
        private readonly List<string> _log;

        public RecordingTransformStep(Func<int, int> transform, string name, List<string> log)
        {
            _transform = transform;
            _name = name;
            _log = log;
        }

        public ValueTask<StepOutcome<int>> InvokeAsync(int input, PipelineContext context, CancellationToken cancellationToken)
        {
            _log.Add($"{_name}:{input}");
            var result = _transform(input);
            return ValueTask.FromResult(StepOutcome<int>.Continue(result));
        }
    }

    private sealed class AbortWhenValueStep : IPipelineStep<int, int>
    {
        private readonly int _stopValue;
        private readonly List<string> _log;

        public AbortWhenValueStep(int stopValue, List<string> log)
        {
            _stopValue = stopValue;
            _log = log;
        }

        public ValueTask<StepOutcome<int>> InvokeAsync(int input, PipelineContext context, CancellationToken cancellationToken)
        {
            _log.Add("abort");
            return input == _stopValue
                ? ValueTask.FromResult(StepOutcome<int>.Abort())
                : ValueTask.FromResult(StepOutcome<int>.Continue(input));
        }
    }
}
