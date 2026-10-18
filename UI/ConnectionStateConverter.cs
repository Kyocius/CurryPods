using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OppoPodsManager.UI;

/// <summary>ConnectedDeviceInfo.ConnectionState (0=断连, 1=连接中, 2=已连接) → 状态圆点颜色</summary>
public class ConnectionStateToColorConverter : IValueConverter
{
    public static readonly ConnectionStateToColorConverter Instance = new();

    private static readonly SolidColorBrush Green = new(Color.FromArgb(255, 0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush Red   = new(Color.FromArgb(255, 0xFF, 0x55, 0x55));
    private static readonly SolidColorBrush Gray  = new(Color.FromArgb(255, 0x66, 0x66, 0x66));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var state = value as int? ?? 0;
        return state switch
        {
            2 => Green,   // 已连接
            1 => Gray,    // 连接中
            _ => Red,     // 0 或其他 = 断连
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
