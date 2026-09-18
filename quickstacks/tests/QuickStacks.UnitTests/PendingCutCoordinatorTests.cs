using QuickStacks.Domain;
using Xunit;

namespace QuickStacks.UnitTests;

public class PendingCutCoordinatorTests
{
    [Fact]
    public void RegisterCut_AddsItem_AndSignalsStateChanged()
    {
        var coordinator = new PendingCutCoordinator();
        var signaled = false;
        coordinator.StateChanged += (_, _) => signaled = true;

        coordinator.RegisterCut("item-1", @"C:\tools\app.exe", "group-1");

        Assert.True(signaled);
        Assert.True(coordinator.IsCutPending("item-1"));
        var active = coordinator.GetActiveCuts();
        Assert.Single(active);
        Assert.Equal("item-1", active[0].ItemId);
        Assert.Equal(@"C:\tools\app.exe", active[0].Path);
        Assert.Equal("group-1", active[0].SourceFolderId);
    }

    [Fact]
    public void IsCutPending_ReturnsFalseForUnknownItem()
    {
        var coordinator = new PendingCutCoordinator();
        coordinator.RegisterCut("item-1", @"C:\tools\app.exe", "group-1");

        Assert.False(coordinator.IsCutPending("item-2"));
    }

    [Fact]
    public void Clear_RemovesAllItems_AndSignalsStateChanged()
    {
        var coordinator = new PendingCutCoordinator();
        coordinator.RegisterCut("item-1", @"C:\tools\app.exe", "group-1");

        var signaled = false;
        coordinator.StateChanged += (_, _) => signaled = true;

        coordinator.Clear();

        Assert.True(signaled);
        Assert.Empty(coordinator.GetActiveCuts());
        Assert.False(coordinator.IsCutPending("item-1"));
    }

    [Fact]
    public void Clear_WhenAlreadyEmpty_DoesNotSignal()
    {
        var coordinator = new PendingCutCoordinator();
        var signaled = false;
        coordinator.StateChanged += (_, _) => signaled = true;

        coordinator.Clear();

        Assert.False(signaled);
    }

    [Fact]
    public void ConsumeMatching_RemovesMatchedItemsCaseInsensitively_AndSignals()
    {
        var coordinator = new PendingCutCoordinator();
        coordinator.RegisterCut("item-1", @"C:\Tools\App.exe", "group-1");
        coordinator.RegisterCut("item-2", @"C:\Docs\file.txt", "group-1");

        var signaled = false;
        coordinator.StateChanged += (_, _) => signaled = true;

        var consumed = coordinator.ConsumeMatching([@"c:\tools\app.exe"]);

        Assert.True(signaled);
        Assert.Single(consumed);
        Assert.Equal("item-1", consumed[0].ItemId);

        var remaining = coordinator.GetActiveCuts();
        Assert.Single(remaining);
        Assert.Equal("item-2", remaining[0].ItemId);
    }

    [Fact]
    public void DetectMissingFromDisk_RemovesItemsWhereDiskPredicateReturnsFalse()
    {
        var coordinator = new PendingCutCoordinator();
        coordinator.RegisterCut("item-1", @"C:\Tools\App.exe", "group-1");
        coordinator.RegisterCut("item-2", @"C:\Docs\file.txt", "group-1");

        var signaled = false;
        coordinator.StateChanged += (_, _) => signaled = true;

        // Simula que App.exe ainda existe, mas file.txt foi movido para fora
        var missing = coordinator.DetectMissingFromDisk(path => path.Contains("App.exe", StringComparison.OrdinalIgnoreCase));

        Assert.True(signaled);
        Assert.Single(missing);
        Assert.Equal("item-2", missing[0].ItemId);

        var remaining = coordinator.GetActiveCuts();
        Assert.Single(remaining);
        Assert.Equal("item-1", remaining[0].ItemId);
    }
}
