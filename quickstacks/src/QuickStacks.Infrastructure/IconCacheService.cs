using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using QuickStacks.Domain;

namespace QuickStacks.Infrastructure;

/// <summary>
/// Extrai icones em alta definicao (Jumbo 256px / ExtraLarge 48px) via Shell Image Lists e salva em PNG no disco (Fase 17).
/// Preserva o canal alfa per-pixel sem distorcoes.
/// </summary>
public sealed class IconCacheService : IIconCacheService, IDisposable
{
    private const int ShilJumbo = 0x4;
    private const int ShilExtraLarge = 0x2;
    private const int ShilLarge = 0x0;
    private const uint ShgfiSysIconIndex = 0x000004000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;

    private static readonly Guid ImageListGuid = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, string?> _memoryCache = new(StringComparer.OrdinalIgnoreCase);

    public IconCacheService(string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickStacks",
            "IconCache");

        try
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
        catch (IOException)
        {
        }
    }

    public string? GetIconPath(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return null;
        }

        if (_memoryCache.TryGetValue(targetPath, out var cached))
        {
            return cached;
        }

        var resolved = LoadOrExtractPng(targetPath);
        _memoryCache[targetPath] = resolved;
        return resolved;
    }

    private string? LoadOrExtractPng(string targetPath)
    {
        var resolvedPath = ResolveFullPath(targetPath);
        if (resolvedPath is null)
        {
            return null;
        }

        var cacheKey = BuildCacheKey(resolvedPath);
        var cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.png");

        if (File.Exists(cacheFilePath))
        {
            return cacheFilePath;
        }

        using var bitmap = ExtractLargeIconBitmap(resolvedPath) ?? ExtractFallbackIconBitmap(resolvedPath);
        if (bitmap is null)
        {
            return null;
        }

        try
        {
            var tempFile = Path.Combine(_cacheDirectory, $"{Guid.NewGuid():N}.tmp");
            bitmap.Save(tempFile, ImageFormat.Png);
            File.Move(tempFile, cacheFilePath, overwrite: true);
            return cacheFilePath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Bitmap? ExtractLargeIconBitmap(string path)
    {
        var info = default(ShFileInfo);
        var attributes = File.Exists(path) ? 0u : FileAttributeNormal;
        var flags = ShgfiSysIconIndex | (attributes == 0 ? 0 : ShgfiUseFileAttributes);

        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) == IntPtr.Zero)
        {
            return null;
        }

        foreach (var size in new[] { ShilJumbo, ShilExtraLarge, ShilLarge })
        {
            var listGuid = ImageListGuid;
            if (SHGetImageList(size, ref listGuid, out var list) != 0 || list is null)
            {
                continue;
            }

            try
            {
                if (list.GetIcon(info.iIcon, 0x00000001, out var hIcon) != 0 || hIcon == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    using var icon = (Icon)Icon.FromHandle(hIcon).Clone();
                    return icon.ToBitmap();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
            catch (COMException)
            {
            }
            finally
            {
                Marshal.ReleaseComObject(list);
            }
        }

        return null;
    }

    private static Bitmap? ExtractFallbackIconBitmap(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon?.ToBitmap();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ResolveFullPath(string targetPath)
    {
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            return Path.GetFullPath(targetPath);
        }

        if (Path.IsPathRooted(targetPath))
        {
            return null;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, targetPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string BuildCacheKey(string targetPath)
    {
        var lastWriteTicks = File.Exists(targetPath) ? File.GetLastWriteTimeUtc(targetPath).Ticks : 0;
        var identity = $"{targetPath.ToLowerInvariant()}|{lastWriteTicks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        _memoryCache.Clear();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList? ppv);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
