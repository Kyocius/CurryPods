using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Media.Imaging;

namespace OppoPodsManager.Assets.VisualAssets;

public static class AssetHelper
{
    private static readonly Dictionary<string, BitmapImage?> SharedBitmaps = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapImage? LoadBitmap(string path)
    {
        try
        {
            if (path.StartsWith("avares://OppoPodsManager/", StringComparison.OrdinalIgnoreCase))
            {
                path = "ms-appx:///" + path.Substring("avares://OppoPodsManager/".Length);
            }
            return new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
        }
        catch { return null; }
    }

    public static BitmapImage? LoadSharedBitmap(string path)
    {
        if (SharedBitmaps.TryGetValue(path, out var cached))
            return cached;

        var bitmap = LoadBitmap(path);
        SharedBitmaps[path] = bitmap;
        return bitmap;
    }

    public static bool IsShared(BitmapImage? bmp)
        => bmp != null && SharedBitmaps.ContainsValue(bmp);
}
