using System.Windows;
using EasyWinMenu.App.Desktop;
using Xunit;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace EasyWinMenu.Tests;

/// <summary>
/// The geometry that rescues a group when the set of monitors is not the one it was placed on.
/// docs/OVERVIEW.md recorded that this maths had been checked by a throwaway console program
/// "não commitado neste repositório" — these are those assertions, committed.
/// </summary>
public sealed class MonitorPlacementTests
{
    private static readonly Rect Primary = new(0, 0, 1920, 1040);
    private static readonly Rect Secondary = new(1920, 0, 2560, 1400);
    private static readonly IReadOnlyList<Rect> TwoMonitors = new[] { Primary, Secondary };
    private static readonly IReadOnlyList<Rect> OneMonitor = new[] { Primary };

    [Fact]
    public void A_group_fully_inside_a_work_area_is_reachable()
    {
        Assert.True(MonitorPlacement.IsReachable(new Rect(100, 100, 400, 300), OneMonitor));
    }

    [Fact]
    public void A_group_pushed_past_the_bottom_edge_is_not_reachable()
    {
        // Its title strip is below the work area, so there is nothing left to grab.
        Assert.False(MonitorPlacement.IsReachable(new Rect(100, 1100, 400, 300), OneMonitor));
    }

    [Fact]
    public void A_group_touching_only_a_corner_is_not_reachable()
    {
        // One pixel of overlap is not a grabbable title strip; this is the case that
        // separates "reachable" from "technically intersects".
        Assert.False(MonitorPlacement.IsReachable(new Rect(1919, 1039, 400, 300), OneMonitor));
    }

    [Fact]
    public void A_group_half_off_the_right_edge_stays_reachable()
    {
        Assert.True(MonitorPlacement.IsReachable(new Rect(1700, 200, 400, 300), OneMonitor));
    }

    [Fact]
    public void Owner_is_the_work_area_the_group_overlaps_most()
    {
        // Straddles the seam, but most of its area sits on the secondary monitor.
        Assert.Equal(1, MonitorPlacement.IndexOfOwner(new Rect(1800, 100, 600, 400), TwoMonitors));
        Assert.Equal(0, MonitorPlacement.IndexOfOwner(new Rect(1500, 100, 600, 400), TwoMonitors));
    }

    [Fact]
    public void Owner_falls_back_to_the_nearest_work_area_when_nothing_overlaps()
    {
        // Far above every monitor: no overlap at all, so distance decides.
        Assert.Equal(0, MonitorPlacement.IndexOfOwner(new Rect(200, -4000, 300, 200), TwoMonitors));
        Assert.Equal(1, MonitorPlacement.IndexOfOwner(new Rect(3000, -4000, 300, 200), TwoMonitors));
    }

    [Fact]
    public void Owner_is_minus_one_when_there_are_no_work_areas()
    {
        Assert.Equal(-1, MonitorPlacement.IndexOfOwner(new Rect(0, 0, 100, 100), Array.Empty<Rect>()));
    }

    [Fact]
    public void There_is_no_adjacent_monitor_when_only_one_exists()
    {
        Assert.Null(MonitorPlacement.AdjacentIndex(0, OneMonitor, +1));
        Assert.Null(MonitorPlacement.AdjacentIndex(0, OneMonitor, -1));
    }

    [Fact]
    public void Adjacent_monitor_wraps_around_at_both_ends()
    {
        Assert.Equal(1, MonitorPlacement.AdjacentIndex(0, TwoMonitors, +1));
        Assert.Equal(0, MonitorPlacement.AdjacentIndex(1, TwoMonitors, +1));
        Assert.Equal(1, MonitorPlacement.AdjacentIndex(0, TwoMonitors, -1));
    }

    [Fact]
    public void Adjacent_monitor_is_null_for_an_index_that_does_not_exist()
    {
        Assert.Null(MonitorPlacement.AdjacentIndex(7, TwoMonitors, +1));
        Assert.Null(MonitorPlacement.AdjacentIndex(-1, TwoMonitors, +1));
    }

