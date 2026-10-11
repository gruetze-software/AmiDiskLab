using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using AmiDiskLab.App.ViewModels;
using AmiDiskLab.App.Views;
using AmiDiskLab.Infrastructure.Archives;
using AmiDiskLab.Infrastructure.Conversion;
using AmiDiskLab.Infrastructure.FileSystem;
using System;
using System.ComponentModel;
using System.Linq;

namespace AmiDiskLab.App
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            ThemeManager.Apply(UiPreferencesStore.Load().Theme);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(
                        new RetroSoftwareScanner(),
                        new ArchiveToAdfConverter(new LhaArchiveExtractor(), new AmigaDiskImageWriter())),
                };
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window } &&
                window.DataContext is MainWindowViewModel viewModel)
            {
                viewModel.PropertyChanged += MenuStateChanged;
                window.PropertyChanged += WindowMenuStateChanged;
                window.Closed += (_, _) =>
                {
                    viewModel.PropertyChanged -= MenuStateChanged;
                    window.PropertyChanged -= WindowMenuStateChanged;
                };
                UpdateMenuState();
            }
            base.OnFrameworkInitializationCompleted();
        }

        private MainWindow? MenuWindow =>
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;

        private void MenuStateChanged(object? sender, PropertyChangedEventArgs e) => UpdateMenuState();

        private void WindowMenuStateChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == InputElement.IsEnabledProperty) UpdateMenuState();
        }

        private void MenuNeedsUpdate(object? sender, EventArgs e) => UpdateMenuState();

        private bool CanUseNativeMenu =>
            MenuWindow is { IsEnabled: true, DataContext: MainWindowViewModel { IsBusy: false } } window &&
            !window.OwnedWindows.Any(owned => owned.IsDialog);

        private void UpdateMenuState()
        {
            // Avalonia appends the standard macOS Services, Hide and Quit actions.
            if (NativeMenu.GetMenu(this) is { } menu)
                foreach (var item in menu.Items.Take(4).OfType<NativeMenuItem>())
                    item.IsEnabled = CanUseNativeMenu;
        }

        private async void AboutMenu_Click(object? sender, EventArgs e)
        {
            if (CanUseNativeMenu && MenuWindow is { } window) await window.ShowAboutAsync();
        }

        private async void SettingsMenu_Click(object? sender, EventArgs e)
        {
            if (CanUseNativeMenu && MenuWindow is { } window) await window.ShowSettingsAsync();
        }

        private async void RefreshMenu_Click(object? sender, EventArgs e)
        {
            if (CanUseNativeMenu && MenuWindow is { } window) await window.RefreshCollectionAsync();
        }
        private void DisableAvaloniaDataAnnotationValidation()
        {
            // Get an array of plugins to remove
            var dataValidationPluginsToRemove =
                    BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

            // remove each entry found
            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }
    }
}
