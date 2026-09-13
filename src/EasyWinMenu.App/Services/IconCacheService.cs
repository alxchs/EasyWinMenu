using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;

namespace EasyWinMenu.App.Services;

/// <summary>
/// Produces the picture for a launch target.
///
/// Icons come from the shell's own image lists rather than from
/// <see cref="DrawingIcon.ExtractAssociatedIcon"/>: that API only ever returns a 32px
/// image, and round-tripping it through an .ico file on disk flattens the alpha channel,
/// which is what used to leave tiles sitting on a black or white square. The cache on
/// disk is therefore PNG, the one format here that keeps per-pixel transparency.
/// </summary>
internal sealed class IconCacheService : IDisposable
{
    private const int ShilJumbo = 0x4;
    private const int ShilExtraLarge = 0x2;
    private const uint ShgfiSysIconIndex = 0x000004000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;

    private static Guid _imageListId = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    private readonly string _cacheDirectory;
    private readonly Dictionary<string, DrawingIcon> _memoryCache = new();
    private readonly Dictionary<string, ImageSource?> _imageSourceCache = new();

    public IconCacheService(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(_cacheDirectory);
    }

    public DrawingIcon? GetIcon(string targetPath)
    {
        if (_memoryCache.TryGetValue(targetPath, out var cached))
        {
            return cached;
        }

        var resolvedPath = ResolveFullPath(targetPath);
        if (resolvedPath is null)
        {
            return null;
        }

        DrawingIcon? icon;
        try
        {
            icon = DrawingIcon.ExtractAssociatedIcon(resolvedPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (icon is not null)
        {
            _memoryCache[targetPath] = icon;
        }

        return icon;
    }

    /// <summary>Frozen WPF image for <paramref name="targetPath"/>, safe to share across windows.</summary>
    public ImageSource? GetImageSource(string targetPath)
    {
        if (_imageSourceCache.TryGetValue(targetPath, out var cached))
        {
            return cached;
        }

        var source = LoadImageSource(targetPath);
        _imageSourceCache[targetPath] = source;
        return source;
    }

    private ImageSource? LoadImageSource(string targetPath)
    {
        var resolvedPath = ResolveFullPath(targetPath);
        if (resolvedPath is null)
        {
            return null;
        }

        var cacheFilePath = Path.Combine(_cacheDirectory, BuildCacheKey(resolvedPath) + ".png");

        var fromDisk = LoadPngFromDisk(cacheFilePath);
        if (fromDisk is not null)
        {
            return fromDisk;
        }

        var extracted = ExtractLargeIcon(resolvedPath) ?? ExtractFallbackIcon(resolvedPath);
        if (extracted is null)
        {
            return null;
        }

        TryPersistPng(extracted, cacheFilePath);
        return extracted;
    }

    /// <summary>The shell's jumbo list gives a 256px image with its alpha intact.</summary>
    private static ImageSource? ExtractLargeIcon(string path)
    {
        var info = default(ShFileInfo);
        var attributes = File.Exists(path) ? 0u : FileAttributeNormal;
        var flags = ShgfiSysIconIndex | (attributes == 0 ? 0 : ShgfiUseFileAttributes);

        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) == IntPtr.Zero)
        {
            return null;
        }

        foreach (var size in new[] { ShilJumbo, ShilExtraLarge })
        {
            if (SHGetImageList(size, ref _imageListId, out var list) != 0 || list is null)
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
                    var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
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

    private static ImageSource? ExtractFallbackIcon(string path)
    {
        try
        {
            using var icon = DrawingIcon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static ImageSource? LoadPngFromDisk(string cacheFilePath)
    {
        if (!File.Exists(cacheFilePath))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.UriSource = new Uri(cacheFilePath);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static void TryPersistPng(ImageSource source, string cacheFilePath)
    {
        if (source is not BitmapSource bitmap)
        {
            return;
        }

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(cacheFilePath);
            encoder.Save(stream);
        }
        catch (IOException)
        {
            // A cache miss next time is the whole cost of failing here.
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

    public void Dispose()
    {
        foreach (var icon in _memoryCache.Values)
        {
            icon.Dispose();
        }

        _memoryCache.Clear();
        _imageSourceCache.Clear();
    }

    private static string BuildCacheKey(string targetPath)
    {
        var lastWriteTicks = File.GetLastWriteTimeUtc(targetPath).Ticks;
        var identity = $"{targetPath.ToLowerInvariant()}|{lastWriteTicks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash);
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
