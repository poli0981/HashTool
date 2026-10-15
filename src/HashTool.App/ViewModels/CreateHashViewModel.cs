using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashTool.App.Infrastructure;
using HashTool.App.Models;
using HashTool.App.Services;
using HashTool.Core.Checksums;
using HashTool.Core.Files;
using HashTool.Core.Hashing;
using HashTool.Core.Processing;
using Microsoft.Extensions.Logging;

namespace HashTool.App.ViewModels;

/// <summary>The "Generate Hash" page.</summary>
public sealed partial class CreateHashViewModel : FileListViewModelBase
{
    private const int ErrorDiskFull = 0x70;
    private const int ErrorHandleDiskFull = 0x27;

    [ObservableProperty] private HashType _selectedAlgorithm;

    public CreateHashViewModel(FileListServices services) : base(services)
    {
        _selectedAlgorithm = services.Settings.Current.CreateAlgorithm;
    }

    partial void OnSelectedAlgorithmChanged(HashType value) => Services.Settings.Update(s => s.CreateAlgorithm = value);

    protected override ActivationVerb ElevationVerb => ActivationVerb.Create;

    protected override Task<IReadOnlyList<FileItem>> CreateItemsAsync(IReadOnlyList<FileEntry> files, ImportReport report)
    {
        var algorithm = SelectedAlgorithm;
        IReadOnlyList<FileItem> items = files.Select(f => new FileItem(f.FullPath, f.Length, algorithm)).ToList();
        return Task.FromResult(items);
    }

    protected override HashJob? CreateJob(FileItem item) =>
        item.IsRemoved ? null : new HashJob(item.FilePath, item.Size, [item.Algorithm], item.Cancellation?.Token ?? default);

    protected override void ApplyResult(FileItem item, HashJobResult result)
    {
        if (result.Status == HashJobStatus.Succeeded && result.Results is [var hash, ..])
        {
            item.SetResult(hash.Algorithm, hash.ToHex(Settings.HashLetterCase));
            item.State = FileStatus.Success;
            item.SetStatus(ItemStatus.Done);
            Logger.LogDebug("{Algorithm} {File}", item.Algorithm, item.FileName);
            return;
        }

        ApplyFailure(item, result);
        if (result.Error is not null) Logger.LogWarning(result.Error, "Hashing {File} failed", item.FileName);
    }

    private List<FileItem> ItemsWithResults() => AllItems.Where(i => i.ResultAlgorithm is not null && !string.IsNullOrEmpty(i.ResultHash)).ToList();

    private static HashType AlgorithmOf(FileItem item) => item.ResultAlgorithm ?? item.Algorithm;

    // ------------------------------------------------------------------ export

