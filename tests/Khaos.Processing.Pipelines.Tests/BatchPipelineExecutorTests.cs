using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khaos.Processing.Pipelines;
using Khaos.Processing.Pipelines.Instrumentation;
using Xunit;

namespace Khaos.Processing.Pipelines.Tests;

public class BatchPipelineExecutorTests
{
    [Fact]
    public async Task SequentialProcessing_StopsOnAbortAndReportsMetrics()
    {
        var metrics = new TestMetrics();
        var recorder = new List<int>();
        var pipeline = Pipeline
            .Start<int>()
            .UseStep(new AbortNonPositiveStep())
            .UseStep(new RecordingStep(recorder.Add, x => x + 10))
            .Build();

        var executor = new BatchPipelineExecutor<int, int>("orders", metrics);
        var options = new PipelineExecutionOptions { IsSequential = true, MaxDegreeOfParallelism = 1 };

        await executor.ProcessBatchAsync(new[] { 1, 0, 2 }, pipeline, new PipelineContext(), options);

        Assert.Equal(new[] { 11, 12 }, recorder);
        Assert.Equal(new[] { nameof(AbortNonPositiveStep), nameof(RecordingStep) }, metrics.RecordedSteps);
        Assert.Contains(1, metrics.RecordedOutcomes); // zero was aborted by the first step
    }

    [Fact]
    public async Task ParallelProcessing_ProcessesAllItems()
    {
        var metrics = new TestMetrics();
        var results = new ConcurrentBag<int>();
        var pipeline = Pipeline
            .Start<int>()
            .UseStep(new RecordingStep(results.Add, x => x * 2))
            .Build();

        var executor = new BatchPipelineExecutor<int, int>("orders", metrics);
        var options = new PipelineExecutionOptions { IsSequential = false, MaxDegreeOfParallelism = 4 };
        var source = Enumerable.Range(1, 20).ToArray();

        await executor.ProcessBatchAsync(source, pipeline, new PipelineContext(), options);

        var ordered = results.OrderBy(x => x).ToArray();
        Assert.Equal(source.Select(x => x * 2), ordered);
        Assert.Single(metrics.RecordedSteps);
    }

    [Fact]
    public async Task BatchAwareStep_ProcessesEntireBatch()
    {
        var metrics = new TestMetrics();
        var recorder = new List<int>();
        var pipeline = Pipeline
            .Start<int>()
            .UseStep(new BatchScaleStep(3))
            .UseStep(new RecordingStep(recorder.Add, x => x))
            .Build();

        var executor = new BatchPipelineExecutor<int, int>("orders", metrics);
        var options = new PipelineExecutionOptions { IsSequential = true, MaxDegreeOfParallelism = 1 };

        await executor.ProcessBatchAsync(new[] { 1, 2, 3 }, pipeline, new PipelineContext(), options);

        Assert.Equal(new[] { 3, 6, 9 }, recorder);
        Assert.Equal(nameof(BatchScaleStep), metrics.RecordedSteps.First());
    }

    [Fact]
    public async Task ProcessBatchAsync_ThrowsWhenPipelineNotFromBuilder()
    {
        var executor = new BatchPipelineExecutor<int, int>();
        var options = new PipelineExecutionOptions { IsSequential = true, MaxDegreeOfParallelism = 1 };

        await Assert.ThrowsAsync<ArgumentException>(() => executor.ProcessBatchAsync(new[] { 1 }, new FakePipeline(), new PipelineContext(), options));
    }

