using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AmiDiskLab.App.Views;

public partial class SceneSearchWindow : Window
{
    public string Query => TitleBox.Text?.Trim() ?? string.Empty;

    public SceneSearchWindow() => InitializeComponent();

    public SceneSearchWindow(string title) : this()
    {
        TitleBox.Text = title;
        Opened += (_, _) => TitleBox.Focus();
    }

    private void Search_Click(object? sender, RoutedEventArgs e)
    {
        if (Query.Length > 0) Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
