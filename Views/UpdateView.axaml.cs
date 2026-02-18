using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CheckHash.Views;

public partial class UpdateView : UserControl
{
    public UpdateView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}