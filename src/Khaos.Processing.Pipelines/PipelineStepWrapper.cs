namespace Khaos.Processing.Pipelines;

internal sealed class PipelineStepWrapper
{
    private readonly Func<object?, PipelineContext, CancellationToken, ValueTask<StepOutcome<object?>>> _perRecordInvoker;
    private readonly Func<IReadOnlyList<object?>, PipelineContext, CancellationToken, Task<IReadOnlyList<StepOutcome<object?>>>>? _batchInvoker;
    public string StepName { get; }

    private PipelineStepWrapper(
        string stepName,
        Func<object?, PipelineContext, CancellationToken, ValueTask<StepOutcome<object?>>> perRecordInvoker,
        Func<IReadOnlyList<object?>, PipelineContext, CancellationToken, Task<IReadOnlyList<StepOutcome<object?>>>>? batchInvoker)
    {
        StepName = stepName;
        _perRecordInvoker = perRecordInvoker;
        _batchInvoker = batchInvoker;
    }

    public bool IsBatchAware => _batchInvoker is not null;

    public ValueTask<StepOutcome<object?>> InvokeSingleAsync(
        object? input,
        PipelineContext context,
        CancellationToken cancellationToken) => _perRecordInvoker(input, context, cancellationToken);

    public Task<IReadOnlyList<StepOutcome<object?>>> InvokeBatchAsync(
        IReadOnlyList<object?> inputs,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        if (_batchInvoker is null)
        {
            throw new InvalidOperationException("Step is not batch-aware.");
        }

        return _batchInvoker(inputs, context, cancellationToken);
    }

    public static PipelineStepWrapper Create<TIn, TOut>(IPipelineStep<TIn, TOut> step)
    {
        if (step is null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        async ValueTask<StepOutcome<object?>> InvokePerRecord(
            object? value,
            PipelineContext context,
            CancellationToken cancellationToken)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var typedValue = (TIn)value!;
            var outcome = await step.InvokeAsync(typedValue, context, cancellationToken).ConfigureAwait(false);
            return ConvertOutcome(outcome);
        }

        Func<IReadOnlyList<object?>, PipelineContext, CancellationToken, Task<IReadOnlyList<StepOutcome<object?>>>>? batchInvoker = null;

        if (step is IBatchAwareStep<TIn, TOut> batchAware)
        {
            batchInvoker = async (values, context, cancellationToken) =>
            {
                var typedValues = new TIn[values.Count];
                for (var i = 0; i < values.Count; i++)
                {
                    typedValues[i] = (TIn)values[i]!;
                }

                var batchOutcomes = await batchAware.InvokeBatchAsync(typedValues, context, cancellationToken).ConfigureAwait(false);
                var converted = new StepOutcome<object?>[batchOutcomes.Count];
                for (var i = 0; i < batchOutcomes.Count; i++)
                {
                    converted[i] = ConvertOutcome(batchOutcomes[i]);
                }

                return converted;
            };
        }

        var stepName = step.GetType().Name;
        return new PipelineStepWrapper(stepName, InvokePerRecord, batchInvoker);
    }

    private static StepOutcome<object?> ConvertOutcome<TOut>(StepOutcome<TOut> outcome)
        => outcome.Kind == StepOutcomeKind.Continue
            ? StepOutcome<object?>.Continue(outcome.Value)
            : StepOutcome<object?>.Abort();
}
