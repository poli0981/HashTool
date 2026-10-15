using System;
using Avalonia;
using Avalonia.Controls;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.Core.Configuration;

namespace HashTool.App.Views;

public partial class MainWindow : Window
{
    private bool _confirmedClose;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private async void OnOpened(object? sender, EventArgs e)
    {
        RestorePlacement();
        if (ViewModel is not { } vm) return;
        vm.ActivateWindowRequested += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        };
        await vm.OnOpenedAsync();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        SavePlacement();
        if (_confirmedClose || ViewModel is not { IsBusy: true } vm) return;

        e.Cancel = true;
        var localization = vm.Loc;
        var dialogs = AppHost.Get<IDialogService>();
        if (await dialogs.ConfirmAsync(localization["Msg_ConfirmExit_Title"], localization["Msg_ConfirmExit_Content"], DialogIcon.Warning))
        {
            vm.Create.CancelCommand.Execute(null);
            vm.Check.CancelCommand.Execute(null);
            _confirmedClose = true;
            Close();
        }
    }

    private void RestorePlacement()
    {
        if (ViewModel?.Settings.Window is not { } placement) return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var area = screen?.WorkingArea;
        Width = Math.Min(placement.Width, area is { } a ? a.Width / screen!.Scaling : placement.Width);
        Height = Math.Min(placement.Height, area is { } b ? b.Height / screen!.Scaling : placement.Height);
        if (placement is { X: { } x, Y: { } y } && area is { } bounds && bounds.Contains(new PixelPoint(x + 40, y + 40)))
            Position = new PixelPoint(x, y);
        if (placement.IsMaximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        if (ViewModel is null || WindowState == WindowState.Minimized) return;
        var maximized = WindowState == WindowState.Maximized;
        var previous = ViewModel.Settings.Window;
        AppHost.Get<SettingsService>().Update(s => s.Window = new WindowPlacement
        {
            Width = maximized && previous is not null ? previous.Width : Bounds.Width,
            Height = maximized && previous is not null ? previous.Height : Bounds.Height,
            X = maximized ? previous?.X : Position.X,
            Y = maximized ? previous?.Y : Position.Y,
            IsMaximized = maximized
        });
    }
}
