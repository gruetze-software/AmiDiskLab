using AmiDiskLab.Core.Models;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AmiDiskLab.App.Views;

public partial class TargetSystemWindow : Window
{
    private static readonly int[] RamChoices = [1, 2, 4, 8];

    public AmigaTargetSystem TargetSystem { get; private set; }

    public TargetSystemWindow() : this(AmigaTargetSystem.Default) { }

    public TargetSystemWindow(AmigaTargetSystem current)
    {
        InitializeComponent();
        TargetSystem = current;
        KickstartBox.SelectedIndex = (int)current.Kickstart;
        RamBox.SelectedIndex = System.Array.IndexOf(RamChoices, current.RamMiB);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (KickstartBox.SelectedIndex < 0 || RamBox.SelectedIndex < 0) return;
        TargetSystem = new AmigaTargetSystem((KickstartVersion)KickstartBox.SelectedIndex,
            RamChoices[RamBox.SelectedIndex]);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
