using TelemetryDash.ViewModels;

namespace TelemetryDash.Tests;

public class ChannelViewModelTests
{
    [Fact]
    public void Setting_CurrentValue_ShouldUpdateSaturation()
    {
        var vm = new ChannelViewModel
        {
            ChannelId = "TEMP_A1",
            MinValue = 0,
            MaxValue = 100
        };

        vm.CurrentValue = 50;
        Assert.Equal(50.0, vm.SaturationPercent);
    }

    [Fact]
    public void SaturationBrush_ShouldBeGreen_WhenBelow70Percent()
    {
        var vm = new ChannelViewModel { MinValue = 0, MaxValue = 100 };
        vm.CurrentValue = 60;

        // Green color: #22C55E -> RGB(34, 197, 94)
        var brush = vm.SaturationBrush;
        Assert.Equal(0x22, brush.Color.R);
        Assert.Equal(0xC5, brush.Color.G);
        Assert.Equal(0x5E, brush.Color.B);
    }

    [Fact]
    public void SaturationBrush_ShouldBeAmber_WhenBetween70And90Percent()
    {
        var vm = new ChannelViewModel { MinValue = 0, MaxValue = 100 };
        vm.CurrentValue = 80;

        // Amber color: #F59E0B
        var brush = vm.SaturationBrush;
        Assert.Equal(0xF5, brush.Color.R);
        Assert.Equal(0x9E, brush.Color.G);
        Assert.Equal(0x0B, brush.Color.B);
    }

    [Fact]
    public void SaturationBrush_ShouldBeRed_WhenAbove90Percent()
    {
        var vm = new ChannelViewModel { MinValue = 0, MaxValue = 100 };
        vm.CurrentValue = 95;

        // Red color: #EF4444
        var brush = vm.SaturationBrush;
        Assert.Equal(0xEF, brush.Color.R);
        Assert.Equal(0x44, brush.Color.G);
        Assert.Equal(0x44, brush.Color.B);
    }

    [Fact]
    public void SparklineValues_ShouldAccumulate()
    {
        var vm = new ChannelViewModel { MinValue = 0, MaxValue = 100 };

        vm.CurrentValue = 10;
        vm.CurrentValue = 20;
        vm.CurrentValue = 30;

        Assert.Equal(3, vm.SparklineValues.Count);
        Assert.Equal(10, vm.SparklineValues[0]);
        Assert.Equal(20, vm.SparklineValues[1]);
        Assert.Equal(30, vm.SparklineValues[2]);
    }

    [Fact]
    public void SparklineValues_ShouldNotExceedMaxPoints()
    {
        var vm = new ChannelViewModel { MinValue = 0, MaxValue = 100 };

        for (int i = 0; i < 70; i++)
            vm.CurrentValue = i;

        Assert.Equal(60, vm.SparklineValues.Count); // MaxSparklinePoints = 60
    }

    [Fact]
    public void PropertyChanged_ShouldFire_OnCurrentValueChange()
    {
        var vm = new ChannelViewModel { MinValue = 0, MaxValue = 100 };
        var changedProperties = new List<string>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        vm.CurrentValue = 42;

        Assert.Contains("CurrentValue", changedProperties);
        Assert.Contains("SaturationPercent", changedProperties);
        Assert.Contains("SaturationBrush", changedProperties);
    }
}
