using AmiDiskLab.Core.Models;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;

namespace AmiDiskLab.App.Views;

public partial class ScreenScraperMatchesWindow : Window
{
    public MetadataSuggestion? SelectedMatch => MatchesBox.SelectedItem as MetadataSuggestion;

    public ScreenScraperMatchesWindow() => InitializeComponent();

    public ScreenScraperMatchesWindow(IReadOnlyList<MetadataSuggestion> matches) : this()
    {
        MatchesBox.ItemsSource = matches;
        if (matches.Count > 0) MatchesBox.SelectedIndex = 0;
    }

    public ScreenScraperMatchesWindow(IReadOnlyList<MetadataSuggestion> matches, string title,
        string explanation) : this(matches)
    {
        Title = title;
        HeadingText.Text = title;
        ExplanationText.Text = explanation;
    }

    private void Choose_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedMatch is not null) Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