    [Fact]
    public void Mapping_keeps_a_corner_group_in_the_same_corner()
    {
        var topLeft = MonitorPlacement.MapBetween(new Rect(0, 0, 400, 300), Primary, Secondary);
        Assert.Equal(Secondary.X, topLeft.X, 3);
        Assert.Equal(Secondary.Y, topLeft.Y, 3);

        var bottomRight = MonitorPlacement.MapBetween(new Rect(1520, 740, 400, 300), Primary, Secondary);
        Assert.Equal(Secondary.Right - 400, bottomRight.X, 3);
        Assert.Equal(Secondary.Bottom - 300, bottomRight.Y, 3);
    }

    [Fact]
    public void Mapping_keeps_a_centred_group_centred()
    {
        var group = new Rect((Primary.Width - 400) / 2, (Primary.Height - 300) / 2, 400, 300);
        var mapped = MonitorPlacement.MapBetween(group, Primary, Secondary);

        Assert.Equal(Secondary.X + ((Secondary.Width - 400) / 2), mapped.X, 3);
        Assert.Equal(Secondary.Y + ((Secondary.Height - 300) / 2), mapped.Y, 3);
    }

    [Fact]
    public void Mapping_back_and_forth_returns_to_the_starting_point()
    {
        // The reversibility property: a round trip must not drift, or a group would walk
        // across the desktop every time a monitor is unplugged and plugged back in.
        foreach (var start in new[] { new Rect(0, 0, 400, 300), new Rect(760, 370, 400, 300), new Rect(1520, 740, 400, 300) })
        {
            var there = MonitorPlacement.MapBetween(start, Primary, Secondary);
            var back = MonitorPlacement.MapBetween(new Rect(there.X, there.Y, start.Width, start.Height), Secondary, Primary);

            Assert.Equal(start.X, back.X, 3);
            Assert.Equal(start.Y, back.Y, 3);
        }
    }

    [Fact]
    public void Mapping_a_group_wider_than_the_destination_pins_it_to_the_left_edge()
    {
        var wide = new Rect(0, 0, 4000, 300);
        var mapped = MonitorPlacement.MapBetween(wide, Secondary, Primary);

        Assert.Equal(Primary.X, mapped.X, 3);
    }

    [Fact]
    public void Clamping_pulls_a_group_back_inside_from_every_side()
    {
        Assert.Equal(new Point(0, 0), MonitorPlacement.ClampInto(new Point(-500, -500), new Size(400, 300), Primary));
        Assert.Equal(new Point(1520, 740), MonitorPlacement.ClampInto(new Point(9000, 9000), new Size(400, 300), Primary));
    }

    [Fact]
    public void Clamping_something_larger_than_the_area_parks_it_at_the_origin()
    {
        Assert.Equal(new Point(Primary.X, Primary.Y), MonitorPlacement.ClampInto(new Point(300, 300), new Size(5000, 5000), Primary));
    }

    [Fact]
    public void Stacking_is_avoided_by_nudging_each_later_group_along()
    {
        var taken = new List<Point>();
        var size = new Size(400, 300);

        var first = MonitorPlacement.AvoidStacking(new Point(100, 100), size, Primary, taken);
        var second = MonitorPlacement.AvoidStacking(new Point(100, 100), size, Primary, taken);
        var third = MonitorPlacement.AvoidStacking(new Point(100, 100), size, Primary, taken);

        Assert.Equal(new Point(100, 100), first);
        Assert.NotEqual(first, second);
        Assert.NotEqual(second, third);
        Assert.All(new[] { first, second, third }, point =>
        {
            Assert.InRange(point.X, Primary.X, Primary.Right - size.Width);
            Assert.InRange(point.Y, Primary.Y, Primary.Bottom - size.Height);
        });
    }

    [Fact]
    public void Stacking_avoidance_leaves_a_free_position_untouched()
    {
        var taken = new List<Point> { new(800, 600) };
        Assert.Equal(new Point(100, 100), MonitorPlacement.AvoidStacking(new Point(100, 100), new Size(400, 300), Primary, taken));
    }
}
