using System.Windows;
using System.Windows.Input;

namespace EraseLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
