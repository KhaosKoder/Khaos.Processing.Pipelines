namespace Khaos.Processing.Pipelines.Instrumentation;

public interface IPipelineMetrics
{
    IPipelineBatchScope TrackBatch(string pipelineName, int recordCount, bool isSequential, int maxDegreeOfParallelism);
}

public interface IPipelineBatchScope : IDisposable
{
    IPipelineStepScope TrackStep(string stepName, int recordCount, bool isBatchAware);
    void MarkFailed(Exception exception);
}

public interface IPipelineStepScope : IDisposable
{
    void RecordOutcome(int abortedCount);
    void MarkFailed(Exception exception);
}

public sealed class NoOpPipelineMetrics : IPipelineMetrics, IPipelineBatchScope, IPipelineStepScope
{
    public static readonly NoOpPipelineMetrics Instance = new();

    private NoOpPipelineMetrics()
    {
    }

    public IPipelineBatchScope TrackBatch(string pipelineName, int recordCount, bool isSequential, int maxDegreeOfParallelism)
        => this;

    public IPipelineStepScope TrackStep(string stepName, int recordCount, bool isBatchAware) => this;

    public void RecordOutcome(int abortedCount)
    {
    }

    public void MarkFailed(Exception exception)
    {
    }

    public void Dispose()
    {
    }
}
