using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using HashTool.App.Models;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.Core.Configuration;

namespace HashTool.App.Converters;

/// <summary>
/// Translates enum values and options for display. Bind it as a MultiBinding whose second value is the view model's
/// <c>Localization</c> proxy, so labels update when the language changes.
/// </summary>
public sealed class EnumLabelConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[1] is not LocalizationProxy localization) return values.Count > 0 ? values[0]?.ToString() : null;
        return values[0] switch
        {
            null => localization["Filter_All"],
            FileStatus status => localization[status switch
            {
                FileStatus.Ready => "Lbl_Status_Ready",
                FileStatus.Processing => "Status_Processing_Filter",
                FileStatus.Success => "Status_Done",
                FileStatus.Failure => "Status_Failure",
                _ => "Status_Cancelled"
            }],
            FileSizeFilter size => localization[size switch
            {
                FileSizeFilter.Small => "Size_Small",
                FileSizeFilter.Medium => "Size_Medium",
                FileSizeFilter.Large => "Size_Large",
                FileSizeFilter.ExtraLarge => "Size_ExtraLarge",
                _ => "Size_All"
            }],
            AppThemeStyle style => localization[$"Theme_{style}"],
            ILabeled labeled => localization[labeled.LabelKey],
            var other => other.ToString()
        };
    }
}

/// <summary>Status column text: [ItemStatus, StatusDetail, Localization].</summary>
public sealed class StatusTextConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3 || values[0] is not ItemStatus status || values[2] is not LocalizationProxy localization) return null;
        var detail = values[1] as string;
        return status switch
        {
            ItemStatus.Ready => localization["Lbl_Status_Ready"],
            ItemStatus.Queued => localization["Status_Queued"],
            ItemStatus.Hashing => localization.Format("Status_Processing", detail ?? ""),
            ItemStatus.Done => localization["Status_Done"],
            ItemStatus.Saved => localization["Status_Saved"],
            ItemStatus.Failed => localization.Format("Status_Error", detail ?? ""),
            ItemStatus.NotFound => localization["Status_MissingOriginal"],
            ItemStatus.AccessDenied => localization["Status_AccessDenied"],
            ItemStatus.InUse => localization["Status_InUse"],
            ItemStatus.TimedOut => localization["Status_TimedOut"],
            ItemStatus.Cancelled => localization["Status_Cancelled"],
            ItemStatus.Waiting => localization["Status_Waiting"],
            ItemStatus.Valid => localization["Status_Valid"],
            ItemStatus.ValidLegacyCrc => localization["Status_ValidLegacy"],
            ItemStatus.Mismatch => localization["Status_Invalid"],
            ItemStatus.LengthMismatch => localization.Format("Status_LengthMismatch", detail ?? ""),
            ItemStatus.MissingHash => localization["Status_MissingHash"],
            ItemStatus.FromChecksumFile => localization["Status_ReadyFromHash"],
            ItemStatus.HashLoaded => localization["Status_HashFileLoaded"],
            ItemStatus.NoHashInFile => localization["Status_DropNoHash"],
            ItemStatus.UnreadableHashFile => localization["Status_ReadError"],
            _ => status.ToString()
        };
    }
}

/// <summary>Severity class for the status column (success / error / warning / muted).</summary>
public static class StatusSeverity
{
    public static readonly IValueConverter IsSuccess = new FuncValueConverter<ItemStatus, bool>(s =>
        s is ItemStatus.Done or ItemStatus.Saved or ItemStatus.Valid or ItemStatus.ValidLegacyCrc);

    public static readonly IValueConverter IsError = new FuncValueConverter<ItemStatus, bool>(s =>
        s is ItemStatus.Failed or ItemStatus.NotFound or ItemStatus.AccessDenied or ItemStatus.InUse or ItemStatus.Mismatch
            or ItemStatus.LengthMismatch or ItemStatus.UnreadableHashFile);

    public static readonly IValueConverter IsWarning = new FuncValueConverter<ItemStatus, bool>(s =>
        s is ItemStatus.Cancelled or ItemStatus.TimedOut or ItemStatus.MissingHash or ItemStatus.NoHashInFile);
}

/// <summary>Colour classes for log entries in the Developer console.</summary>
public static class LogLevelSeverity
{
    public static readonly IValueConverter IsWarning =
        new FuncValueConverter<Microsoft.Extensions.Logging.LogLevel, bool>(l => l == Microsoft.Extensions.Logging.LogLevel.Warning);

    public static readonly IValueConverter IsError =
        new FuncValueConverter<Microsoft.Extensions.Logging.LogLevel, bool>(l => l >= Microsoft.Extensions.Logging.LogLevel.Error);
}
