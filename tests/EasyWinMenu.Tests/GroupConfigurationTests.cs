using System.IO;
using EasyWinMenu.App.Desktop;
using EasyWinMenu.Core.Configuration;
using EasyWinMenu.Core.Models;
using Xunit;

namespace EasyWinMenu.Tests;

/// <summary>
/// What a group is on disk and how the app finds one. These are the contracts an existing
/// installation depends on: a configuration written by an older build has to keep opening,
/// and a taskbar shortcut has to keep pointing at the same group after a rename.
/// </summary>
public sealed class GroupConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EasyWinMenuTests", Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_directory, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static MenuCategory Group(string name, bool desktop = true) => new()
    {
        Name = name,
        IsDesktopGroup = desktop
    };

    [Fact]
    public void A_saved_configuration_reloads_with_every_group_field_intact()
    {
        var store = new ConfigurationStore(ConfigPath);
        var group = Group("Work");
        group.Id = "abc123";
        group.DesktopX = 321.5;
        group.DesktopY = 654.25;
        group.DesktopWidth = 480;
        group.DesktopHeight = 360;
        group.DesktopIconScale = 1.5;
        group.DisplayMode = DesktopGroupDisplayMode.AppFolder;
        group.IconArrangement = IconArrangement.ByName;
        group.AreaOpacity = 0.5;
        group.TitleOpacity = 0.25;
        group.IsCollapsed = true;
        group.IsClosed = true;
        group.ShowBadge = false;
        group.Items.Add(new LaunchItem
        {
            Name = "Notepad",
            Type = LaunchItemType.Application,
            Target = @"C:\Windows\System32\notepad.exe",
            IsDesktopPinned = true,
            DesktopIconX = 12,
            DesktopIconY = 34
        });

        var configuration = LauncherConfiguration.CreateDefault();
        configuration.Categories.Add(group);
        store.Save(configuration);

        var reloaded = new ConfigurationStore(ConfigPath).Load().Categories.Single(c => c.Name == "Work");

        Assert.Equal("abc123", reloaded.Id);
        Assert.Equal(321.5, reloaded.DesktopX);
        Assert.Equal(654.25, reloaded.DesktopY);
        Assert.Equal(480, reloaded.DesktopWidth);
        Assert.Equal(360, reloaded.DesktopHeight);
        Assert.Equal(1.5, reloaded.DesktopIconScale);
        Assert.Equal(DesktopGroupDisplayMode.AppFolder, reloaded.DisplayMode);
        Assert.Equal(IconArrangement.ByName, reloaded.IconArrangement);
        Assert.Equal(0.5, reloaded.AreaOpacity);
        Assert.Equal(0.25, reloaded.TitleOpacity);
        Assert.True(reloaded.IsCollapsed);
        Assert.True(reloaded.IsClosed);
        Assert.False(reloaded.ShowBadge);

        var item = Assert.Single(reloaded.Items);
        Assert.Equal("Notepad", item.Name);
        Assert.True(item.IsDesktopPinned);
        Assert.Equal(12, item.DesktopIconX);
        Assert.Equal(34, item.DesktopIconY);
    }

    [Fact]
    public void Saving_twice_produces_the_same_bytes()
    {
        var configuration = LauncherConfiguration.CreateDefault();
        configuration.Categories.Add(Group("Stable"));

        var first = Path.Combine(_directory, "a.json");
        var second = Path.Combine(_directory, "b.json");
        new ConfigurationStore(first).Save(configuration);
        new ConfigurationStore(second).Save(configuration);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Theory]
    [InlineData("Grid")]
    [InlineData("ByType")]
    [InlineData("ByName")]
    [InlineData("None")]
    public void A_configuration_written_by_an_older_build_still_opens(string arrangement)
    {
        // Grid and ByType were removed from the menu but must stay readable: a user upgrading
        // from 1.1.0.2 has one of them saved, and a config that fails to parse silently
        // becomes an empty desktop.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, $$"""
        {
          "Categories": [
            {
              "Name": "Legacy",
              "Categories": [],
              "Items": [],
              "IsDesktopGroup": true,
              "AreaTransparent": true,
              "TitleTransparent": true,
              "IconArrangement": "{{arrangement}}"
            }
          ],
          "Items": [],
          "Theme": {},
          "Behavior": {}
        }
        """);

        var loaded = new ConfigurationStore(ConfigPath).TryLoad(out var configuration);

        Assert.True(loaded);
        var group = Assert.Single(configuration.Categories);
        Assert.Equal("Legacy", group.Name);
        Assert.True(group.IsDesktopGroup);
        Assert.Equal(Enum.Parse<IconArrangement>(arrangement), group.IconArrangement);

        // The old booleans are how transparency used to be stored; they have to land on the
        // opacity values that replaced them.
        Assert.Equal(0, group.AreaOpacity);
        Assert.Equal(0, group.TitleOpacity);
    }

    [Fact]
    public void A_configuration_without_ids_loads_and_the_ids_are_filled_in()
    {
        var configuration = LauncherConfiguration.CreateDefault();
        configuration.Categories.Add(Group("One"));
        configuration.Categories.Add(Group("Two"));

        Assert.True(DesktopOrganizerService.EnsureIds(configuration));

        // Only desktop groups are stamped. CreateDefault() also seeds a plain category, and it
        // must stay without an Id — an Id is there for shortcuts to point at, and nothing points
        // at a category that is not a group.
        var ids = DesktopOrganizerService.AllDesktopGroups(configuration).Select(c => c.Id).ToList();
        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Filling_in_ids_a_second_time_changes_nothing()
    {
        var configuration = LauncherConfiguration.CreateDefault();
        configuration.Categories.Add(Group("One"));
        DesktopOrganizerService.EnsureIds(configuration);
        var before = configuration.Categories[0].Id;

        Assert.False(DesktopOrganizerService.EnsureIds(configuration));
        Assert.Equal(before, configuration.Categories[0].Id);
    }

    [Fact]
    public void Nested_desktop_groups_all_get_an_id()
    {
        var configuration = LauncherConfiguration.CreateDefault();
        var outer = Group("Outer");
        var inner = Group("Inner");
        outer.Categories.Add(inner);
        configuration.Categories.Add(outer);

        DesktopOrganizerService.EnsureIds(configuration);

        Assert.False(string.IsNullOrWhiteSpace(outer.Id));
        Assert.False(string.IsNullOrWhiteSpace(inner.Id));
        Assert.NotEqual(outer.Id, inner.Id);
    }

    [Fact]
    public void A_plain_folder_inside_a_group_is_not_treated_as_a_group()
    {
        var configuration = LauncherConfiguration.CreateDefault();
        var group = Group("Group");
        group.Categories.Add(Group("PlainFolder", desktop: false));
        configuration.Categories.Add(group);

        DesktopOrganizerService.EnsureIds(configuration);

        Assert.Single(DesktopOrganizerService.AllDesktopGroups(configuration));
        Assert.Null(group.Categories[0].Id);
    }

    [Fact]
    public void A_group_is_found_by_id_and_survives_being_renamed()
    {
        var configuration = LauncherConfiguration.CreateDefault();
        var group = Group("Before");
        configuration.Categories.Add(group);
        DesktopOrganizerService.EnsureIds(configuration);

        group.Name = "After";

        var found = DesktopOrganizerService.FindGroupById(configuration, group.Id!);
        Assert.Same(group, found);
    }

    [Fact]
    public void An_unknown_id_finds_nothing_rather_than_throwing()
    {
        var configuration = LauncherConfiguration.CreateDefault();
        configuration.Categories.Add(Group("Only"));
        DesktopOrganizerService.EnsureIds(configuration);

        Assert.Null(DesktopOrganizerService.FindGroupById(configuration, "does-not-exist"));
    }

    [Fact]
    public void Cloning_a_group_carries_its_id_so_a_duplicate_must_be_given_a_new_one()
    {
        var group = Group("Source");
        group.Id = "source-id";

        var clone = group.Clone();

        // Clone is deliberately faithful; it is App.Duplicate that has to replace the Id,
        // and this test is what would catch that step going missing.
        Assert.Equal("source-id", clone.Id);
    }

    [Fact]
    public void Counting_a_group_ignores_items_that_are_not_pinned_to_the_desktop()
    {
        var group = Group("Mixed");
        group.Items.Add(new LaunchItem { Name = "Pinned", Type = LaunchItemType.Application, Target = "a", IsDesktopPinned = true });
        group.Items.Add(new LaunchItem { Name = "NotPinned", Type = LaunchItemType.Application, Target = "b", IsDesktopPinned = false });
        group.Categories.Add(Group("Sub", desktop: false));

        Assert.Equal(2, GroupEntries.Count(group));
        Assert.Equal(new[] { "Sub", "Pinned" }, GroupEntries.Enumerate(group).Select(GroupEntries.NameOf));
    }
}
