using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace OppoPodsManager.UI.Controls;

public class WrapPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(WrapPanel),
            new PropertyMetadata(Orientation.Horizontal, OnOrientationChanged));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WrapPanel panel)
            panel.InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var curLineSize = new Size();
        var panelSize = new Size();
        var isHorizontal = Orientation == Orientation.Horizontal;

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            child.Measure(availableSize);
            var sz = child.DesiredSize;

            if (isHorizontal)
            {
                if (curLineSize.Width + sz.Width > availableSize.Width && curLineSize.Width > 0)
                {
                    panelSize.Width = Math.Max(curLineSize.Width, panelSize.Width);
                    panelSize.Height += curLineSize.Height;
                    curLineSize = sz;
                }
                else
                {
                    curLineSize.Width += sz.Width;
                    curLineSize.Height = Math.Max(sz.Height, curLineSize.Height);
                }
            }
            else
            {
                if (curLineSize.Height + sz.Height > availableSize.Height && curLineSize.Height > 0)
                {
                    panelSize.Height = Math.Max(curLineSize.Height, panelSize.Height);
                    panelSize.Width += curLineSize.Width;
                    curLineSize = sz;
                }
                else
                {
                    curLineSize.Height += sz.Height;
                    curLineSize.Width = Math.Max(sz.Width, curLineSize.Width);
                }
            }
        }

        if (isHorizontal)
        {
            panelSize.Width = Math.Max(curLineSize.Width, panelSize.Width);
            panelSize.Height += curLineSize.Height;
        }
        else
        {
            panelSize.Height = Math.Max(curLineSize.Height, panelSize.Height);
            panelSize.Width += curLineSize.Width;
        }

        return panelSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var isHorizontal = Orientation == Orientation.Horizontal;
        var curLineStart = 0;
        var curOffset = 0.0;
        var accumulatedLength = 0.0;
        var maxBreadth = 0.0;

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (child.Visibility == Visibility.Collapsed)
                continue;

            var sz = child.DesiredSize;
            var itemLength = isHorizontal ? sz.Width : sz.Height;
            var itemBreadth = isHorizontal ? sz.Height : sz.Width;
            var lineLength = isHorizontal ? finalSize.Width : finalSize.Height;

            if (curOffset + itemLength > lineLength && curOffset > 0)
            {
                ArrangeLine(curLineStart, i, accumulatedLength, maxBreadth, isHorizontal);
                accumulatedLength += maxBreadth;
                curOffset = itemLength;
                maxBreadth = itemBreadth;
                curLineStart = i;
            }
            else
            {
                curOffset += itemLength;
                maxBreadth = Math.Max(maxBreadth, itemBreadth);
            }
        }

        if (curLineStart < Children.Count)
        {
            ArrangeLine(curLineStart, Children.Count, accumulatedLength, maxBreadth, isHorizontal);
        }

        return finalSize;
    }

    private void ArrangeLine(int start, int end, double accumulatedLength, double maxBreadth, bool isHorizontal)
    {
        double currentOffset = 0;
        for (int i = start; i < end; i++)
        {
            var child = Children[i];
            if (child.Visibility == Visibility.Collapsed)
                continue;

            var sz = child.DesiredSize;
            if (isHorizontal)
            {
                child.Arrange(new Rect(currentOffset, accumulatedLength, sz.Width, maxBreadth));
                currentOffset += sz.Width;
            }
            else
            {
                child.Arrange(new Rect(accumulatedLength, currentOffset, maxBreadth, sz.Height));
                currentOffset += sz.Height;
            }
        }
    }
}
