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

    /// <summary>
    /// Implicit conversion to the abstractions type.
    /// </summary>
    public static implicit operator Khaos.Pipeline.Abstractions.StepOutcome<TOut>(StepOutcome<TOut> outcome)
        => outcome.Kind == StepOutcomeKind.Continue
            ? Khaos.Pipeline.Abstractions.StepOutcome<TOut>.Continue(outcome.Value!)
            : Khaos.Pipeline.Abstractions.StepOutcome<TOut>.Abort();

    /// <summary>
    /// Implicit conversion from the abstractions type.
    /// </summary>
    public static implicit operator StepOutcome<TOut>(Khaos.Pipeline.Abstractions.StepOutcome<TOut> outcome)
        => outcome.Kind == Khaos.Pipeline.Abstractions.StepOutcomeKind.Continue
            ? Continue(outcome.Value!)
            : Abort();
}

/// <summary>
/// Outcome classification values for pipeline steps.
/// </summary>
public enum StepOutcomeKind
{
    Continue,
    Abort
}