    [RelayCommand]
    private async Task SaveHashFileAsync(FileItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.ResultHash)) return;
        var extension = HashAlgorithms.FileExtension(AlgorithmOf(item));
        var choice = new FileTypeChoice(AlgorithmOf(item).ToString(), $"*.{extension}");
        var target = await Services.Storage.SaveFileAsync(L["Dialog_SaveHash"], $"{item.FileName}.{extension}", [choice]);
        if (target is not { } t) return;

        var line = ChecksumWriter.Write([new ChecksumLine(item.FileName, AlgorithmOf(item), item.ResultHash, item.Size)], ExportFormat.Gnu, Settings.HashLetterCase);
        if (await WriteAsync(t.Path, line))
        {
            item.SetStatus(ItemStatus.Saved);
            Logger.LogInformation("Saved {File}", Path.GetFileName(t.Path));
        }
    }

    [RelayCommand]
    private async Task ExportChecksumFileAsync()
    {
        var items = ItemsWithResults();
        if (items.Count == 0)
        {
            await Services.Dialogs.ShowAsync(L["Msg_Result_Title"], L["Msg_NothingToExport"], DialogIcon.Information);
            return;
        }

        var algorithms = items.Select(AlgorithmOf).Distinct().ToList();
        HashType? single = algorithms.Count == 1 ? algorithms[0] : null;
        var types = new List<FileTypeChoice>
        {
            new(L["FileType_Gnu"], $"*.{ChecksumWriter.SuggestedExtension(ExportFormat.Gnu, single)}", ExportFormat.Gnu),
            new(L["FileType_Bsd"], "*.txt", ExportFormat.Bsd)
        };
        if (single == HashType.CRC32) types.Add(new FileTypeChoice(L["FileType_Sfv"], "*.sfv", ExportFormat.Sfv));
        types.Add(new FileTypeChoice(L["FileType_Json"], "*.json", ExportFormat.Json));
        types.Add(new FileTypeChoice(L["FileType_Csv"], "*.csv", ExportFormat.Csv));

        var preferred = types.FirstOrDefault(t => (ExportFormat)t.Tag! == Settings.DefaultExportFormat) ?? types[0];
        types.Remove(preferred);
        types.Insert(0, preferred);
        var format = (ExportFormat)preferred.Tag!;
        var suggested = format == ExportFormat.Gnu && single is { } s
            ? $"{HashAlgorithms.BsdTag(s)}SUMS.{ChecksumWriter.SuggestedExtension(ExportFormat.Gnu, s)}"
            : $"checksums.{ChecksumWriter.SuggestedExtension(format, single)}";

        var target = await Services.Storage.SaveFileAsync(L["Dialog_SaveChecksumFile"], suggested, types);
        if (target is not { } t) return;
        format = t.Type?.Tag is ExportFormat chosen ? chosen : FormatFromExtension(t.Path, format);
        if (format == ExportFormat.Sfv && single != HashType.CRC32) format = ExportFormat.Bsd;
        if (format == ExportFormat.Gnu && single is null) format = ExportFormat.Bsd; // GNU lines cannot name the algorithm

        var baseDirectory = Path.GetDirectoryName(t.Path)!;
        var lines = items.Select(i => new ChecksumLine(RelativePath(baseDirectory, i.FilePath), AlgorithmOf(i), i.ResultHash, i.Size));
        // Checksum files use lower case like sha256sum; SFV conventionally uses upper case.
        var text = ChecksumWriter.Write(lines, format, format == ExportFormat.Sfv ? HexCase.Upper : HexCase.Lower);
        if (await WriteAsync(t.Path, text))
        {
            Logger.LogInformation("Exported {Count} checksums as {Format}", items.Count, format);
            await Services.Dialogs.ShowAsync(L["Msg_Result_Title"], L.Format("Msg_ExportDone", items.Count, Path.GetFileName(t.Path)), DialogIcon.Success);
        }
    }

    [RelayCommand]
    private async Task CompressAsync()
    {
        var items = ItemsWithResults();
        if (items.Count == 0)
        {
            await Services.Dialogs.ShowAsync(L["Msg_Result_Title"], L["Msg_NothingToExport"], DialogIcon.Information);
            return;
        }

        var target = await Services.Storage.SaveFileAsync(L["Dialog_SaveZip"],
            $"hashes-{DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}.zip",
            [new FileTypeChoice(L["Dialog_FileType_Zip"], "*.zip")]);
        if (target is not { } t) return;

        try
        {
            var baseDirectory = CommonDirectory(items.Select(i => i.DirectoryName));
            await Task.Run(() =>
            {
                using var zip = ZipFile.Open(t.Path, ZipArchiveMode.Create);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var manifest = new List<ChecksumLine>();
                foreach (var item in items)
                {
                    var relative = RelativePath(baseDirectory, item.FilePath);
                    var entryName = UniqueName(used, $"{ZipEntryName(relative)}.{HashAlgorithms.FileExtension(AlgorithmOf(item))}");
                    var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                        writer.Write(ChecksumWriter.Write([new ChecksumLine(item.FileName, AlgorithmOf(item), item.ResultHash)], ExportFormat.Gnu, Settings.HashLetterCase));
                    manifest.Add(new ChecksumLine(relative, AlgorithmOf(item), item.ResultHash, item.Size));
                }

                var all = zip.CreateEntry(UniqueName(used, "CHECKSUMS.txt"), CompressionLevel.Optimal);
                using var manifestWriter = new StreamWriter(all.Open(), new UTF8Encoding(false));
                manifestWriter.Write(ChecksumWriter.Write(manifest, ExportFormat.Bsd));
            });
            Logger.LogInformation("Wrote {Count} hash files to a zip archive", items.Count);
            await Services.Dialogs.ShowAsync(L["Msg_Result_Title"], L.Format("Msg_ExportDone", items.Count, Path.GetFileName(t.Path)), DialogIcon.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowWriteErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task CopyAllAsync()
    {
        var items = ItemsWithResults();
        if (items.Count == 0) return;
        var text = ChecksumWriter.Write(items.Select(i => new ChecksumLine(i.FileName, AlgorithmOf(i), i.ResultHash)),
            items.Select(AlgorithmOf).Distinct().Count() == 1 ? ExportFormat.Gnu : ExportFormat.Bsd, Settings.HashLetterCase);
        await Services.Clipboard.SetTextAsync(text);
    }

    private async Task<bool> WriteAsync(string path, string content)
    {
        try
        {
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowWriteErrorAsync(ex);
            return false;
        }
    }

    private async Task ShowWriteErrorAsync(Exception ex)
    {
        Logger.LogError(ex, "Writing an export failed");
        var diskFull = (ex.HResult & 0xFFFF) is ErrorDiskFull or ErrorHandleDiskFull;
        await Services.Dialogs.ShowErrorAsync(L["Msg_Error"], diskFull ? L["Msg_DiskFull"] : L.Format("Status_Error", ex.Message), ex.ToString());
    }

    private static ExportFormat FormatFromExtension(string path, ExportFormat fallback) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => ExportFormat.Json,
        ".csv" => ExportFormat.Csv,
        ".sfv" => ExportFormat.Sfv,
        ".txt" => fallback == ExportFormat.Gnu ? ExportFormat.Gnu : ExportFormat.Bsd,
        _ => fallback
    };

    /// <summary>Path of <paramref name="file"/> relative to <paramref name="baseDirectory"/> with '/' separators, or the full path.</summary>
    internal static string RelativePath(string baseDirectory, string file)
    {
        var relative = Path.GetRelativePath(baseDirectory, file);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? file : relative.Replace('\\', '/');
    }

    internal static string CommonDirectory(IEnumerable<string> directories)
    {
        string? common = null;
        foreach (var directory in directories)
        {
            if (common is null)
            {
                common = directory;
                continue;
            }

            while (!directory.StartsWith(common, StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(common) is { } parent)
                common = parent;
        }

        return common ?? string.Empty;
    }

    /// <summary>Zip entry names must be relative and use '/' (files on another drive keep their path without the colon).</summary>
    private static string ZipEntryName(string path) => path.Replace('\\', '/').Replace(":", "").TrimStart('/');

    private static string UniqueName(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var i = 2; !used.Add(candidate); i++)
            candidate = $"{Path.ChangeExtension(name, null)} ({i}){Path.GetExtension(name)}";
        return candidate;
    }
}
