using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.App.Views;
using HashTool.Core.Hashing;
using HashTool.Core.Tests;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>Renders the real window and pages on Avalonia's headless platform.</summary>
public sealed class ViewTests
{
    private const string Sha256Of123456789 = "15E2B0D3C33891EBB0F1EF609EC419420C20E320CE94C65FBC8C3312448EB225";

    [AvaloniaFact]
    public void EveryPageRendersWithoutMissingTranslations()
    {
        var main = AppHost.Get<MainWindowViewModel>();
        var localization = AppHost.Get<LocalizationService>();
        var window = new MainWindow { DataContext = main };
        window.Show();
        try
        {
            foreach (var code in new[] { "en-US", "vi-VN", "ar", "ja-JP", "de-DE", "fil-PH" })
            {
                localization.SelectedLanguage = localization.Find(code);
                Assert.Equal(code is "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, window.FlowDirection);
                foreach (var page in new[] { "Create", "Check", "Settings", "Update", "About", "Developer" })
                {
                    main.NavigateCommand.Execute(page);
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();

                    var missing = window.GetVisualDescendants().OfType<TextBlock>()
                        .Select(t => t.Text)
                        .Where(t => t is not null && t.StartsWith('[') && t.EndsWith(']'))
                        .ToList();
                    Assert.True(missing.Count == 0, $"{code}/{page}: {string.Join(", ", missing)}");
                }
            }
        }
        finally
        {
            localization.SelectedLanguage = localization.AvailableLanguages[0];
            main.NavigateCommand.Execute("Create");
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CreateAndVerify_FolderInputAndChecksumFile()
    {
        using var temp = new TempDirectory();
        temp.File("folder/sub/check.txt", "123456789");
        var create = AppHost.Get<CreateHashViewModel>();
        var check = AppHost.Get<CheckHashViewModel>();
        create.SelectedAlgorithm = HashType.SHA256;

        // 1.x crashed when a folder was dropped.
        await create.AddPathsAsync([Path.Combine(temp.Path, "folder")]);
        await create.RunAllCommand.ExecuteAsync(null);
        var created = Assert.Single(create.Items);
        Assert.Equal(Sha256Of123456789, created.ResultHash, ignoreCase: true);

        var manifest = temp.File("folder/SHA256SUMS", $"{Sha256Of123456789.ToLowerInvariant()}  sub/check.txt\n");
        await check.AddPathsAsync([manifest]);
        await check.RunAllCommand.ExecuteAsync(null);
        var verified = Assert.Single(check.Items);
        Assert.True(verified.IsMatch);
        Assert.Equal(ItemStatus.Valid, verified.Status);
    }
}