    [Fact]
    public async Task ProcessBatchAsync_ValidatesArguments()
    {
        var executor = new BatchPipelineExecutor<int, int>();
        var pipeline = Pipeline.Start<int>().Build();
        var options = new PipelineExecutionOptions { IsSequential = true, MaxDegreeOfParallelism = 1 };

        await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ProcessBatchAsync(null!, pipeline, new PipelineContext(), options));
        await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ProcessBatchAsync(Array.Empty<int>(), null!, new PipelineContext(), options));
        await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ProcessBatchAsync(Array.Empty<int>(), pipeline, null!, options));
        await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ProcessBatchAsync(Array.Empty<int>(), pipeline, new PipelineContext(), null!));
    }

    [Fact]
    public async Task ProcessBatchAsync_ThrowsWhenMaxDegreeInvalid()
    {
        var executor = new BatchPipelineExecutor<int, int>();
        var pipeline = Pipeline.Start<int>().Build();
        var options = new PipelineExecutionOptions { IsSequential = false, MaxDegreeOfParallelism = 0 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => executor.ProcessBatchAsync(new[] { 1 }, pipeline, new PipelineContext(), options));
    }

    [Fact]
    public async Task ProcessBatchAsync_SkipsEmptyBatches()
    {
        var metrics = new TestMetrics();
        var executor = new BatchPipelineExecutor<int, int>("orders", metrics);
        var sink = new List<int>();
        var pipeline = Pipeline
            .Start<int>()
            .UseStep(new RecordingStep(sink.Add, x => x))
            .Build();

        var options = new PipelineExecutionOptions { IsSequential = true, MaxDegreeOfParallelism = 1 };
        await executor.ProcessBatchAsync(Array.Empty<int>(), pipeline, new PipelineContext(), options);

        Assert.Empty(metrics.RecordedSteps);
    }

    [Fact]
    public async Task ParallelProcessing_PropagatesStepExceptions()
    {
        var metrics = new TestMetrics();
        var pipeline = Pipeline
            .Start<int>()
            .UseStep(new ThrowOnValueStep(3))
            .Build();

        var executor = new BatchPipelineExecutor<int, int>("orders", metrics);
        var options = new PipelineExecutionOptions { IsSequential = false, MaxDegreeOfParallelism = 4 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ProcessBatchAsync(new[] { 1, 2, 3, 4 }, pipeline, new PipelineContext(), options));
        Assert.True(metrics.BatchFailures.Count > 0);
        Assert.True(metrics.StepFailures.Count > 0);
    }

    private sealed class AbortNonPositiveStep : IPipelineStep<int, int>
    {
        public ValueTask<StepOutcome<int>> InvokeAsync(int input, PipelineContext context, CancellationToken cancellationToken)
        {
            if (input <= 0)
            {
                return ValueTask.FromResult(StepOutcome<int>.Abort());
            }

            return ValueTask.FromResult(StepOutcome<int>.Continue(input));
        }
    }

    private sealed class RecordingStep : IPipelineStep<int, int>
    {
        private readonly Action<int> _onRecorded;
        private readonly Func<int, int> _transform;

        public RecordingStep(Action<int> onRecorded, Func<int, int> transform)
        {
            _onRecorded = onRecorded;
            _transform = transform;
        }

        public ValueTask<StepOutcome<int>> InvokeAsync(int input, PipelineContext context, CancellationToken cancellationToken)
        {
            var value = _transform(input);
            _onRecorded(value);
            return ValueTask.FromResult(StepOutcome<int>.Continue(value));
        }
    }

    private sealed class BatchScaleStep : IPipelineStep<int, int>, IBatchAwareStep<int, int>
    {
        private readonly int _factor;

        public BatchScaleStep(int factor)
        {
            _factor = factor;
        }

        public ValueTask<StepOutcome<int>> InvokeAsync(int input, PipelineContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(StepOutcome<int>.Continue(input * _factor));

        public Task<IReadOnlyList<StepOutcome<int>>> InvokeBatchAsync(IReadOnlyList<int> inputs, PipelineContext context, CancellationToken cancellationToken)
        {
            var results = inputs.Select(v => StepOutcome<int>.Continue(v * _factor)).ToArray();
            return Task.FromResult<IReadOnlyList<StepOutcome<int>>>(results);
        }
    }

    private sealed class ThrowOnValueStep : IPipelineStep<int, int>
    {
        private readonly int _badValue;

        public ThrowOnValueStep(int badValue)
        {
            _badValue = badValue;
        }

        public ValueTask<StepOutcome<int>> InvokeAsync(int input, PipelineContext context, CancellationToken cancellationToken)
        {
            if (input == _badValue)
            {
                throw new InvalidOperationException($"Value {input} is invalid.");
            }

            return ValueTask.FromResult(StepOutcome<int>.Continue(input));
        }
    }

    private sealed class FakePipeline : IProcessingPipeline<int, int>
    {
        public ValueTask<StepOutcome<int>> ProcessAsync(int input, PipelineContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(StepOutcome<int>.Continue(input));
    }

    private sealed class TestMetrics : IPipelineMetrics
    {
        public List<string> RecordedSteps { get; } = new();
        public List<int> RecordedOutcomes { get; } = new();
        public List<Exception> BatchFailures { get; } = new();
        public List<Exception> StepFailures { get; } = new();

        public IPipelineBatchScope TrackBatch(string pipelineName, int recordCount, bool isSequential, int maxDegreeOfParallelism)
            => new BatchScope(this);

        private sealed class BatchScope : IPipelineBatchScope
        {
            private readonly TestMetrics _owner;

            public BatchScope(TestMetrics owner)
            {
                _owner = owner;
            }

            public IPipelineStepScope TrackStep(string stepName, int recordCount, bool isBatchAware)
                => new StepScope(_owner, stepName);

            public void MarkFailed(Exception exception) => _owner.BatchFailures.Add(exception);

            public void Dispose()
            {
            }
        }

        private sealed class StepScope : IPipelineStepScope
        {
            private readonly TestMetrics _owner;
            private readonly string _stepName;

            public StepScope(TestMetrics owner, string stepName)
            {
                _owner = owner;
                _stepName = stepName;
                _owner.RecordedSteps.Add(stepName);
            }

            public void RecordOutcome(int abortedCount) => _owner.RecordedOutcomes.Add(abortedCount);

            public void MarkFailed(Exception exception) => _owner.StepFailures.Add(exception);

            public void Dispose()
            {
            }
        }
    }
}
