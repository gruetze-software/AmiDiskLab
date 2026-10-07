using AmiDiskLab.Core.Models;
using AmiDiskLab.Infrastructure.Metadata;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace AmiDiskLab.App.Views;

public partial class ConfigurationWindow : Window
{
    private static readonly int[] RamChoices = [1, 2, 4, 8];
    private static readonly string[] Languages = ["de", "en", "fr", "es", "it"];
    private static readonly string[] Regions = ["eu", "us", "jp", "wor"];
    public AmigaTargetSystem TargetSystem { get; private set; } = AmigaTargetSystem.Default;
    public ScreenScraperAccess? Access { get; private set; }
    public ScreenScraperPreferences Preferences { get; private set; } = ScreenScraperPreferences.Default;

    public ConfigurationWindow() => InitializeComponent();

    public ConfigurationWindow(AmigaTargetSystem target, ScreenScraperAccess? access,
        ScreenScraperPreferences preferences,
        bool showScreenScraper = false) : this()
    {
        TargetSystem = target;
        Access = access;
        Preferences = preferences;
        KickstartBox.SelectedIndex = (int)target.Kickstart;
        RamBox.SelectedIndex = Array.IndexOf(RamChoices, target.RamMiB);
        UserIdBox.Text = access?.UserId;
        UserPasswordBox.Text = access?.UserPassword;
        LanguageBox.SelectedIndex = Array.IndexOf(Languages, preferences.Language);
        RegionBox.SelectedIndex = Array.IndexOf(Regions, preferences.Region);
        SettingsTabs.SelectedIndex = showScreenScraper ? 1 : 0;
        if (!OperatingSystem.IsWindows())
            CredentialHint.Text = "A personal ScreenScraper account is not required. Signing in uses your account limits and member benefits. On this system, credentials are used for the current session only and are not stored.";
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (KickstartBox.SelectedIndex < 0 || RamBox.SelectedIndex < 0 ||
            LanguageBox.SelectedIndex < 0 || RegionBox.SelectedIndex < 0) return;
        var userId = UserIdBox.Text?.Trim() ?? "";
        var userPassword = UserPasswordBox.Text ?? "";
        TargetSystem = new AmigaTargetSystem((KickstartVersion)KickstartBox.SelectedIndex,
            RamChoices[RamBox.SelectedIndex]);
        Access = new ScreenScraperAccess(userId, userPassword);
        Preferences = new ScreenScraperPreferences(Languages[LanguageBox.SelectedIndex],
            Regions[RegionBox.SelectedIndex]);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
