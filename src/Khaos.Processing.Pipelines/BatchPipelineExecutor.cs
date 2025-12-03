using System.Runtime.ExceptionServices;
using Khaos.Processing.Pipelines.Instrumentation;

namespace Khaos.Processing.Pipelines;

/// <summary>
/// Execution settings for batch processing.
/// </summary>
public sealed class PipelineExecutionOptions
{
    public bool IsSequential { get; init; }
    public int MaxDegreeOfParallelism { get; init; } = 1;
}

/// <summary>
/// Executes processing pipelines over batches of records.
/// </summary>
public interface IBatchPipelineExecutor<TIn, TOut>
{
    Task ProcessBatchAsync(
        IReadOnlyList<TIn> items,
        IProcessingPipeline<TIn, TOut> pipeline,
        PipelineContext context,
        PipelineExecutionOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of <see cref="IBatchPipelineExecutor{TIn, TOut}"/>.
/// </summary>
public sealed class BatchPipelineExecutor<TIn, TOut> : IBatchPipelineExecutor<TIn, TOut>
{
    private readonly string _pipelineName;
    private readonly IPipelineMetrics _metrics;

    public BatchPipelineExecutor()
        : this(null, null)
    {
    }

    public BatchPipelineExecutor(string? pipelineName, IPipelineMetrics? metrics = null)
    {
        _pipelineName = string.IsNullOrWhiteSpace(pipelineName) ? "pipeline" : pipelineName;
        _metrics = metrics ?? NoOpPipelineMetrics.Instance;
    }

    public async Task ProcessBatchAsync(
        IReadOnlyList<TIn> items,
        IProcessingPipeline<TIn, TOut> pipeline,
        PipelineContext context,
        PipelineExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (pipeline is null)
        {
            throw new ArgumentNullException(nameof(pipeline));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (items.Count == 0)
        {
            return;
        }

        if (options.MaxDegreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxDegreeOfParallelism), "MaxDegreeOfParallelism must be greater than zero.");
        }

        if (pipeline is not IInternalProcessingPipeline internalPipeline)
        {
            throw new ArgumentException("Pipeline instance was not created via the pipeline builder.", nameof(pipeline));
        }

        var workItems = new PipelineWorkItem[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            workItems[i] = PipelineWorkItem.Active(items[i]);
        }

        using var batchScope = _metrics.TrackBatch(_pipelineName, items.Count, options.IsSequential, options.MaxDegreeOfParallelism);

        try
        {
            foreach (var step in internalPipeline.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var activeBefore = CountActiveItems(workItems);
                if (activeBefore == 0)
                {
                    break;
                }

                var abortedBefore = workItems.Length - activeBefore;
                using var stepScope = batchScope.TrackStep(step.StepName, activeBefore, step.IsBatchAware);

                try
                {
                    if (step.IsBatchAware)
                    {
                        await InvokeBatchStepAsync(step, workItems, context, cancellationToken).ConfigureAwait(false);
                    }
                    else if (options.IsSequential || options.MaxDegreeOfParallelism == 1)
                    {
                        await InvokeSequentialAsync(step, workItems, context, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await InvokeParallelAsync(step, workItems, context, options.MaxDegreeOfParallelism, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    stepScope.MarkFailed(ex);
                    throw;
                }
                finally
                {
                    var activeAfter = CountActiveItems(workItems);
                    var abortedAfter = workItems.Length - activeAfter;
                    var abortedByStep = Math.Max(0, abortedAfter - abortedBefore);
                    stepScope.RecordOutcome(abortedByStep);
                }
            }
        }
        catch (Exception ex)
        {
            batchScope.MarkFailed(ex);
            throw;
        }
    }

    private static async Task InvokeBatchStepAsync(
        PipelineStepWrapper step,
        PipelineWorkItem[] workItems,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        var activeIndexes = new List<int>(workItems.Length);
        var activeValues = new List<object?>(workItems.Length);

        for (var i = 0; i < workItems.Length; i++)
        {
            var item = workItems[i];
            if (item.IsAborted)
            {
                continue;
            }

            activeIndexes.Add(i);
            activeValues.Add(item.Value);
        }

        if (activeIndexes.Count == 0)
        {
            return;
        }

        var outcomes = await step.InvokeBatchAsync(activeValues, context, cancellationToken).ConfigureAwait(false);
        if (outcomes.Count != activeIndexes.Count)
        {
            throw new InvalidOperationException("Batch step did not return an outcome for every input.");
        }

        for (var i = 0; i < activeIndexes.Count; i++)
        {
            var outcome = outcomes[i];
            var index = activeIndexes[i];

            if (outcome.Kind == StepOutcomeKind.Abort)
            {
                workItems[index] = workItems[index].Abort();
            }
            else
            {
                workItems[index] = workItems[index].ContinueWith(outcome.Value);
            }
        }
    }

    private static int CountActiveItems(PipelineWorkItem[] workItems)
    {
        var count = 0;
        for (var i = 0; i < workItems.Length; i++)
        {
            if (!workItems[i].IsAborted)
            {
                count++;
            }
        }

        return count;
    }

    private static async Task InvokeSequentialAsync(
        PipelineStepWrapper step,
        PipelineWorkItem[] workItems,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < workItems.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = workItems[i];
            if (item.IsAborted)
            {
                continue;
            }

            var outcome = await step.InvokeSingleAsync(item.Value, context, cancellationToken).ConfigureAwait(false);
            workItems[i] = outcome.Kind == StepOutcomeKind.Abort
                ? item.Abort()
                : item.ContinueWith(outcome.Value);
        }
    }

    private static async Task InvokeParallelAsync(
        PipelineStepWrapper step,
        PipelineWorkItem[] workItems,
        PipelineContext context,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        using var throttler = new SemaphoreSlim(maxDegreeOfParallelism);
        ExceptionDispatchInfo? capturedException = null;
        var tasks = new List<Task>(workItems.Length);

        for (var i = 0; i < workItems.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = i;
            if (workItems[index].IsAborted)
            {
                continue;
            }

            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var outcome = await step.InvokeSingleAsync(workItems[index].Value, context, cancellationToken).ConfigureAwait(false);
                    workItems[index] = outcome.Kind == StepOutcomeKind.Abort
                        ? workItems[index].Abort()
                        : workItems[index].ContinueWith(outcome.Value);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref capturedException, ExceptionDispatchInfo.Capture(ex), null);
                }
                finally
                {
                    throttler.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        capturedException?.Throw();
    }

    private readonly struct PipelineWorkItem
    {
        private PipelineWorkItem(bool isAborted, object? value)
        {
            IsAborted = isAborted;
            Value = value;
        }

        public static PipelineWorkItem Active(object? value) => new(false, value);

        public bool IsAborted { get; }
        public object? Value { get; }

        public PipelineWorkItem Abort() => new(true, Value);

        public PipelineWorkItem ContinueWith(object? nextValue) => new(false, nextValue);
    }
}
