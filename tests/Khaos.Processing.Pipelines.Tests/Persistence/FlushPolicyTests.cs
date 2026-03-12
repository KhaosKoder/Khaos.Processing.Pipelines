using Khaos.Processing.Pipelines.Persistence;
using Xunit;

namespace Khaos.Processing.Pipelines.Tests.Persistence;

public class FlushPolicyTests
{
    #region TimeBasedFlushPolicy

    [Fact]
    public void TimeBasedFlushPolicy_Constructor_ThrowsForNegativeInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeBasedFlushPolicy(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void TimeBasedFlushPolicy_Name_IsCorrect()
    {
        var policy = new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5));
        Assert.Equal("TimeBased", policy.Name);
    }

    [Fact]
    public void TimeBasedFlushPolicy_ShouldFlush_ReturnsFalseWhenNoDirtyItems()
    {
        var policy = new TimeBasedFlushPolicy(TimeSpan.FromSeconds(1));
        var context = new FlushContext
        {
            DirtyCount = 0,
            ElapsedSinceLastFlush = TimeSpan.FromSeconds(5)
        };

        Assert.False(policy.ShouldFlush(context));
    }

    [Fact]
    public void TimeBasedFlushPolicy_ShouldFlush_ReturnsFalseWhenBelowInterval()
    {
        var policy = new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5));
        var context = new FlushContext
        {
            DirtyCount = 10,
            ElapsedSinceLastFlush = TimeSpan.FromMinutes(2)
        };

        Assert.False(policy.ShouldFlush(context));
    }

    [Fact]
    public void TimeBasedFlushPolicy_ShouldFlush_ReturnsTrueWhenAtOrAboveInterval()
    {
        var policy = new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5));
        var context = new FlushContext
        {
            DirtyCount = 10,
            ElapsedSinceLastFlush = TimeSpan.FromMinutes(5)
        };

        Assert.True(policy.ShouldFlush(context));
    }

    [Fact]
    public void TimeBasedFlushPolicy_ShouldFlush_ReturnsTrueWhenAboveInterval()
    {
        var policy = new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5));
        var context = new FlushContext
        {
            DirtyCount = 1,
            ElapsedSinceLastFlush = TimeSpan.FromMinutes(10)
        };

        Assert.True(policy.ShouldFlush(context));
    }

    #endregion

    #region CountBasedFlushPolicy

    [Fact]
    public void CountBasedFlushPolicy_Constructor_ThrowsForZeroThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CountBasedFlushPolicy(0));
    }

    [Fact]
    public void CountBasedFlushPolicy_Constructor_ThrowsForNegativeThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CountBasedFlushPolicy(-1));
    }

    [Fact]
    public void CountBasedFlushPolicy_Name_IsCorrect()
    {
        var policy = new CountBasedFlushPolicy(100);
        Assert.Equal("CountBased", policy.Name);
    }

    [Fact]
    public void CountBasedFlushPolicy_ShouldFlush_ReturnsFalseWhenBelowThreshold()
    {
        var policy = new CountBasedFlushPolicy(100);
        var context = new FlushContext { DirtyCount = 50 };

        Assert.False(policy.ShouldFlush(context));
    }

    [Fact]
    public void CountBasedFlushPolicy_ShouldFlush_ReturnsTrueWhenAtThreshold()
    {
        var policy = new CountBasedFlushPolicy(100);
        var context = new FlushContext { DirtyCount = 100 };

        Assert.True(policy.ShouldFlush(context));
    }

    [Fact]
    public void CountBasedFlushPolicy_ShouldFlush_ReturnsTrueWhenAboveThreshold()
    {
        var policy = new CountBasedFlushPolicy(100);
        var context = new FlushContext { DirtyCount = 150 };

        Assert.True(policy.ShouldFlush(context));
    }

    #endregion

    #region MemoryPressureFlushPolicy

    [Fact]
    public void MemoryPressureFlushPolicy_Constructor_ThrowsForZeroThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MemoryPressureFlushPolicy(0));
    }

    [Fact]
    public void MemoryPressureFlushPolicy_Constructor_ThrowsForNegativeThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MemoryPressureFlushPolicy(-1));
    }

    [Fact]
    public void MemoryPressureFlushPolicy_Name_IsCorrect()
    {
        var policy = new MemoryPressureFlushPolicy(1024 * 1024);
        Assert.Equal("MemoryPressure", policy.Name);
    }

    [Fact]
    public void MemoryPressureFlushPolicy_ShouldFlush_ReturnsFalseWhenBelowThreshold()
    {
        var policy = new MemoryPressureFlushPolicy(1024 * 1024); // 1MB
        var context = new FlushContext { EstimatedDirtyMemoryBytes = 512 * 1024 }; // 512KB

        Assert.False(policy.ShouldFlush(context));
    }

    [Fact]
    public void MemoryPressureFlushPolicy_ShouldFlush_ReturnsTrueWhenAtThreshold()
    {
        var policy = new MemoryPressureFlushPolicy(1024 * 1024); // 1MB
        var context = new FlushContext { EstimatedDirtyMemoryBytes = 1024 * 1024 };

        Assert.True(policy.ShouldFlush(context));
    }

    [Fact]
    public void MemoryPressureFlushPolicy_ShouldFlush_ReturnsTrueWhenAboveThreshold()
    {
        var policy = new MemoryPressureFlushPolicy(1024 * 1024); // 1MB
        var context = new FlushContext { EstimatedDirtyMemoryBytes = 2 * 1024 * 1024 };

        Assert.True(policy.ShouldFlush(context));
    }

    #endregion

    #region DirtyRatioFlushPolicy

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void DirtyRatioFlushPolicy_Constructor_ThrowsForInvalidThreshold(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DirtyRatioFlushPolicy(threshold));
    }

    [Fact]
    public void DirtyRatioFlushPolicy_Name_IsCorrect()
    {
        var policy = new DirtyRatioFlushPolicy(0.5);
        Assert.Equal("DirtyRatio", policy.Name);
    }

    [Fact]
    public void DirtyRatioFlushPolicy_ShouldFlush_ReturnsFalseWhenTotalCountIsZero()
    {
        var policy = new DirtyRatioFlushPolicy(0.5);
        var context = new FlushContext { DirtyCount = 10, TotalCount = 0 };

        Assert.False(policy.ShouldFlush(context));
    }

    [Fact]
    public void DirtyRatioFlushPolicy_ShouldFlush_ReturnsFalseWhenBelowThreshold()
    {
        var policy = new DirtyRatioFlushPolicy(0.5);
        var context = new FlushContext { DirtyCount = 30, TotalCount = 100 }; // 30%

        Assert.False(policy.ShouldFlush(context));
    }

    [Fact]
    public void DirtyRatioFlushPolicy_ShouldFlush_ReturnsTrueWhenAtThreshold()
    {
        var policy = new DirtyRatioFlushPolicy(0.5);
        var context = new FlushContext { DirtyCount = 50, TotalCount = 100 }; // 50%

        Assert.True(policy.ShouldFlush(context));
    }

    [Fact]
    public void DirtyRatioFlushPolicy_ShouldFlush_ReturnsTrueWhenAboveThreshold()
    {
        var policy = new DirtyRatioFlushPolicy(0.5);
        var context = new FlushContext { DirtyCount = 70, TotalCount = 100 }; // 70%

        Assert.True(policy.ShouldFlush(context));
    }

    #endregion

    #region ExternalTriggerFlushPolicy

    [Fact]
    public void ExternalTriggerFlushPolicy_Name_IsCorrect()
    {
        var policy = new ExternalTriggerFlushPolicy();
        Assert.Equal("ExternalTrigger", policy.Name);
    }

    [Fact]
    public void ExternalTriggerFlushPolicy_ShouldFlush_ReturnsFalseWhenNoTrigger()
    {
        var policy = new ExternalTriggerFlushPolicy();
        var context = new FlushContext { DirtyCount = 10, ExternalTrigger = false };

        Assert.False(policy.ShouldFlush(context));
    }

    [Fact]
    public void ExternalTriggerFlushPolicy_ShouldFlush_ReturnsFalseWhenTriggerButNoDirty()
    {
        var policy = new ExternalTriggerFlushPolicy();
        var context = new FlushContext { DirtyCount = 0, ExternalTrigger = true };

        Assert.False(policy.ShouldFlush(context));
    }

    [Fact]
    public void ExternalTriggerFlushPolicy_ShouldFlush_ReturnsTrueWhenTriggerAndDirty()
    {
        var policy = new ExternalTriggerFlushPolicy();
        var context = new FlushContext { DirtyCount = 10, ExternalTrigger = true };

        Assert.True(policy.ShouldFlush(context));
    }

    #endregion

    #region ShutdownFlushPolicy

    [Fact]
    public void ShutdownFlushPolicy_Name_IsCorrect()
    {
        var policy = new ShutdownFlushPolicy();
        Assert.Equal("Shutdown", policy.Name);
    }

    [Fact]
    public void ShutdownFlushPolicy_ShouldFlush_ReturnsFalseWhenNotShutdown()
    {
        var policy = new ShutdownFlushPolicy();
        var context = new FlushContext { DirtyCount = 10, IsShutdown = false };

        Assert.False(policy.ShouldFlush(context));
    }

    [Fact]
    public void ShutdownFlushPolicy_ShouldFlush_ReturnsFalseWhenShutdownButNoDirty()
    {
        var policy = new ShutdownFlushPolicy();
        var context = new FlushContext { DirtyCount = 0, IsShutdown = true };

        Assert.False(policy.ShouldFlush(context));
    }

    [Fact]
    public void ShutdownFlushPolicy_ShouldFlush_ReturnsTrueWhenShutdownAndDirty()
    {
        var policy = new ShutdownFlushPolicy();
        var context = new FlushContext { DirtyCount = 10, IsShutdown = true };

        Assert.True(policy.ShouldFlush(context));
    }

    #endregion

    #region CompositeFlushPolicy

    [Fact]
    public void CompositeFlushPolicy_Constructor_ThrowsForNullName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CompositeFlushPolicy(null!, new CountBasedFlushPolicy(10)));
    }

    [Fact]
    public void CompositeFlushPolicy_Constructor_ThrowsForNullPolicies()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CompositeFlushPolicy("Test", (IFlushPolicy[])null!));
    }

    [Fact]
    public void CompositeFlushPolicy_Constructor_ThrowsForEmptyPolicies()
    {
        Assert.Throws<ArgumentException>(() =>
            new CompositeFlushPolicy("Test", Array.Empty<IFlushPolicy>()));
    }

    [Fact]
    public void CompositeFlushPolicy_Name_IsSet()
    {
        var policy = new CompositeFlushPolicy("MyComposite",
            new CountBasedFlushPolicy(10));

        Assert.Equal("MyComposite", policy.Name);
    }

    [Fact]
    public void CompositeFlushPolicy_Policies_ContainsAllPolicies()
    {
        var p1 = new CountBasedFlushPolicy(10);
        var p2 = new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5));
        var composite = new CompositeFlushPolicy("Test", p1, p2);

        Assert.Equal(2, composite.Policies.Count);
        Assert.Contains(p1, composite.Policies);
        Assert.Contains(p2, composite.Policies);
    }

    [Fact]
    public void CompositeFlushPolicy_ShouldFlush_ReturnsFalseWhenNoPolicyTriggered()
    {
        var composite = new CompositeFlushPolicy("Test",
            new CountBasedFlushPolicy(100),
            new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5)));

        var context = new FlushContext
        {
            DirtyCount = 10,
            ElapsedSinceLastFlush = TimeSpan.FromMinutes(1)
        };

        Assert.False(composite.ShouldFlush(context));
    }

    [Fact]
    public void CompositeFlushPolicy_ShouldFlush_ReturnsTrueWhenFirstPolicyTriggered()
    {
        var composite = new CompositeFlushPolicy("Test",
            new CountBasedFlushPolicy(10),
            new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5)));

        var context = new FlushContext
        {
            DirtyCount = 20,
            ElapsedSinceLastFlush = TimeSpan.FromMinutes(1)
        };

        Assert.True(composite.ShouldFlush(context));
    }

    [Fact]
    public void CompositeFlushPolicy_ShouldFlush_ReturnsTrueWhenSecondPolicyTriggered()
    {
        var composite = new CompositeFlushPolicy("Test",
            new CountBasedFlushPolicy(100),
            new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5)));

        var context = new FlushContext
        {
            DirtyCount = 1,
            ElapsedSinceLastFlush = TimeSpan.FromMinutes(10)
        };

        Assert.True(composite.ShouldFlush(context));
    }

    [Fact]
    public void CompositeFlushPolicy_ShouldFlush_ReturnsTrueWhenBothPoliciesTriggered()
    {
        var composite = new CompositeFlushPolicy("Test",
            new CountBasedFlushPolicy(10),
            new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5)));

        var context = new FlushContext
        {
            DirtyCount = 100,
            ElapsedSinceLastFlush = TimeSpan.FromMinutes(10)
        };

        Assert.True(composite.ShouldFlush(context));
    }

    [Fact]
    public void CompositeFlushPolicy_GetTriggeringPolicies_ReturnsEmptyWhenNoneTriggered()
    {
        var composite = new CompositeFlushPolicy("Test",
            new CountBasedFlushPolicy(100),
            new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5)));

        var context = new FlushContext
        {
            DirtyCount = 10,
            ElapsedSinceLastFlush = TimeSpan.FromMinutes(1)
        };

        var triggering = composite.GetTriggeringPolicies(context);
        Assert.Empty(triggering);
    }

    [Fact]
    public void CompositeFlushPolicy_GetTriggeringPolicies_ReturnsTriggeredPolicies()
    {
        var composite = new CompositeFlushPolicy("Test",
            new CountBasedFlushPolicy(10),
            new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5)));

        var context = new FlushContext
        {
            DirtyCount = 100,
            ElapsedSinceLastFlush = TimeSpan.FromMinutes(10)
        };

        var triggering = composite.GetTriggeringPolicies(context).ToList();
        Assert.Equal(2, triggering.Count);
        Assert.Contains("CountBased", triggering);
        Assert.Contains("TimeBased", triggering);
    }

    [Fact]
    public void CompositeFlushPolicy_GetTriggeringPolicies_ReturnsOnlyTriggeredPolicies()
    {
        var composite = new CompositeFlushPolicy("Test",
            new CountBasedFlushPolicy(100),
            new TimeBasedFlushPolicy(TimeSpan.FromMinutes(5)));

        var context = new FlushContext
        {
            DirtyCount = 1, // Count policy not triggered
            ElapsedSinceLastFlush = TimeSpan.FromMinutes(10) // Time policy triggered
        };

        var triggering = composite.GetTriggeringPolicies(context).ToList();
        Assert.Single(triggering);
        Assert.Contains("TimeBased", triggering);
    }

    #endregion
}
