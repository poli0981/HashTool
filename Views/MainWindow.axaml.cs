using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CheckHash.Services;
using CheckHash.ViewModels;

namespace CheckHash.Views;

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