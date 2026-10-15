using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

/// <summary>The "Verify Hash" page.</summary>
public sealed partial class CheckHashViewModel : FileListViewModelBase
{
    [ObservableProperty] private HashType _defaultAlgorithm;

    public CheckHashViewModel(FileListServices services) : base(services)
    {
        _defaultAlgorithm = services.Settings.Current.VerifyAlgorithm;
    }

    partial void OnDefaultAlgorithmChanged(HashType value) => Services.Settings.Update(s => s.VerifyAlgorithm = value);

    protected override string PickFilesTitleKey => "Dialog_SelectCheckFiles";

    protected override ActivationVerb ElevationVerb => ActivationVerb.Verify;

    protected override ItemStatus QueuedStatus => ItemStatus.Waiting;

    // ------------------------------------------------------------------ importing

    protected override async Task<IReadOnlyList<FileItem>> CreateItemsAsync(IReadOnlyList<FileEntry> files, ImportReport report)
    {
        var items = new List<FileItem>();
        foreach (var file in files)
        {
            if (!ChecksumFileNames.IsChecksumFile(file.FullPath, out _))
            {
                items.Add(new FileItem(file.FullPath, file.Length, DefaultAlgorithm));
                continue;
            }

            ChecksumDocument document;
            try
            {
                document = await ChecksumParser.ParseFileAsync(file.FullPath, DefaultAlgorithm).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                report.Errors.Add($"{Path.GetFileName(file.FullPath)}: {ex.Message}");
                continue;
            }

            if (document.InvalidLines.Count > 0)
                report.Errors.Add(L.Format("Msg_ChecksumFileInvalid", document.InvalidLines.Count, Path.GetFileName(file.FullPath)));
            if (document.IsEmpty)
            {
                report.Errors.Add(L.Format("Msg_ChecksumFileEmpty", Path.GetFileName(file.FullPath)));
                continue;
            }

            var directory = Path.GetDirectoryName(file.FullPath)!;
            foreach (var entry in document.Entries)
            {
                // A bare hash in "image.iso.sha256" belongs to "image.iso".
                var target = entry.FileName is null ? ChecksumFileNames.SidecarTarget(file.FullPath) : entry.ResolvePath(directory);
                if (target is null)
                {
                    report.Errors.Add(L.Format("Msg_UnsafePath", entry.FileName ?? Path.GetFileName(file.FullPath)));
                    continue;
                }

                var info = new FileInfo(target);
                var item = new FileItem(target, info.Exists ? info.Length : 0, entry.Algorithm ?? DefaultAlgorithm)
                {
                    ExpectedHash = entry.Hash,
                    IsAlgorithmKnown = entry.Algorithm is not null && !entry.IsAlgorithmGuessed,
                    SourceChecksumFile = file.FullPath
                };
                if (info.Exists) item.SetStatus(ItemStatus.FromChecksumFile, Path.GetFileName(file.FullPath));
                else
                {
                    item.State = FileStatus.Failure;
                    item.IsMatch = false;
                    item.SetStatus(ItemStatus.NotFound);
                }

                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>Dropping a file and its checksum file together fills in the row instead of being ignored.</summary>
    protected override bool Merge(FileItem existing, FileItem incoming)
    {
        if (string.IsNullOrEmpty(incoming.ExpectedHash) || existing.IsProcessing) return false;
        existing.ExpectedHash = incoming.ExpectedHash;
        existing.Algorithm = incoming.Algorithm;
        existing.IsAlgorithmKnown = incoming.IsAlgorithmKnown;
        existing.SourceChecksumFile = incoming.SourceChecksumFile;
        existing.ResetResult();
        existing.SetStatus(incoming.Status, incoming.StatusDetail);
        return true;
    }

    [RelayCommand]
    private async Task OpenChecksumFileAsync()
    {
        var files = await Services.Storage.PickFilesAsync(L["Dialog_OpenChecksumFile"], allowMultiple: true,
            [new FileTypeChoice(L["FileType_Checksum"], "*.*")]);
        if (files.Count > 0) await AddPathsAsync(files);
    }

    /// <summary>Loads the expected hash for one row from a checksum file chosen by the user (any name is accepted).</summary>
    [RelayCommand]
    private async Task BrowseHashFileAsync(FileItem? item)
    {
        if (item is null || item.IsProcessing) return;
        var files = await Services.Storage.PickFilesAsync(L.Format("Dialog_SelectHashFile", item.FileName), allowMultiple: false);
        if (files.Count == 1) await LoadHashFileAsync(item, files[0]);
    }

    /// <summary>Reads a dropped or browsed checksum file and copies the matching hash onto the row.</summary>
    public async Task LoadHashFileAsync(FileItem item, string hashFile)
    {
        try
        {
            ChecksumFileNames.IsChecksumFile(hashFile, out var hint);
            var document = await ChecksumParser.ParseFileAsync(hashFile, hint ?? item.Algorithm);
            var entry = document.FindEntry(item.FilePath, Path.GetDirectoryName(hashFile)!)
                        ?? (document.Entries.Count == 1 ? document.Entries[0] : null);
            if (entry is null)
            {
                item.SetStatus(ItemStatus.NoHashInFile, Path.GetFileName(hashFile));
                return;
            }

            item.ResetResult();
            item.ExpectedHash = entry.Hash;
            if (entry.Algorithm is { } algorithm) item.Algorithm = algorithm;
            item.IsAlgorithmKnown = entry.Algorithm is not null && !entry.IsAlgorithmGuessed;
            item.SourceChecksumFile = hashFile;
            item.SetStatus(ItemStatus.HashLoaded, Path.GetFileName(hashFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.LogWarning(ex, "Could not read {File}", Path.GetFileName(hashFile));
            item.SetStatus(ItemStatus.UnreadableHashFile, ex.Message);
        }
    }

    partial void OnDefaultAlgorithmChanging(HashType value)
    {
        foreach (var item in AllItems.Where(i => !i.IsAlgorithmKnown && !i.IsProcessing)) item.Algorithm = value;
    }

    // ------------------------------------------------------------------ verification

    protected override async Task<IReadOnlyList<FileItem>> PrepareAsync(IReadOnlyList<FileItem> items)
    {
        var defaultAlgorithm = DefaultAlgorithm;
        var lookups = await Task.Run(async () =>
        {
            var found = new List<(FileItem Item, ChecksumEntry Entry, string File)>();
            foreach (var item in items.Where(i => string.IsNullOrWhiteSpace(i.ExpectedHash)))
            {
                foreach (var (sidecar, algorithm) in ChecksumFileNames.SidecarCandidates(item.FilePath, defaultAlgorithm))
                {
                    if (!File.Exists(sidecar)) continue;
                    try
                    {
                        var document = await ChecksumParser.ParseFileAsync(sidecar, algorithm).ConfigureAwait(false);
                        if (document.FindEntry(item.FilePath, Path.GetDirectoryName(sidecar)!) is { } entry)
                        {
                            found.Add((item, entry, sidecar));
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        Logger.LogDebug(ex, "Skipping unreadable sidecar {File}", Path.GetFileName(sidecar));
                    }
                }
            }

            return found;
        });

        foreach (var (item, entry, file) in lookups)
        {
            item.ExpectedHash = entry.Hash;
            if (entry.Algorithm is { } algorithm) item.Algorithm = algorithm;
            item.IsAlgorithmKnown = entry.Algorithm is not null && !entry.IsAlgorithmGuessed;
            item.SourceChecksumFile = file;
        }

        var ready = new List<FileItem>();
        foreach (var item in items)
        {
            if (!File.Exists(item.FilePath))
            {
                item.ResetResult();
                item.State = FileStatus.Failure;
                item.IsMatch = false;
                item.SetStatus(ItemStatus.NotFound);
            }
            else if (string.IsNullOrWhiteSpace(item.ExpectedHash))
            {
                item.ResetResult();
                item.State = FileStatus.Failure;
                item.IsMatch = false;
                item.SetStatus(ItemStatus.MissingHash);
            }
            else
            {
                ready.Add(item);
            }
        }

        return ready;
    }

    protected override HashJob? CreateJob(FileItem item) =>
        item.IsRemoved ? null : new HashJob(item.FilePath, item.Size, AlgorithmsToTry(item), item.Cancellation?.Token ?? default);

    /// <summary>
    /// The row's algorithm, plus every other algorithm with the same digest length when the algorithm was only guessed:
    /// one read checks them all, so a 64-character hash verifies whether it is SHA-256 or BLAKE3.
    /// </summary>
    private IReadOnlyList<HashType> AlgorithmsToTry(FileItem item)
    {
        var expectedLength = HashMatcher.Normalize(item.ExpectedHash).Length;
        if (item.IsAlgorithmKnown || expectedLength == 0) return [item.Algorithm];

        var candidates = HashAlgorithms.CandidatesForHexLength(expectedLength).ToList();
        if (candidates.Count == 0) return [item.Algorithm];
        if (candidates.Remove(item.Algorithm)) candidates.Insert(0, item.Algorithm);
        else if (candidates.Remove(DefaultAlgorithm)) candidates.Insert(0, DefaultAlgorithm);
        return candidates;
    }

    protected override void ApplyResult(FileItem item, HashJobResult result)
    {
        if (result.Status != HashJobStatus.Succeeded || result.Results is null)
        {
            ApplyFailure(item, result);
            if (result.Error is not null) Logger.LogWarning(result.Error, "Verifying {File} failed", item.FileName);
            return;
        }

        var comparisons = result.Results.Select(r => (Result: r, Match: HashMatcher.Compare(item.ExpectedHash, r))).ToList();
        var best = comparisons.FirstOrDefault(c => c.Match == HashMatch.Match);
        if (best.Result is null) best = comparisons.FirstOrDefault(c => c.Match == HashMatch.LegacyByteOrderMatch);

        if (best.Result is not null)
        {
            item.Algorithm = best.Result.Algorithm;
            item.ResultHash = best.Result.ToHex(Settings.HashLetterCase);
            item.IsMatch = true;
            item.State = FileStatus.Success;
            item.SetStatus(best.Match == HashMatch.LegacyByteOrderMatch ? ItemStatus.ValidLegacyCrc : ItemStatus.Valid);
            Logger.LogInformation("Valid: {File} ({Algorithm})", item.FileName, item.Algorithm);
            return;
        }

        var primary = comparisons[0];
        item.ResultHash = primary.Result.ToHex(Settings.HashLetterCase);
        item.IsMatch = false;
        item.State = FileStatus.Failure;
        if (primary.Match is HashMatch.LengthMismatch or HashMatch.InvalidExpected)
            item.SetStatus(ItemStatus.LengthMismatch, item.Algorithm.ToString());
        else
            item.SetStatus(ItemStatus.Mismatch);
        Logger.LogWarning("Mismatch: {File} ({Algorithm})", item.FileName, item.Algorithm);
    }

    protected override void ResetItem(FileItem item) => item.ResetResult(string.IsNullOrEmpty(item.ExpectedHash) ? ItemStatus.Ready : ItemStatus.Waiting);

    protected override bool IsFailedForCleanup(FileItem item) => item.IsMatch != true;

    protected override async Task OnBatchFinishedAsync(IReadOnlyList<FileItem> items)
    {
        if (items.Count <= 1) return;
        var icon = FailedCount > 0 || CancelledCount > 0 ? DialogIcon.Warning : DialogIcon.Success;
        var message = $"{L.Format("Msg_CheckResult", items.Count, SucceededCount, FailedCount, CancelledCount)}\n{L.Format("Msg_TaskDuration", FormatClock(BatchElapsed))}";
        await Services.Dialogs.ShowAsync(L["Msg_Result_Title"], message, icon);
        await OfferElevationAsync(items);
    }
}
