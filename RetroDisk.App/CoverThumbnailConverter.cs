using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using AmiDiskLab.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AmiDiskLab.App;

public sealed class CoverThumbnailConverter : IValueConverter
{
    private readonly Dictionary<string, Bitmap?> _images = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SoftwareMetadata metadata) return null;
        var kind = parameter as string;
        var path = string.Equals(kind, "PublisherLogo", StringComparison.Ordinal)
            ? PublisherLogoStore.Get(metadata.Publisher)?.Path ??
              ImportAndReturnLegacy(metadata.Publisher, metadata.PublisherLogoUrl)
            : string.Equals(kind, "StudioLogo", StringComparison.Ordinal)
                ? PublisherLogoStore.GetStudio(metadata.Studio)?.Path
                : ValidLocalPath(metadata.CoverUrl) ?? ValidLocalPath(metadata.ScreenshotUrl);
        if (path is null) return null;
        if (_images.TryGetValue(path, out var cached)) return cached;
        try
        {
            using var stream = File.OpenRead(path);
            return _images[path] = new Bitmap(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        { return _images[path] = null; }
    }

    private static string? ImportAndReturnLegacy(string? publisher, string? path)
    {
        var valid = ValidLocalPath(path);
        if (valid is not null) PublisherLogoStore.ImportLegacy(publisher, valid);
        return valid;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string? ValidLocalPath(string? path) =>
        path is not null && Path.IsPathFullyQualified(path) && File.Exists(path) ? path : null;
}
