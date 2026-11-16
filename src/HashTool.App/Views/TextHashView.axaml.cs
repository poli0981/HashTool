using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HashTool.App.Views;

public partial class TextHashView : UserControl
{
    public TextHashView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
