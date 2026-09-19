using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace QuickStacks.UI;

/// <summary>
/// Carregamento seguro e em cache de imagens PNG de ícones para componentes XAML/WinUI 3.
/// Evita passar caminhos do sistema de arquivos diretamente para BitmapImage(Uri) ou
/// Source="{x:Bind IconPath}", que lança E_INVALIDARG (0x80070057) e derruba o processo no WinUI 3.
/// </summary>
public static class IconImageLoader
{
    private static readonly ConcurrentDictionary<string, BitmapImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapImage? GetBitmap(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        return _cache.GetOrAdd(filePath, path =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.SetSource(stream.AsRandomAccessStream());
                return bitmap;
            }
            catch (Exception ex)
            {
                App.Log($"[IconImageLoader] Falha ao carregar ícone de '{path}': {ex.Message}");
                return null;
            }
        });
    }

    public static void ClearCache() => _cache.Clear();
}

