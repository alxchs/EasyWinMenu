using QuickStacks.Domain;
using Xunit;

namespace QuickStacks.UnitTests;

public class MonitorPlacementTests
{
    private static readonly MonitorRect Primary = new(0, 0, 1920, 1080);
    private static readonly MonitorRect Secondary = new(1920, 0, 1920, 1080);

    [Fact]
    public void IsReachable_TrueWhenTitleStripOverlapsWorkArea()
    {
        var group = new MonitorRect(100, 100, 260, 220);

        Assert.True(MonitorPlacement.IsReachable(group, [Primary]));
    }

    [Fact]
    public void IsReachable_FalseWhenGroupIsOffscreen()
    {
        var group = new MonitorRect(5000, 5000, 260, 220);

        Assert.False(MonitorPlacement.IsReachable(group, [Primary]));
    }

    [Fact]
    public void IsReachable_FalseWhenNoWorkAreas()
    {
        var group = new MonitorRect(100, 100, 260, 220);

        Assert.False(MonitorPlacement.IsReachable(group, []));
    }

    [Fact]
    public void IndexOfOwner_ReturnsMonitorWithMostOverlap()
    {
        var group = new MonitorRect(1850, 100, 260, 220);

        Assert.Equal(1, MonitorPlacement.IndexOfOwner(group, [Primary, Secondary]));
    }

    [Fact]
    public void IndexOfOwner_FallsBackToNearestWhenFullyOffscreen()
    {
        var group = new MonitorRect(-500, 100, 260, 220);

        Assert.Equal(0, MonitorPlacement.IndexOfOwner(group, [Primary, Secondary]));
    }

    [Fact]
    public void AdjacentIndex_WrapsAroundAtTheEnds()
    {
        Assert.Equal(0, MonitorPlacement.AdjacentIndex(1, [Primary, Secondary], 1));
        Assert.Equal(1, MonitorPlacement.AdjacentIndex(0, [Primary, Secondary], -1));
    }

    [Fact]
    public void AdjacentIndex_NullWithOnlyOneMonitor()
    {
        Assert.Null(MonitorPlacement.AdjacentIndex(0, [Primary], 1));
    }

    [Fact]
    public void MapBetween_KeepsRelativePositionAcrossMonitors()
    {
        var group = new MonitorRect(0, 0, 260, 220); // canto superior esquerdo do monitor 1

        var (x, y) = MonitorPlacement.MapBetween(group, Primary, Secondary);

        Assert.Equal(Secondary.X, x);
        Assert.Equal(Secondary.Y, y);
    }

    [Fact]
    public void ClampInto_KeepsRectangleInsideArea()
    {
        var (x, y) = MonitorPlacement.ClampInto(-100, -100, 260, 220, Primary);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void AvoidStacking_MovesAwayFromTakenPosition()
    {
        var taken = new List<(double X, double Y)> { (100, 100) };

        var (x, y) = MonitorPlacement.AvoidStacking(100, 100, 260, 220, Primary, taken);

        Assert.False(Math.Abs(x - 100) < 4 && Math.Abs(y - 100) < 4);
        Assert.Equal(2, taken.Count);
    }
}
