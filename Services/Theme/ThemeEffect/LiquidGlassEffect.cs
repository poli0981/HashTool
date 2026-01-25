using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Runtime.InteropServices; // Để check OS
using Avalonia.Controls.ApplicationLifetimes;

namespace CheckHash.Services.ThemeEffects;

public static class LiquidGlassEffect
{
    // Hàm áp dụng hiệu ứng cho toàn bộ UI (tìm MainWindow)
    public static void ApplyToMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is Window window)
            {
                Apply(window);
            }
        }
    }

    // Hàm gỡ bỏ hiệu ứng cho toàn bộ UI
    public static void DisableForMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is Window window)
            {
                Disable(window);
            }
        }
    }

    // Hàm áp dụng hiệu ứng cụ thể cho 1 window
    public static void Apply(Window window)
    {
        if (window == null) return;

        // 1. Cấu hình chung bắt buộc cho hiệu ứng kính
        window.Background = Brushes.Transparent; 
        
        // 2. Xử lý riêng cho từng Hệ điều hành
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ApplyMacOS(window);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ApplyWindows(window);
        }
        else
        {
            window.TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
        }
        
        // 3. Tinh chỉnh UI cho Liquid Glass (Bo góc, Toolbar nhỏ hơn)
        // Lưu ý: Việc thay đổi Margin/CornerRadius của SplitView Pane nên được thực hiện qua Style hoặc Binding
        // Nhưng ở đây ta có thể inject Resource hoặc Style class nếu cần.
        // Tạm thời ta đã xử lý Margin="0,10" trong MainWindow.axaml để tạo khoảng hở cho toolbar.
    }

    // Hàm gỡ bỏ hiệu ứng (về mặc định - Solid)
    public static void Disable(Window window)
    {
        if (window == null) return;

        // Tắt hiệu ứng trong suốt
        window.TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
        
        // Reset background về null hoặc để ThemeService tự set lại màu Solid
        // window.Background = null; 
    }

    // --- LOGIC CHI TIẾT ---

    private static void ApplyMacOS(Window window)
    {
        // macOS hỗ trợ AcrylicBlur rất đẹp (hiệu ứng mờ phía sau cửa sổ)
        window.TransparencyLevelHint = new[] 
        { 
            WindowTransparencyLevel.AcrylicBlur, 
            WindowTransparencyLevel.Blur 
        };

        // Để hiệu ứng kính tràn lên cả thanh tiêu đề (TitleBar) tạo cảm giác liền mạch
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
    }

    private static void ApplyWindows(Window window)
    {
        // Windows 11 hỗ trợ Mica (vật liệu mới, lấy màu từ hình nền desktop) và Acrylic
        // Ưu tiên Mica (nhẹ hơn, đẹp hơn, native Win 11) -> Nếu không có thì Acrylic -> Blur
        window.TransparencyLevelHint = new[] 
        { 
            WindowTransparencyLevel.Mica, 
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur
        };

        // Trên Windows cũng nên mở rộng vùng client để hiệu ứng đẹp hơn
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
    }
}
