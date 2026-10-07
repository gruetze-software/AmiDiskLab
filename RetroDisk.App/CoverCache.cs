using Avalonia.Media.Imaging;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace AmiDiskLab.App;

internal static class CoverCache
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmiDiskLab", "covers");

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AmiDiskLab/0.1 (desktop metadata manager)");
        return client;
    }

    public static async Task<string?> CacheAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Path.IsPathFullyQualified(url))
        {
            var local = Path.GetFullPath(url);
            var hit = local.StartsWith(Path.GetFullPath(Folder) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) && File.Exists(local)
                ? EnsureImageExtension(local) : null;
            Trace.WriteLine($"[MediaCache] Local {(hit is null ? "miss" : "hit")}: {local}");
            return hit;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("screenscraper.fr", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".screenscraper.fr", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("amidisklab-api.c-schaef.workers.dev", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("demozoo.org", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".demozoo.org", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("commons.wikimedia.org", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase))) return null;
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url!))) + ".img";
        var legacyPath = Path.Combine(Folder, name);
        var basePath = Path.ChangeExtension(legacyPath, null);
        foreach (var extension in new[] { ".png", ".jpg", ".webp", ".gif", ".img" })
        {
            var cached = basePath + extension;
            if (File.Exists(cached))
            {
                Trace.WriteLine($"[MediaCache] Cache hit for {Redact(uri)} -> {cached}");
                return EnsureImageExtension(cached);
            }
        }
        HttpResponseMessage response;
        Trace.WriteLine($"[MediaCache] GET {Redact(uri)}");
        try { response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Trace.WriteLine($"[MediaCache] Request failed: {ex.GetType().Name}: {ex.Message}");
            throw new IOException("Image download failed.");
        }
        using (response)
        {
            Trace.WriteLine($"[MediaCache] HTTP {(int)response.StatusCode} {response.ReasonPhrase}; " +
                $"type={response.Content.Headers.ContentType}; length={response.Content.Headers.ContentLength}");
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 5_000_000) return null;
            await using var input = await response.Content.ReadAsStreamAsync();
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                if (memory.Length + read > 5_000_000) return null;
                memory.Write(buffer, 0, read);
            }
            memory.Position = 0;
            try { using var bitmap = new Bitmap(memory); }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                Trace.WriteLine($"[MediaCache] Avalonia could not decode response: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                return null;
            }
            var extension = DetectExtension(memory.ToArray());
            if (extension is null)
            {
                Trace.WriteLine("[MediaCache] Response is not a supported PNG, JPEG, WebP, or GIF image.");
                return null;
            }
            var path = basePath + extension;
            Directory.CreateDirectory(Folder);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, memory.ToArray());
                File.Move(temporary, path, overwrite: true);
                Trace.WriteLine($"[MediaCache] Stored {path}");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }
    }

    private static string Redact(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query)) return uri.ToString();
        var sensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "devid", "devpassword", "ssid", "sspassword" };
        var query = uri.Query.TrimStart('?').Split('&').Select(part =>
        {
            var separator = part.IndexOf('=');
            if (separator < 0) return part;
            var key = Uri.UnescapeDataString(part[..separator]);
            return sensitive.Contains(key) ? part[..(separator + 1)] + "***" : part;
        });
        return uri.GetLeftPart(UriPartial.Path) + "?" + string.Join("&", query);
    }

    private static string? EnsureImageExtension(string path)
    {
        if (!path.EndsWith(".img", StringComparison.OrdinalIgnoreCase)) return path;
        using var stream = File.OpenRead(path);
        var header = new byte[12];
        var count = stream.Read(header, 0, header.Length);
        var extension = DetectExtension(header.AsSpan(0, count));
        if (extension is null) return null;
        var typedPath = Path.ChangeExtension(path, extension);
        if (!File.Exists(typedPath)) File.Copy(path, typedPath);
        return typedPath;
    }

    private static string? DetectExtension(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return ".png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ".jpg";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) &&
            data[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        if (data.Length >= 6 && data[..3].SequenceEqual("GIF"u8)) return ".gif";
        return null;
    }

    public static async Task<Bitmap?> LoadAsync(string? url)
    {
        var path = await CacheAsync(url);
        if (path is null) return null;
        try
        {
            await using var image = File.OpenRead(path);
            return new Bitmap(image);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            Trace.WriteLine($"[MediaCache] Cached image could not be decoded and will be removed: " +
                $"{path}; {ex.GetType().Name}: {ex.Message}");
            File.Delete(path);
            return null;
        }
    }
}
