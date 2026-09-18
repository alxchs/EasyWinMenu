using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Text;
using QuickStacks.Localization;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Text;

namespace QuickStacks.UI;

/// <summary>
/// Janela integrada de Ajuda, Guia Rápido e Tabela de Atalhos do QuickStacks.
/// </summary>
public sealed partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();

        ThemeService.Register(RootGrid);

        AppWindow.Resize(new SizeInt32(720, 580));
        try
        {
            AppWindow.SetIcon("Assets\\quickstacks.ico");
        }
        catch
        {
        }

        RefreshTexts();
        LocalizationService.LanguageChanged += RefreshTexts;
        Closed += (_, _) => LocalizationService.LanguageChanged -= RefreshTexts;

        RootGrid.KeyDown += RootGrid_KeyDown;

        SelectTab(0);
    }

    private void RefreshTexts()
    {
        Title = LocalizationService.Get("help.title");
        TabOverviewButton.Content = LocalizationService.Get("help.tab.overview");
        TabShortcutsButton.Content = LocalizationService.Get("help.tab.shortcuts");
        TabDesktopGroupsButton.Content = LocalizationService.Get("help.tab.desktopGroups");
        TabAboutButton.Content = LocalizationService.Get("help.tab.about");

        StatusTierText.Text = $"Modo: {(FeatureTier.IsFull ? "Full" : "Lite")}";
        StatusLanguageText.Text = $"Idioma: {LocalizationService.CurrentLanguage}";
        StatusThemeText.Text = $"Tema: {ThemeService.CurrentTheme}";
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void TabOverview_Click(object sender, RoutedEventArgs e) => SelectTab(0);

    private void TabShortcuts_Click(object sender, RoutedEventArgs e) => SelectTab(1);

    private void TabDesktopGroups_Click(object sender, RoutedEventArgs e) => SelectTab(2);

    private void TabAbout_Click(object sender, RoutedEventArgs e) => SelectTab(3);

    private void SelectTab(int tabIndex)
    {
        PanelOverview.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelShortcuts.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelDesktopGroups.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelAbout.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

        TabOverviewButton.FontWeight = tabIndex == 0 ? FontWeights.SemiBold : FontWeights.Normal;
        TabShortcutsButton.FontWeight = tabIndex == 1 ? FontWeights.SemiBold : FontWeights.Normal;
        TabDesktopGroupsButton.FontWeight = tabIndex == 2 ? FontWeights.SemiBold : FontWeights.Normal;
        TabAboutButton.FontWeight = tabIndex == 3 ? FontWeights.SemiBold : FontWeights.Normal;
    }
}

