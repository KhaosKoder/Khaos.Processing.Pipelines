namespace Khaos.Processing.Pipelines;

internal sealed class PipelineBuilder<TIn, TCurrent> : IPipelineBuilder<TIn, TCurrent>
{
    private readonly List<PipelineStepWrapper> _steps;

    public PipelineBuilder()
        : this(new List<PipelineStepWrapper>())
    {
    }

    private PipelineBuilder(List<PipelineStepWrapper> steps)
    {
        _steps = steps;
    }

    public IPipelineBuilder<TIn, TNext> UseStep<TNext>(IPipelineStep<TCurrent, TNext> step)
    {
        if (step is null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        var nextSteps = new List<PipelineStepWrapper>(_steps)
        {
            PipelineStepWrapper.Create(step)
        };

        return new PipelineBuilder<TIn, TNext>(nextSteps);
    }

    public IProcessingPipeline<TIn, TCurrent> Build()
    {
        var frozen = _steps.ToArray();
        return new ProcessingPipeline<TIn, TCurrent>(frozen);
    }
}
