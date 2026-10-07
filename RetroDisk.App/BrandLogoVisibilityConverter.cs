using AmiDiskLab.Core.Models;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AmiDiskLab.App;

public sealed class BrandLogoVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SoftwareMetadata metadata) return false;
        var mode = parameter as string ?? string.Empty;
        var studio = mode.StartsWith("Studio", StringComparison.Ordinal);
        if (!studio && PublisherLogoStore.Get(metadata.Publisher) is null)
            PublisherLogoStore.ImportLegacy(metadata.Publisher, metadata.PublisherLogoUrl);
        var available = studio
            ? PublisherLogoStore.GetStudio(metadata.Studio) is not null
            : PublisherLogoStore.Get(metadata.Publisher) is not null;
        return mode.EndsWith("Missing", StringComparison.Ordinal) ? !available : available;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
