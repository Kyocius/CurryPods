using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;

namespace OppoPodsManager.Assets.VisualAssets;

// 管理自定义背景文件、缩略图缓存和背景图像处理。
public sealed class BackgroundImageManager : IDisposable
{
    private const int MaximumHistoryCount = 10;
    private static readonly string ManagedDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OppoPodsManager",
        "Background");

    private static readonly string CacheDirectory = Path.Combine(ManagedDirectory, "Cache");

    private readonly Dictionary<string, ImageSource> _backgroundCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageSource> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);

    // 构造包含文件版本、目标宽度和模糊参数的稳定缓存键。
    public string BuildCacheKey(string path, int targetWidth, int blur)
        => $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|w={targetWidth}|b={blur}";

    // 判断背景文件是否仍然存在，避免界面层直接访问文件系统。
    public bool IsAvailable(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    // 获取或创建指定尺寸和模糊程度的背景图片源。
    public ImageSource GetOrCreateBitmap(string path, int targetWidth, int blur, string cacheKey)
    {
        if (_backgroundCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var bitmap = LoadBitmap(path, targetWidth, blur, cacheKey);
        _backgroundCache[cacheKey] = bitmap;
        return bitmap;
    }

    // 获取或创建背景缩略图。
    public ImageSource GetOrCreateThumbnail(string path)
    {
        var key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|thumb=128";
        if (_thumbnailCache.TryGetValue(key, out var cached))
            return cached;

        RemoveThumbnail(path);
        var thumbnail = new BitmapImage(new Uri(path))
        {
            DecodePixelWidth = 128
        };
        _thumbnailCache[key] = thumbnail;
        return thumbnail;
    }

    // 清理不再使用的背景图片，并保留当前缓存项。
    public void ClearBackgroundCache(string? keepKey)
    {
        foreach (var key in _backgroundCache.Keys.ToList())
        {
            if (key == keepKey)
                continue;

            _backgroundCache.Remove(key);
        }
    }

    // 清理全部背景缩略图缓存。
    public void ClearThumbnailCache()
    {
        _thumbnailCache.Clear();
    }

    // 移除指定文件对应的缩略图缓存。
    public void RemoveThumbnail(string path)
    {
        foreach (var key in _thumbnailCache.Keys.ToList())
        {
            if (!key.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase))
                continue;

            _thumbnailCache.Remove(key);
        }
    }

    // 将用户选择的背景复制到应用管理目录，避免源文件被移动后失效。
    public string CopyToManagedFile(string sourcePath)
    {
        try
        {
            Directory.CreateDirectory(ManagedDirectory);
            var extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".png";

            var destination = Path.Combine(ManagedDirectory, $"{Guid.NewGuid():N}{extension}");
            File.Copy(sourcePath, destination, overwrite: true);
            return destination;
        }
        catch
        {
            return sourcePath;
        }
    }

    // 添加背景历史并清理超出数量限制的托管文件和缩略图。
    public IReadOnlyList<string> AddToHistory(IReadOnlyList<string> history, string sourcePath)
    {
        var managedPath = CopyToManagedFile(sourcePath);
        var updated = history
            .Where(path => !string.Equals(path, managedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        updated.Insert(0, managedPath);

        while (updated.Count > MaximumHistoryCount)
        {
            var removed = updated[^1];
            updated.RemoveAt(updated.Count - 1);
            RemoveThumbnail(removed);
            DeleteManagedFile(removed);
        }

        return updated;
    }

    // 删除背景历史并清理对应的缓存和托管文件。
    public IReadOnlyList<string> RemoveFromHistory(IReadOnlyList<string> history, string path)
    {
        var updated = history
            .Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (updated.Count == history.Count)
            return updated;

        RemoveThumbnail(path);
        DeleteManagedFile(path);
        return updated;
    }

    // 删除应用管理目录内的背景文件，阻止误删用户原始文件。
    public void DeleteManagedFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var managedRoot = Path.GetFullPath(ManagedDirectory);
            if (fullPath.StartsWith(managedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        ClearBackgroundCache(null);
        ClearThumbnailCache();
    }

    private static ImageSource LoadBitmap(string path, int targetWidth, int blur, string cacheKey)
    {
        if (blur <= 0)
        {
            return new BitmapImage(new Uri(path))
            {
                DecodePixelWidth = targetWidth
            };
        }

        return LoadBlurredBitmap(path, targetWidth, blur, cacheKey);
    }

    private static ImageSource LoadBlurredBitmap(string path, int targetWidth, int blur, string cacheKey)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var safeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cacheKey)))[..16];
            var cachedPath = Path.Combine(CacheDirectory, $"bg_{safeHash}.png");

            if (File.Exists(cachedPath))
            {
                return new BitmapImage(new Uri(cachedPath));
            }

            using var codec = SKCodec.Create(path);
            if (codec is null)
                return new BitmapImage(new Uri(path));

            var sourceInfo = codec.Info;
            var scale = targetWidth / (double)Math.Max(1, sourceInfo.Width);
            var targetHeight = Math.Max(1, (int)Math.Round(sourceInfo.Height * scale));
            var scaledSize = codec.GetScaledDimensions((float)Math.Min(1.0, scale));
            if (scaledSize.Width <= 0 || scaledSize.Height <= 0)
                scaledSize = new SKSizeI(targetWidth, targetHeight);

            var decodeInfo = new SKImageInfo(scaledSize.Width, scaledSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var source = new SKBitmap(decodeInfo);
            var result = codec.GetPixels(decodeInfo, source.GetPixels());
            if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
                return new BitmapImage(new Uri(path));

            var outputInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(outputInfo);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            using var blurFilter = SKImageFilter.CreateBlur(blur, blur);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                ImageFilter = blurFilter
            };
            using var sourceImage = SKImage.FromBitmap(source);
            canvas.DrawImage(sourceImage, new SKRect(0, 0, targetWidth, targetHeight), paint);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 85);
            using (var fileStream = File.Create(cachedPath))
            {
                data.SaveTo(fileStream);
            }

            return new BitmapImage(new Uri(cachedPath));
        }
        catch
        {
            return new BitmapImage(new Uri(path));
        }
    }
}
