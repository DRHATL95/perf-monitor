using FluentAssertions;
using PerfMonitor.Hardware;

namespace PerfMonitor.Hardware.Tests;

public class EmaSmootherTests
{
    [Fact]
    public void Smooth_FirstSample_ReturnsSampleUnchanged()
    {
        var ema = new EmaSmoother(alpha: 0.3f);
        ema.Smooth(10f).Should().Be(10f);
    }

    [Fact]
    public void Smooth_SecondSample_BlendsByAlpha()
    {
        var ema = new EmaSmoother(alpha: 0.5f);
        ema.Smooth(10f);
        ema.Smooth(20f).Should().Be(15f);
    }
}
