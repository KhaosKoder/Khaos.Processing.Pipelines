namespace Khaos.Processing.Pipelines;

internal interface IInternalProcessingPipeline
{
    IReadOnlyList<PipelineStepWrapper> Steps { get; }
}

internal sealed class ProcessingPipeline<TIn, TOut> : IProcessingPipeline<TIn, TOut>, IInternalProcessingPipeline
{
    private readonly PipelineStepWrapper[] _steps;

    public ProcessingPipeline(PipelineStepWrapper[] steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    }

    public IReadOnlyList<PipelineStepWrapper> Steps => _steps;

    public async ValueTask<StepOutcome<TOut>> ProcessAsync(
        TIn input,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        object? current = input;
        if (_steps.Length == 0)
        {
            return StepOutcome<TOut>.Continue((TOut)current!);
        }

        foreach (var step in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await step.InvokeSingleAsync(current!, context, cancellationToken).ConfigureAwait(false);
            if (outcome.Kind == StepOutcomeKind.Abort)
            {
                return StepOutcome<TOut>.Abort();
            }

            current = outcome.Value;
        }

        return StepOutcome<TOut>.Continue((TOut)current!);
    }
}
