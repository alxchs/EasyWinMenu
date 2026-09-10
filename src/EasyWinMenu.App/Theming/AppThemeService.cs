using System.Windows;
using EasyWinMenu.Core.Models;
using Application = System.Windows.Application;

namespace EasyWinMenu.App.Theming;

internal static class AppThemeService
{
    private static ResourceDictionary? _currentPalette;

    public static void Apply(AppThemeMode mode)
    {
        var effectiveMode = mode == AppThemeMode.System ? DetectSystemMode() : mode;
        var source = effectiveMode == AppThemeMode.Light ? "Theming/Light.xaml" : "Theming/Dark.xaml";
        var palette = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (_currentPalette is not null)
        {
            dictionaries.Remove(_currentPalette);
        }

        dictionaries.Add(palette);
        _currentPalette = palette;
    }

    private static AppThemeMode DetectSystemMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value == 0 ? AppThemeMode.Dark : AppThemeMode.Light;
            }
        }
        catch (System.Security.SecurityException)
        {
        }

        return AppThemeMode.Dark;
    }
}
