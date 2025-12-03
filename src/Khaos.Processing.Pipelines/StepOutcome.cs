namespace Khaos.Processing.Pipelines;

/// <summary>
/// Represents the decision returned from a pipeline step.
/// </summary>
/// <typeparam name="TOut">The output type for the step.</typeparam>
public readonly struct StepOutcome<TOut>
{
    /// <summary>
    /// Initializes a new outcome.
    /// </summary>
    private StepOutcome(StepOutcomeKind kind, TOut? value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>
    /// Gets the outcome classification.
    /// </summary>
    public StepOutcomeKind Kind { get; }

    /// <summary>
    /// Gets the value produced by the step when <see cref="Kind"/> is <see cref="StepOutcomeKind.Continue"/>.
    /// </summary>
    public TOut? Value { get; }

    /// <summary>
    /// Creates a continue outcome with the provided value.
    /// </summary>
    public static StepOutcome<TOut> Continue(TOut value) => new(StepOutcomeKind.Continue, value);

    /// <summary>
    /// Creates an abort outcome, signalling downstream steps should be skipped for the current record.
    /// </summary>
    public static StepOutcome<TOut> Abort() => new(StepOutcomeKind.Abort, default);
}

/// <summary>
/// Outcome classification values for pipeline steps.
/// </summary>
public enum StepOutcomeKind
{
    Continue,
    Abort
}
