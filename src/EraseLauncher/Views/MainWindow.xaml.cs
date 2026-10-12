using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using EraseLauncher.ViewModels;

namespace EraseLauncher.Views;

public partial class MainWindow : Window
{
    private static readonly Duration IndicatorDuration = new(TimeSpan.FromMilliseconds(360));
    private ToggleButton? _activeNavItem;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => MoveIndicator(animate: false);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && !viewModel.TryRequestClose())
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void NavItem_OnChecked(object sender, RoutedEventArgs e)
    {
        _activeNavItem = (ToggleButton)sender;
        MoveIndicator(animate: IsLoaded);
    }

    private void NavItem_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _activeNavItem))
        {
            MoveIndicator(animate: false);
        }
    }

    /// <summary>Slides the glass pill under the selected navigation item.</summary>
    private void MoveIndicator(bool animate)
    {
        if (_activeNavItem is not { IsVisible: true } item || !NavHost.IsLoaded)
        {
            return;
        }

        var x = item.TranslatePoint(new Point(0, 0), NavHost).X;
        var width = item.ActualWidth;
        if (!animate)
        {
            NavIndicatorShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            NavIndicator.BeginAnimation(WidthProperty, null);
            NavIndicatorShift.X = x;
            NavIndicator.Width = width;
            return;
        }

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 };
        NavIndicatorShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(x, IndicatorDuration) { EasingFunction = ease });
        NavIndicator.BeginAnimation(WidthProperty, new DoubleAnimation(width, IndicatorDuration) { EasingFunction = ease });
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
