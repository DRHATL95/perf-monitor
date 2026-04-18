using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PerfMonitor.Windowing.Controls;

public partial class PillControl : UserControl
{
    // Click detection: record the down-position; if the up-position is within
    // this many pixels AND within the time threshold, treat as a click (not
    // a drag). This keeps the outer widget draggable from empty space while
    // making pills themselves distinct click targets.
    private const double ClickMovementThreshold = 4;
    private static readonly TimeSpan ClickTimeThreshold = TimeSpan.FromMilliseconds(400);

    private Point _downPosition;
    private DateTime _downAt;

    public event EventHandler? Click;

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(PillControl),
            new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(PillControl),
            new PropertyMetadata(""));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(PillControl),
            new PropertyMetadata(Brushes.White));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public PillControl()
    {
        InitializeComponent();
        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp   += OnMouseUp;
        Cursor = Cursors.Hand;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _downPosition = e.GetPosition(this);
        _downAt = DateTime.UtcNow;
        // Mark handled so the parent Window's drag handler doesn't grab
        // the pill press as the start of a drag gesture.
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var up = e.GetPosition(this);
        var dx = up.X - _downPosition.X;
        var dy = up.Y - _downPosition.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var elapsed = DateTime.UtcNow - _downAt;

        if (distance <= ClickMovementThreshold && elapsed <= ClickTimeThreshold)
        {
            Click?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
