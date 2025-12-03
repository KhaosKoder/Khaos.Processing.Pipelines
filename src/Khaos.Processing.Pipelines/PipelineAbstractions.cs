namespace Khaos.Processing.Pipelines;

/// <summary>
/// Represents a per-record pipeline step.
/// </summary>
public interface IPipelineStep<TIn, TOut>
{
    ValueTask<StepOutcome<TOut>> InvokeAsync(
        TIn input,
        PipelineContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional interface that enables a step to operate over an entire batch at once.
/// </summary>
public interface IBatchAwareStep<TIn, TOut>
{
    Task<IReadOnlyList<StepOutcome<TOut>>> InvokeBatchAsync(
        IReadOnlyList<TIn> inputs,
        PipelineContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Represents a composed processing pipeline executed per record.
/// </summary>
public interface IProcessingPipeline<TIn, TOut>
{
    ValueTask<StepOutcome<TOut>> ProcessAsync(
        TIn input,
        PipelineContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fluent builder for creating processing pipelines.
/// </summary>
public interface IPipelineBuilder<TIn, TCurrent>
{
    IPipelineBuilder<TIn, TNext> UseStep<TNext>(IPipelineStep<TCurrent, TNext> step);

    IProcessingPipeline<TIn, TCurrent> Build();
}

/// <summary>
/// Entry-point helpers for the fluent pipeline builder.
/// </summary>
public static class Pipeline
{
    public static IPipelineBuilder<T, T> Start<T>() => new PipelineBuilder<T, T>();
}
