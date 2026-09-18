using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using Xunit;

namespace QuickStacks.IntegrationTests;

public class ShellContextMenuServiceTests
{
    [Fact]
    public void ShellContextMenuService_ImplementsInterface()
    {
        IShellContextMenuService service = new ShellContextMenuService();
        Assert.NotNull(service);
    }

    [Fact]
    public void TryShow_WithZeroHandle_ReturnsFalseWithoutThrowing()
    {
        var service = new ShellContextMenuService();
        var result = service.TryShow(IntPtr.Zero, @"C:\Windows\notepad.exe", 100, 100);
        Assert.False(result);
    }

    [Fact]
    public void TryShow_WithEmptyPath_ReturnsFalseWithoutThrowing()
    {
        var service = new ShellContextMenuService();
        var result = service.TryShow(new IntPtr(12345), string.Empty, 100, 100);
        Assert.False(result);
    }

    [Fact]
    public void TryShow_WithNonExistentPath_ReturnsFalseWithoutThrowing()
    {
        var service = new ShellContextMenuService();
        var nonExistent = @"C:\NonExistent_QuickStacks_Directory_12345\file.xyz";
        var result = service.TryShow(new IntPtr(12345), nonExistent, 100, 100);
        Assert.False(result);
    }
}
