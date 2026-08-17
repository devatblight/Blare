using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Blight.Blare.App.Services;

/// <summary>
/// Resolves a process's own icon to a WinUI-displayable <see cref="BitmapImage"/>,
/// caching by executable path since many sessions share one process (e.g. every
/// browser tab is the same exe). AUMID-based icon resolution for packaged apps
/// is a follow-up — this covers the common Win32-exe case.
/// </summary>
public sealed class IconResolver
{
    private readonly Dictionary<string, BitmapImage?> _cache = new();

    public async Task<BitmapImage?> ResolveAsync(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        if (_cache.TryGetValue(executablePath, out var cached))
        {
            return cached;
        }

        var image = await ExtractAsync(executablePath);
        _cache[executablePath] = image;
        return image;
    }

    private static async Task<BitmapImage?> ExtractAsync(string executablePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;

            var bitmapImage = new BitmapImage();
            using var randomAccessStream = new InMemoryRandomAccessStream();
            await randomAccessStream.WriteAsync(stream.ToArray().AsBuffer());
            randomAccessStream.Seek(0);
            await bitmapImage.SetSourceAsync(randomAccessStream);

            return bitmapImage;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Some processes (protected/system) refuse icon extraction — fall back to no icon.
            return null;
        }
    }
}
