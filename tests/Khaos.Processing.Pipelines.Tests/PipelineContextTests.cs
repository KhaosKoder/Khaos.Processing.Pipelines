using System.Collections.Generic;
using Khaos.Processing.Pipelines;
using Xunit;

namespace Khaos.Processing.Pipelines.Tests;

public class PipelineContextTests
{
    [Fact]
    public void SetAndGet_RoundTripsValues()
    {
        var ctx = new PipelineContext();
        ctx.Set("answer", 42);

        var value = ctx.Get<int>("answer");

        Assert.Equal(42, value);
        Assert.True(ctx.Items.ContainsKey("answer"));
    }

    [Fact]
    public void Get_ThrowsForMissingKey()
    {
        var ctx = new PipelineContext();
        Assert.Throws<KeyNotFoundException>(() => ctx.Get<int>("missing"));
    }

    [Fact]
    public void TryGet_ReturnsFalseWhenTypeMismatch()
    {
        var ctx = new PipelineContext();
        ctx.Set("number", 5);

        var found = ctx.TryGet<string>("number", out var result);

        Assert.False(found);
        Assert.Null(result);
    }
}
