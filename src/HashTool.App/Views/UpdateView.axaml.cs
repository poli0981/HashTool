using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HashTool.App.Views;

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