using System.Windows;
using System.Windows.Controls;

namespace EraseLauncher.Controls;

/// <summary>A translucent surface with a light-catching rim. The look lives in Controls.xaml.</summary>
public sealed class GlassPanel : ContentControl
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(GlassPanel), new PropertyMetadata(new CornerRadius(16)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }
}
