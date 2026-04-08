using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FixedAvaloniaAuthApp;

public partial class ErrorWindow : Window
{
    public ErrorWindow(string message)
    {
        InitializeComponent();
        ErrorMessage.Text = message;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
