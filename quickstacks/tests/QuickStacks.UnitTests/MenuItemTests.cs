using QuickStacks.Domain;
using Xunit;

namespace QuickStacks.UnitTests;

public class MenuItemTests
{
    [Fact]
    public void CreateFolder_SetsFolderType()
    {
        var folder = MenuItem.CreateFolder("Desenvolvimento", parentId: null, sortOrder: 0);

        Assert.Equal(MenuItemType.Folder, folder.Type);
        Assert.True(folder.IsFolder);
        Assert.Null(folder.ParentId);
    }

    [Fact]
    public void CreateShortcut_WithFolderType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MenuItem.CreateShortcut("Invalido", parentId: null, MenuItemType.Folder, "C:\\", sortOrder: 0));
    }

    [Fact]
    public void CreateShortcut_SetsPathAndParent()
    {
        var shortcut = MenuItem.CreateShortcut("Bloco de Notas", "parent-id", MenuItemType.Executable, "notepad.exe", sortOrder: 3);

        Assert.Equal("notepad.exe", shortcut.Path);
        Assert.Equal("parent-id", shortcut.ParentId);
        Assert.Equal(3, shortcut.SortOrder);
        Assert.False(shortcut.IsFolder);
    }

    [Fact]
    public void RegisterLaunch_IncrementsCountAndStampsLastUsed()
    {
        var item = MenuItem.CreateShortcut("Calculadora", null, MenuItemType.Executable, "calc.exe", 0);

        item.RegisterLaunch();

        Assert.Equal(1, item.LaunchCount);
        Assert.NotNull(item.LastUsedUtc);
    }
}
