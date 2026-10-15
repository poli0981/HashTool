using Avalonia.Controls;
using HashTool.App.Services;
using HashTool.App.ViewModels;

namespace HashTool.App.Views;

public partial class MainWindow : Window
{
    private bool _canClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    private LocalizationService L => LocalizationService.Instance;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_canClose) return;

        if (DataContext is MainWindowViewModel vm)
            if (vm.CheckHashVM.IsChecking || vm.CreateHashVM.IsComputing)
            {
                e.Cancel = true;

                var result = await MessageBoxHelper.ShowConfirmationAsync(
                    L["Msg_ConfirmExit_Title"],
                    L["Msg_ConfirmExit_Content"],
                    L["Btn_Yes"],
                    L["Btn_No"],
                    MessageBoxIcon.Warning);

                if (result)
                {
                    _canClose = true;
                    vm.CheckHashVM.Dispose();
                    vm.CreateHashVM.Dispose();
                    Close();
                }
            }
    }
}
