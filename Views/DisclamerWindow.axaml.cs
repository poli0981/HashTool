using Avalonia.Controls;
using Avalonia.Interactivity;
using CheckHash.Services;

namespace CheckHash.Views;

public partial class DisclaimerWindow : Window
{
    public DisclaimerWindow()
    {
        InitializeComponent();
        DataContext = LocalizationService.Instance;
    }

    public bool IsAccepted { get; private set; }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        IsAccepted = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        IsAccepted = false;
        Close();
    }
}