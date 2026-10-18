using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace OppoPodsManager.UI.Controls;

public class UniformGrid : Panel
{
    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(int), typeof(UniformGrid),
            new PropertyMetadata(0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty RowsProperty =
        DependencyProperty.Register(nameof(Rows), typeof(int), typeof(UniformGrid),
            new PropertyMetadata(0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty FirstColumnProperty =
        DependencyProperty.Register(nameof(FirstColumn), typeof(int), typeof(UniformGrid),
            new PropertyMetadata(0, OnLayoutPropertyChanged));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public int FirstColumn
    {
        get => (int)GetValue(FirstColumnProperty);
        set => SetValue(FirstColumnProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UniformGrid grid)
            grid.InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateComputedRowsColumns(out var rows, out var columns);

        var childAvailableSize = new Size(
            double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width / columns,
            double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : availableSize.Height / rows);

        double maxChildWidth = 0.0;
        double maxChildHeight = 0.0;

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            child.Measure(childAvailableSize);
            maxChildWidth = Math.Max(maxChildWidth, child.DesiredSize.Width);
            maxChildHeight = Math.Max(maxChildHeight, child.DesiredSize.Height);
        }

        return new Size(maxChildWidth * columns, maxChildHeight * rows);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateComputedRowsColumns(out var rows, out var columns);

        var childWidth = finalSize.Width / columns;
        var childHeight = finalSize.Height / rows;
        var cellBounds = new Rect(0, 0, childWidth, childHeight);

        var column = FirstColumn;
        var row = 0;

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            cellBounds.X = column * childWidth;
            cellBounds.Y = row * childHeight;
            child.Arrange(cellBounds);

            column++;
            if (column >= columns)
            {
                column = 0;
                row++;
            }
        }

        return finalSize;
    }

    private void UpdateComputedRowsColumns(out int rows, out int columns)
    {
        rows = Rows;
        columns = Columns;

        if (FirstColumn >= columns && columns > 0)
            FirstColumn = 0;

        var visibleChildCount = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Collapsed)
                visibleChildCount++;
        }

        if (rows == 0 && columns == 0)
        {
            columns = (int)Math.Ceiling(Math.Sqrt(visibleChildCount));
            rows = (int)Math.Ceiling((double)visibleChildCount / Math.Max(1, columns));
        }
        else if (rows == 0)
        {
            rows = (int)Math.Ceiling((double)(visibleChildCount + FirstColumn) / columns);
        }
        else if (columns == 0)
        {
            columns = (int)Math.Ceiling((double)visibleChildCount / rows);
        }

        if (rows < 1) rows = 1;
        if (columns < 1) columns = 1;
    }
}
