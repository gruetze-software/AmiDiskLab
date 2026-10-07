using AmiDiskLab.Infrastructure.Metadata;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace AmiDiskLab.App;

public sealed class ScreenScraperAccessStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmiDiskLab", "screenscraper-credentials.dat");

    public ScreenScraperAccess? Load()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            if (!File.Exists(_path)) return null;
            var encrypted = File.ReadAllBytes(_path);
            if (encrypted.Length is 0 or > 32_768) return null;
            var plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            try
            {
                var access = JsonSerializer.Deserialize<ScreenScraperAccess>(plaintext);
                return access;
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            CryptographicException or JsonException or ArgumentException or PlatformNotSupportedException)
        {
            Trace.WriteLine("Could not load encrypted ScreenScraper credentials.");
            return null;
        }
    }

    public void Save(ScreenScraperAccess? access)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (access is null || string.IsNullOrWhiteSpace(access.UserId) &&
            string.IsNullOrEmpty(access.UserPassword))
        {
            if (File.Exists(_path)) File.Delete(_path);
            return;
        }
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(access);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
