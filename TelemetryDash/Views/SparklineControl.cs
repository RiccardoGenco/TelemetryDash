using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TelemetryDash.Views;

public class SparklineControl : Canvas
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(ObservableCollection<double>),
            typeof(SparklineControl), new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty StrokeColorProperty =
        DependencyProperty.Register(nameof(StrokeColor), typeof(Brush),
            typeof(SparklineControl), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))));

    public ObservableCollection<double>? Values
    {
        get => (ObservableCollection<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush StrokeColor
    {
        get => (Brush)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SparklineControl)d;

        if (e.OldValue is ObservableCollection<double> oldCollection)
            oldCollection.CollectionChanged -= control.OnCollectionChanged;

        if (e.NewValue is ObservableCollection<double> newCollection)
            newCollection.CollectionChanged += control.OnCollectionChanged;

        control.Redraw();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        Children.Clear();

        if (Values is null || Values.Count < 2)
            return;

        var width = ActualWidth > 0 ? ActualWidth : 200;
        var height = ActualHeight > 0 ? ActualHeight : 40;

        var min = Values.Min();
        var max = Values.Max();
        var range = max - min;
        if (range < 0.001) range = 1;

        var polyline = new Polyline
        {
            Stroke = StrokeColor,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
        };

        var stepX = width / (Values.Count - 1);

        for (int i = 0; i < Values.Count; i++)
        {
            var x = i * stepX;
            var y = height - ((Values[i] - min) / range * (height - 4)) - 2;
            polyline.Points.Add(new Point(x, y));
        }

        Children.Add(polyline);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Redraw();
    }
}
