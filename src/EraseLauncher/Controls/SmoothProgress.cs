using System.Windows;
using System.Windows.Media.Animation;

namespace EraseLauncher.Controls;

/// <summary>
/// Animates the width of a progress fill towards <c>Fraction</c> of its parent's width,
/// so progress glides between reports instead of jumping. Width is animated rather than
/// a scale transform so the rounded ends keep their shape.
/// </summary>
public static class SmoothProgress
{
    private static readonly Duration GlideDuration = new(TimeSpan.FromMilliseconds(450));

    public static readonly DependencyProperty FractionProperty = DependencyProperty.RegisterAttached(
        "Fraction", typeof(double), typeof(SmoothProgress), new PropertyMetadata(0d, OnFractionChanged));

    private static readonly DependencyProperty CurrentProperty = DependencyProperty.RegisterAttached(
        "Current", typeof(double), typeof(SmoothProgress), new PropertyMetadata(0d, OnCurrentChanged));

    public static double GetFraction(DependencyObject element) => (double)element.GetValue(FractionProperty);
    public static void SetFraction(DependencyObject element, double value) => element.SetValue(FractionProperty, value);

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        // Re-subscribing is idempotent and keeps the fill correct if the track resizes.
        element.Loaded -= OnElementLoaded;
        element.Loaded += OnElementLoaded;
        if (element.Parent is FrameworkElement parent)
        {
            parent.SizeChanged -= OnParentSizeChanged;
            parent.SizeChanged += OnParentSizeChanged;
        }

        var target = Math.Clamp((double)e.NewValue, 0, 1);
        var current = (double)element.GetValue(CurrentProperty);
        if (target < current)
        {
            // A new run starts from zero; do not animate backwards.
            element.BeginAnimation(CurrentProperty, null);
            element.SetValue(CurrentProperty, target);
            return;
        }

        element.BeginAnimation(CurrentProperty, new DoubleAnimation(target, GlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnCurrentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => Apply((FrameworkElement)d);

    private static void OnElementLoaded(object sender, RoutedEventArgs e) => Apply((FrameworkElement)sender);

    private static void OnParentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Panel panel)
        {
            return;
        }

        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            if (child.ReadLocalValue(FractionProperty) != DependencyProperty.UnsetValue)
            {
                Apply(child);
            }
        }
    }

    private static void Apply(FrameworkElement element)
    {
        if (element.Parent is FrameworkElement parent)
        {
            element.Width = Math.Max(0, parent.ActualWidth * (double)element.GetValue(CurrentProperty));
        }
    }
}
