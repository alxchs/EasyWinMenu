using QuickStacks.Infrastructure;
using Xunit;

namespace QuickStacks.IntegrationTests;

public class IconCacheServiceTests : IDisposable
{
    private readonly string _testCacheDir;
    private readonly IconCacheService _service;

    public IconCacheServiceTests()
    {
        _testCacheDir = Path.Combine(Path.GetTempPath(), "QuickStacksTests_IconCache_" + Guid.NewGuid().ToString("N"));
        _service = new IconCacheService(_testCacheDir);
    }

    [Fact]
    public void GetIconPath_WithNullOrEmpty_ReturnsNull()
    {
        Assert.Null(_service.GetIconPath(null));
        Assert.Null(_service.GetIconPath("   "));
    }

    [Fact]
    public void GetIconPath_WithNonExistentPath_ReturnsNull()
    {
        var result = _service.GetIconPath(@"C:\non_existent_path_12345.xyz");
        Assert.Null(result);
    }

    [Fact]
    public void GetIconPath_WithRealExecutable_GeneratesValidPng()
    {
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.True(File.Exists(cmdPath));

        var iconPath = _service.GetIconPath(cmdPath);

        Assert.NotNull(iconPath);
        Assert.True(File.Exists(iconPath));
        Assert.EndsWith(".png", iconPath, StringComparison.OrdinalIgnoreCase);

        // Verifica assinatura PNG (89 50 4E 47 0D 0A 1A 0A)
        var bytes = File.ReadAllBytes(iconPath);
        Assert.True(bytes.Length > 8);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]); // P
        Assert.Equal(0x4E, bytes[2]); // N
        Assert.Equal(0x47, bytes[3]); // G

        // Segunda chamada deve devolver o mesmo caminho (cache)
        var secondCall = _service.GetIconPath(cmdPath);
        Assert.Equal(iconPath, secondCall);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_testCacheDir))
        {
            try
            {
                Directory.Delete(_testCacheDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

