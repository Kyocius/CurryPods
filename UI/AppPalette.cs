using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OppoPodsManager.UI;

/// <summary>
/// 共享画刷调色板：主窗口（状态/电量）与首页视图（ANC/功能）以及小窗共用的内容画刷。
/// </summary>
public static class AppPalette
{
    private static Color C(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    public static bool IsLightTheme { get; set; }

    // 静态（与主题无关）
    public static readonly SolidColorBrush BrushGreen = new(C(0x4C, 0xAF, 0x50));
    public static readonly SolidColorBrush BrushRed = new(C(0xFF, 0x55, 0x55));
    public static readonly SolidColorBrush BrushTransparent = new(Colors.Transparent);
    public static readonly SolidColorBrush BrushAccent = new(C(0x60, 0x90, 0xFF));
    public static readonly SolidColorBrush BrushWhitePure = new(Colors.White);
    public static readonly SolidColorBrush BrushBatteryLow = new(C(0xFF, 0x55, 0x55));
    public static readonly SolidColorBrush BrushBatteryMid = new(C(0xFF, 0xB0, 0x20));
    public static readonly SolidColorBrush BrushBatteryHigh = new(C(0x4C, 0xD9, 0x64));

    // 主题相关原色
    private static readonly SolidColorBrush _brushGrayDark = new(C(0xCC, 0xCC, 0xCC));
    private static readonly SolidColorBrush _brushGrayLight = new(C(0x55, 0x55, 0x55));
    private static readonly SolidColorBrush _brushWhiteDark = new(Colors.White);
    private static readonly SolidColorBrush _brushDark = new(C(0x1A, 0x1A, 0x1A));
    private static readonly SolidColorBrush _brushLightGreenLight = new(C(0x2E, 0x7D, 0x32));
    private static readonly SolidColorBrush _brushLightGreenDark = new(C(0x88, 0xCC, 0x88));
    private static readonly SolidColorBrush _brushLightRedLight = new(C(0xC6, 0x28, 0x28));
    private static readonly SolidColorBrush _brushLightRedDark = new(C(0xFF, 0x88, 0x88));
    private static readonly SolidColorBrush _brushCircleStrokeLight = new(C(0x20, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush _brushCircleStrokeDark = new(C(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush _brushCircleStrokeInactiveLight = new(C(0x0C, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush _brushCircleStrokeInactiveDark = new(C(0x20, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush _brushCircleGrayLight = new(C(0x15, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush _brushCircleGrayDark = new(C(0x10, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush _brushAccentLight = new(C(0x25, 0x63, 0xEB));
    private static readonly SolidColorBrush _brushCardLight = new(C(0xF5, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush _brushCardDark = new(C(0xF5, 0x22, 0x22, 0x22));

    // 主题相关访问器
    public static SolidColorBrush BrushGray => IsLightTheme ? _brushGrayLight : _brushGrayDark;
    public static SolidColorBrush BrushWhite => IsLightTheme ? _brushDark : _brushWhiteDark;
    public static SolidColorBrush BrushLightGreen => IsLightTheme ? _brushLightGreenLight : _brushLightGreenDark;
    public static SolidColorBrush BrushLightRed => IsLightTheme ? _brushLightRedLight : _brushLightRedDark;
    public static SolidColorBrush BrushCircleStroke => IsLightTheme ? _brushCircleStrokeLight : _brushCircleStrokeDark;
    public static SolidColorBrush BrushCircleStrokeInactive =>
        IsLightTheme ? _brushCircleStrokeInactiveLight : _brushCircleStrokeInactiveDark;
    public static SolidColorBrush BrushCard => IsLightTheme ? _brushCardLight : _brushCardDark;
    public static SolidColorBrush CircleGray => IsLightTheme ? _brushCircleGrayLight : _brushCircleGrayDark;
    public static SolidColorBrush BrushCircleGray => CircleGray;
    public static SolidColorBrush Accent => IsLightTheme ? _brushAccentLight : BrushAccent;
    public static SolidColorBrush BrushPrimary => Accent;
}
