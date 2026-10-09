using AmiDiskLab.App.ViewModels;
using AmiDiskLab.Core.Models;
using AmiDiskLab.Infrastructure.Metadata;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AmiDiskLab.App.Views;

public partial class BatchMetadataWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ObservableCollection<string> _log = [];
    private readonly ConcurrentQueue<PendingReview> _pendingReviews = new();
    private int _processed, _saved, _review, _miss, _total;

    public BatchMetadataWindow() { InitializeComponent(); _viewModel = null!; }
    public BatchMetadataWindow(MainWindowViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        LogList.ItemsSource = _log;
        Opened += async (_, _) => await RunAsync();
        Closing += (_, _) => { if (!CloseButton.IsEnabled) _cancellation.Cancel(); };
    }

    private async Task RunAsync()
    {
        var access = _viewModel.ScreenScraperAccess ?? new ScreenScraperAccess("", "");
        var client = new ScreenScraperClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        try
        {
            var info = await client.GetUserInfoAsync(access, _cancellation.Token);
            client.ConfigureLimits(info);
            client.UserInfoUpdated += updated => Dispatcher.UIThread.Post(() => ShowAccount(updated));
            ShowAccount(info);
            var items = _viewModel.SoftwareItems.Where(NeedsMetadata).ToArray();
            _total = items.Length;
            Progress.Maximum = Math.Max(1, _total);
            ProcessedText.Text = $"0 / {items.Length}";
            AddLog(info.HasDailyQuota
                ? $"Started with {info.MaxThreads} parallel ScreenScraper thread(s); {info.RequestsRemaining:N0} requests remaining today."
                : $"Started anonymously with {info.MaxThreads} parallel ScreenScraper thread(s); no personal daily quota reported.");
            await Parallel.ForEachAsync(items, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, info.MaxThreads)
            }, async (item, token) => await ProcessAsync(client, access, item, items.Length, token));
            if (_cancellation.IsCancellationRequested)
            {
                AddLog("Cancelled. Already saved metadata remains available.");
                SummaryText.Text = "Cancelled safely.";
                _viewModel.FinishBatchMetadata(SummaryText.Text);
                return;
            }
            SummaryText.Text = $"Completed: {_saved} saved, {_review} need review, {_miss} without a usable match.";
            _viewModel.FinishBatchMetadata(SummaryText.Text);
        }
        catch (OperationCanceledException) { AddLog("Cancelled. Already saved metadata remains available."); SummaryText.Text = "Cancelled safely."; }
        catch (Exception ex) { AddLog($"ERROR: {ex.Message}"); SummaryText.Text = "The batch stopped because of an error."; }
        finally { CancelButton.IsEnabled = false; ReviewButton.IsEnabled = !_pendingReviews.IsEmpty; CloseButton.IsEnabled = true; }
    }

    private async ValueTask ProcessAsync(ScreenScraperClient client, ScreenScraperAccess access,
        RetroSoftwareItem item, int total, CancellationToken token)
    {
        token = _cancellation.Token;
        if (token.IsCancellationRequested) return;
        await Dispatcher.UIThread.InvokeAsync(() => CurrentText.Text = item.DisplayTitle);
        try
        {
            var matches = await client.SearchAdfAsync(item.FullPath, access,
                _viewModel.ScreenScraperPreferences, token);
            if (token.IsCancellationRequested) return;
            var exact = matches.FirstOrDefault(match => match.Source.Contains("exact SHA-1", StringComparison.Ordinal));
            var automaticMatch = exact ?? (matches.Count == 1 ? matches[0] : null);
            if (automaticMatch is null)
            {
                if (matches.Count > 0) { _pendingReviews.Enqueue(new PendingReview(item, matches)); Interlocked.Increment(ref _review); AddLog($"REVIEW  {item.DisplayTitle} ({matches.Count} candidates)"); }
                else { Interlocked.Increment(ref _miss); AddLog($"NO MATCH {item.DisplayTitle}"); }
            }
            else
            {
                var metadata = await CacheMediaAsync(automaticMatch.Metadata);
                await Dispatcher.UIThread.InvokeAsync(() => _viewModel.SaveBatchMetadata(item, metadata));
                Interlocked.Increment(ref _saved);
                AddLog($"SAVED   {item.DisplayTitle} -> {metadata.Title}");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { AddLog($"CANCEL  {item.DisplayTitle}"); }
        catch (Exception ex) { Interlocked.Increment(ref _miss); AddLog($"ERROR   {item.DisplayTitle}: {ex.Message}"); }
        finally
        {
            Interlocked.Increment(ref _processed);
            await Dispatcher.UIThread.InvokeAsync(UpdateCounters);
        }
    }

    private static bool NeedsMetadata(RetroSoftwareItem item) =>
        string.IsNullOrWhiteSpace(item.Metadata.Title) || string.IsNullOrWhiteSpace(item.Metadata.CoverUrl) ||
        item.Metadata.Category == SoftwareCategory.Unknown;

    private static async Task<SoftwareMetadata> CacheMediaAsync(SoftwareMetadata metadata)
    {
        var cover = await CoverCache.CacheAsync(metadata.CoverUrl);
        var screenshot = await CoverCache.CacheAsync(metadata.ScreenshotUrl);
        var logo = await CoverCache.CacheAsync(metadata.PublisherLogoUrl);
        if (logo is not null) PublisherLogoStore.Set(metadata.Publisher, logo,
            metadata.PublisherLogoUrl?.Contains("monochrome", StringComparison.OrdinalIgnoreCase) != true);
        return metadata with { CoverUrl = cover, ScreenshotUrl = screenshot, PublisherLogoUrl = null };
    }

    private void ShowAccount(ScreenScraperUserInfo info) => AccountText.Text =
        $"Account: {info.UserId ?? "anonymous"} · {info.MaxThreads} thread(s) · " +
        (info.HasDailyQuota
            ? $"{info.RequestsToday:N0} / {info.MaxRequestsPerDay:N0} requests today · " +
              $"{info.FailedRequestsToday:N0} / {info.MaxFailedRequestsPerDay:N0} failed"
            : "daily personal quota not reported");

    private void UpdateCounters()
    {
        ProcessedText.Text = $"{_processed} / {_total}";
        SavedText.Text = _saved.ToString(); ReviewText.Text = _review.ToString(); MissText.Text = _miss.ToString();
        ReviewButton.Content = _review > 0 ? $"Review matches ({_review})" : "Review matches";
        Progress.Value = _processed;
    }

    private void AddLog(string message) => Dispatcher.UIThread.Post(() =>
    {
        _log.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        if (_log.Count > 2000) _log.RemoveAt(0);
        LogList.ScrollIntoView(_log[^1]);
    });

    private async void Review_Click(object? sender, RoutedEventArgs e)
    {
        ReviewButton.IsEnabled = false;
        while (_pendingReviews.TryDequeue(out var pending))
        {
            var picker = new ScreenScraperMatchesWindow(pending.Matches, "Review ScreenScraper matches",
                $"Choose the correct match for {pending.Item.DisplayTitle}.");
            if (!await picker.ShowDialog<bool>(this) || picker.SelectedMatch is null)
            {
                _pendingReviews.Enqueue(pending);
                break;
            }

            var editor = new MetadataWindow(pending.Item, picker.SelectedMatch, preferSuggestion: true,
                metadataLanguage: _viewModel.ScreenScraperPreferences.Language);
            if (!await editor.ShowDialog<bool>(this))
            {
                _pendingReviews.Enqueue(pending);
                break;
            }

            _viewModel.SaveBatchMetadata(pending.Item, editor.Metadata);
            Interlocked.Decrement(ref _review);
            Interlocked.Increment(ref _saved);
            AddLog($"SAVED   {pending.Item.DisplayTitle} -> {editor.Metadata.Title}");
            UpdateCounters();
        }

        ReviewButton.IsEnabled = !_pendingReviews.IsEmpty;
        SummaryText.Text = $"Completed: {_saved} saved, {_review} need review, {_miss} without a usable match.";
        _viewModel.FinishBatchMetadata(SummaryText.Text);
    }
    private void Cancel_Click(object? sender, RoutedEventArgs e) { CancelButton.IsEnabled = false; _cancellation.Cancel(); }
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private sealed record PendingReview(RetroSoftwareItem Item, IReadOnlyList<MetadataSuggestion> Matches);
}
