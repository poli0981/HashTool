using System;
using System.Globalization;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HashTool.App.Converters;
using HashTool.App.Models;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HashTool.App.Tests;

public sealed class ConverterTests
{
    private static LocalizationProxy Proxy => new(TestServices.Localization);

    private static string L(string key) => TestServices.Localization[key];

    [AvaloniaFact]
    public void EnumLabels_TranslateEveryKindOfValue()
    {
        var converter = new EnumLabelConverter();
        object? Label(object? value) => converter.Convert([value, Proxy], typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(L("Filter_All"), Label(null));
        Assert.Equal(L("Lbl_Status_Ready"), Label(FileStatus.Ready));
        Assert.Equal(L("Status_Processing_Filter"), Label(FileStatus.Processing));
        Assert.Equal(L("Status_Done"), Label(FileStatus.Success));
        Assert.Equal(L("Status_Failure"), Label(FileStatus.Failure));
        Assert.Equal(L("Status_Cancelled"), Label(FileStatus.Cancelled));
        Assert.Equal(L("Size_All"), Label(FileSizeFilter.All));
        Assert.Equal(L("Size_Small"), Label(FileSizeFilter.Small));
        Assert.Equal(L("Size_Medium"), Label(FileSizeFilter.Medium));
        Assert.Equal(L("Size_Large"), Label(FileSizeFilter.Large));
        Assert.Equal(L("Size_ExtraLarge"), Label(FileSizeFilter.ExtraLarge));
        foreach (var style in Enum.GetValues<Core.Configuration.AppThemeStyle>()) Assert.Equal(L($"Theme_{style}"), Label(style));
        Assert.Equal(L("Btn_Yes"), Label(new Option<int>(1, "Btn_Yes")));
        Assert.Equal("42", Label(42));
    }

    [AvaloniaFact]
    public void EnumLabels_WithoutLocalization_FallBackToTheValue()
    {
        var converter = new EnumLabelConverter();

        Assert.Equal("Success", converter.Convert([FileStatus.Success], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("Success", converter.Convert([FileStatus.Success, "not a proxy"], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert([], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert([null], typeof(string), null, CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void StatusText_HasATranslationForEveryStatus()
    {
        var converter = new StatusTextConverter();
        foreach (var status in Enum.GetValues<ItemStatus>())
        {
            foreach (var detail in new[] { "SHA256", null })
            {
                var text = converter.Convert([status, detail, Proxy], typeof(string), null, CultureInfo.InvariantCulture) as string;
                Assert.False(string.IsNullOrEmpty(text), status.ToString());
                Assert.False(text.StartsWith('['), $"{status}: {text}");
            }
        }

        Assert.Contains("SHA256", (string)converter.Convert([ItemStatus.Hashing, "SHA256", Proxy], typeof(string), null, CultureInfo.InvariantCulture)!);
        Assert.Equal("999", converter.Convert([(ItemStatus)999, null, Proxy], typeof(string), null, CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void StatusText_NeedsStatusAndLocalization()
    {
        var converter = new StatusTextConverter();

        Assert.Null(converter.Convert([ItemStatus.Done, null], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert(["Done", null, Proxy], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert([ItemStatus.Done, null, "proxy"], typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void StatusSeverity_PutsEveryStatusInAtMostOneClass()
    {
        ItemStatus[] success = [ItemStatus.Done, ItemStatus.Saved, ItemStatus.Valid, ItemStatus.ValidLegacyCrc];
        ItemStatus[] error =
        [
            ItemStatus.Failed, ItemStatus.NotFound, ItemStatus.AccessDenied, ItemStatus.InUse, ItemStatus.Mismatch, ItemStatus.LengthMismatch,
            ItemStatus.UnreadableHashFile
        ];
        ItemStatus[] warning = [ItemStatus.Cancelled, ItemStatus.TimedOut, ItemStatus.MissingHash, ItemStatus.NoHashInFile];

        foreach (var status in Enum.GetValues<ItemStatus>())
        {
            Assert.Equal(success.Contains(status), Is(StatusSeverity.IsSuccess, status));
            Assert.Equal(error.Contains(status), Is(StatusSeverity.IsError, status));
            Assert.Equal(warning.Contains(status), Is(StatusSeverity.IsWarning, status));
        }
    }

    [Fact]
    public void LogLevelSeverity_MarksWarningsAndErrors()
    {
        Assert.True(Is(LogLevelSeverity.IsWarning, LogLevel.Warning));
        Assert.False(Is(LogLevelSeverity.IsWarning, LogLevel.Error));
        Assert.True(Is(LogLevelSeverity.IsError, LogLevel.Error));
        Assert.True(Is(LogLevelSeverity.IsError, LogLevel.Critical));
        Assert.False(Is(LogLevelSeverity.IsError, LogLevel.Information));
    }

    private static bool Is(Avalonia.Data.Converters.IValueConverter converter, object value) =>
        (bool)converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture)!;

    [Theory]
    [InlineData("0123456789abcdef", true, false, null, "0123********cdef")]
    [InlineData("0123456789abcdef", true, true, null, "0123456789abcdef")]
    [InlineData("0123456789abcdef", false, false, null, "0123456789abcdef")]
    [InlineData("0123456789abcdef", false, true, "MaskOnly", "0123********cdef")]
    [InlineData("abcd1234", true, false, null, "********")]
    [InlineData("", true, false, null, "")]
    [InlineData("", false, false, "MaskOnly", "")]
    public void HashMask_HidesTheMiddleOfAHash(string hash, bool masking, bool revealed, string? parameter, string expected)
    {
        Assert.Equal(expected, new HashMaskConverter().Convert([hash, masking, revealed], typeof(string), parameter, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void HashMask_ToleratesMissingValues()
    {
        var converter = new HashMaskConverter();

        Assert.Equal("", converter.Convert([null, null], typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("abc", converter.Convert(["abc", null, null], typeof(string), null, CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void FontName_ShowsTheDefaultFontAsTranslatedLabel()
    {
        var converter = new FontNameConverter();

        Assert.Equal(L("Font_Default"), converter.Convert(FontFamily.Default, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(L("Font_Default"), converter.Convert(new FontFamily("$Default"), typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("Inter", converter.Convert(new FontFamily("Inter"), typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(5, converter.Convert(5, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack("Inter", typeof(FontFamily), null, CultureInfo.InvariantCulture));
    }
}
