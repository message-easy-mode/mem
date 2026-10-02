using Mem.Cli.Models;
using Mem.Localization;

namespace Mem.Cli.Output;

/// <summary>
/// Renders human-facing Backup Catalog mutation results. API-authored checks,
/// warnings, errors, and detail text remain verbatim; CLI-owned headings,
/// labels, safety guidance, known values, and formatting are localised.
/// </summary>
public static class BackupMutationPresenter
{
    public static void WriteImport(
        CliOutput output,
        BackupCatalogImportCliResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        output.WriteLocalizedLine(CliMessageKeys.BackupImportTitle);
        output.WriteHumanLine();
        WriteField(output, CliMessageKeys.BackupLabelStatus, FormatKnownValue(output, result.Status));
        WriteField(output, CliMessageKeys.BackupLabelUploadedFile, result.UploadedFileName);
        WriteProvenanceField(
            output,
            CliMessageKeys.BackupLabelValidationReference,
            result.ValidationId ?? Text(output, CliMessageKeys.BackupNotCreated));
        WriteField(output, CliMessageKeys.BackupLabelCatalogEntryId, result.CatalogEntryId ?? Text(output, CliMessageKeys.BackupNotAvailable));
        WriteField(output, CliMessageKeys.BackupLabelPayloadState, FormatKnownValue(output, result.CatalogPayloadState, CliMessageKeys.BackupNotAvailable));
        WriteField(output, CliMessageKeys.BackupLabelMaterialisationAction, FormatKnownValue(output, result.CatalogMaterialisationAction, CliMessageKeys.BackupNotAvailable));
        WriteField(output, CliMessageKeys.BackupLabelZipSize, FormatBytes(output, result.ZipBytes));
        WriteField(output, CliMessageKeys.BackupLabelZipEntries, result.ZipEntryCount);
        WriteField(output, CliMessageKeys.BackupLabelUncompressedSize, FormatBytes(output, result.TotalUncompressedBytes));
        WriteField(output, CliMessageKeys.BackupLabelManifestPresent, FormatYesNo(output, result.ManifestPresent));
        WriteField(output, CliMessageKeys.BackupLabelChecksumsPresent, FormatYesNo(output, result.ChecksumsPresent));

        output.WriteHumanLine();
        output.WriteLocalizedLine(CliMessageKeys.BackupImportIntegrityTitle);
        WriteIndentedField(output, CliMessageKeys.BackupLabelChecksumLines, result.Integrity.ChecksumLines);
        WriteIndentedField(output, CliMessageKeys.BackupLabelCheckedFiles, result.Integrity.CheckedFiles);
        WriteIndentedField(output, CliMessageKeys.BackupLabelPassedChecks, result.Integrity.PassedFiles);
        WriteIndentedField(output, CliMessageKeys.BackupLabelMissingFiles, result.Integrity.MissingFiles);
        WriteIndentedField(output, CliMessageKeys.BackupLabelFailedChecks, result.Integrity.FailedFiles);

        if (result.Manifest is not null)
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupImportManifestTitle);
            WriteIndentedField(output, CliMessageKeys.BackupLabelVersion, result.Manifest.ManifestVersion);
            WriteIndentedField(output, CliMessageKeys.BackupLabelKind, result.Manifest.ExportKind ?? Text(output, CliMessageKeys.BackupUnknown));
            WriteIndentedField(output, CliMessageKeys.BackupLabelCreatedUtc, FormatDate(output, result.Manifest.CreatedAtUtc, CliMessageKeys.BackupUnknown));
            WriteIndentedField(output, CliMessageKeys.BackupLabelMemVersion, result.Manifest.MemVersion ?? Text(output, CliMessageKeys.BackupUnknown));
            WriteIndentedField(output, CliMessageKeys.BackupLabelSourceStack, result.Manifest.Stack.Slug ?? Text(output, CliMessageKeys.BackupUnknown));
            WriteIndentedField(output, CliMessageKeys.BackupLabelStackDisplayName, result.Manifest.Stack.DisplayName ?? Text(output, CliMessageKeys.BackupUnknown));
            WriteIndentedField(output, CliMessageKeys.BackupLabelMatrixServerName, result.Manifest.Stack.MatrixServerName ?? Text(output, CliMessageKeys.BackupUnknown));
            WriteIndentedField(output, CliMessageKeys.BackupLabelDatabasePresent, FormatYesNo(output, result.Manifest.Database.Present));
            WriteIndentedField(output, CliMessageKeys.BackupLabelMatrixPresent, FormatYesNo(output, result.Manifest.Matrix.Present));
            WriteIndentedField(output, CliMessageKeys.BackupLabelMediaFiles, result.Manifest.Matrix.MediaFiles);
            WriteIndentedField(output, CliMessageKeys.BackupLabelMediaBytes, FormatBytes(output, result.Manifest.Matrix.MediaBytes));
            WriteIndentedField(output, CliMessageKeys.BackupLabelElementPresent, FormatYesNo(output, result.Manifest.Element.Present));
        }

        if (result.Checks.Count > 0)
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupChecksTitle);

            foreach (var check in result.Checks)
            {
                var marker = Text(
                    output,
                    check.Passed
                        ? CliMessageKeys.BackupCheckPass
                        : CliMessageKeys.BackupCheckFail);

                output.WriteHumanLine($"  [{marker}] {check.Code}: {check.Message}");

                if (!string.IsNullOrWhiteSpace(check.Detail))
                {
                    output.WriteHumanLine($"         {check.Detail}");
                }
            }
        }

        WriteRawBulletSection(
            output,
            CliMessageKeys.BackupWarningsTitle,
            result.Warnings);
        WriteRawBulletSection(
            output,
            CliMessageKeys.BackupValidationErrorsTitle,
            result.Errors);

        if (!string.IsNullOrWhiteSpace(result.CatalogEntryId))
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupCatalogFirstNextStepsTitle);
            output.WriteLocalizedLine(
                CliMessageKeys.BackupNextInspect,
                Values(("catalogEntryId", result.CatalogEntryId)));

            if (!string.IsNullOrWhiteSpace(result.ValidationId))
            {
                output.WriteLocalizedLine(
                    CliMessageKeys.BackupNextInspectUploadedZip,
                    Values(("validationId", result.ValidationId)));
            }

            output.WriteLocalizedLine(CliMessageKeys.BackupNextStartRestore);
        }

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            output.WriteHumanLine();
            WriteField(output, CliMessageKeys.BackupLabelDetail, result.Detail);
        }
    }

    public static void WriteCatalogPermanentDelete(
        CliOutput output,
        BackupCatalogPermanentDeleteCliResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        output.WriteLocalizedLine(CliMessageKeys.BackupPermanentDeleteTitle);
        output.WriteHumanLine();
        WriteField(output, CliMessageKeys.BackupLabelSource, result.Source);
        WriteField(output, CliMessageKeys.BackupLabelStatus, FormatKnownValue(output, result.Status));
        WriteField(output, CliMessageKeys.BackupLabelCatalogEntryId, result.CatalogEntryId);
        WriteField(output, CliMessageKeys.BackupLabelOrigin, FormatKnownValue(output, result.OriginKind, CliMessageKeys.BackupNotRecorded));
        WriteField(output, CliMessageKeys.BackupLabelDeletedBy, result.DeletedBy ?? Text(output, CliMessageKeys.BackupNotRecorded));
        WriteField(output, CliMessageKeys.BackupLabelPayloadDeleted, FormatYesNo(output, result.PayloadDeleted));
        WriteField(output, CliMessageKeys.BackupLabelOriginalArchiveDeleted, FormatYesNo(output, result.OriginalArchiveDeleted));
        WriteField(output, CliMessageKeys.BackupLabelPortableExportsDeleted, result.PortableExportsDeleted);
        WriteField(output, CliMessageKeys.BackupLabelDetachedRestoreAttempts, result.DetachedRestoreAttempts);

        if (!string.IsNullOrWhiteSpace(result.ActiveRestoreSessionId))
        {
            WriteField(output, CliMessageKeys.BackupLabelActiveRestoreSession, result.ActiveRestoreSessionId);
        }

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            output.WriteHumanLine();
            WriteField(output, CliMessageKeys.BackupLabelDetail, result.Detail);
        }

        if (string.Equals(result.Status, "deleted", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupDeleteSuccessSourceRemoved);
            output.WriteLocalizedLine(CliMessageKeys.BackupDeleteSuccessHistoryRetained);
        }
    }

    public static void WriteCatalogExport(
        CliOutput output,
        BackupCatalogPortableExportCliResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        output.WriteLocalizedLine(CliMessageKeys.BackupExportTitle);
        output.WriteHumanLine();
        WriteField(output, CliMessageKeys.BackupLabelSource, result.Source);
        WriteField(output, CliMessageKeys.BackupLabelStatus, FormatKnownValue(output, result.Status));
        WriteField(output, CliMessageKeys.BackupLabelCatalogEntryId, result.CatalogEntryId);
        WriteField(output, CliMessageKeys.BackupLabelOrigin, FormatKnownValue(output, result.OriginKind, CliMessageKeys.BackupNotRecorded));
        WriteField(output, CliMessageKeys.BackupLabelSourceStack, result.SourceStackSlug ?? Text(output, CliMessageKeys.BackupNotRecorded));
        WriteField(output, CliMessageKeys.BackupLabelExportId, result.ExportId ?? Text(output, CliMessageKeys.BackupNotCreated));
        WriteField(output, CliMessageKeys.BackupLabelDownloadName, result.DownloadName ?? Text(output, CliMessageKeys.BackupNotAvailable));
        WriteField(output, CliMessageKeys.BackupLabelOutputPath, result.OutputPath ?? Text(output, CliMessageKeys.BackupNotWritten));
        WriteField(output, CliMessageKeys.BackupLabelExportSize, FormatNullableBytes(output, result.SizeBytes, CliMessageKeys.BackupUnknown));
        WriteField(output, CliMessageKeys.BackupLabelBytesWritten, FormatBytes(output, result.BytesWritten));

        WriteRawBulletSection(
            output,
            CliMessageKeys.BackupWarningsTitle,
            result.Warnings);

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            output.WriteHumanLine();
            WriteField(output, CliMessageKeys.BackupLabelDetail, result.Detail);
        }
    }

    public static void WriteUploadedZipArchiveDelete(
        CliOutput output,
        UploadedZipArchiveDeleteCliResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        output.WriteLocalizedLine(CliMessageKeys.BackupUploadedZipDeleteTitle);
        output.WriteHumanLine();
        WriteField(output, CliMessageKeys.BackupLabelSource, result.Source);
        WriteField(output, CliMessageKeys.BackupLabelStatus, FormatKnownValue(output, result.Status));
        WriteProvenanceField(output, CliMessageKeys.BackupLabelValidationReference, result.ValidationId);
        WriteField(output, CliMessageKeys.BackupLabelArchiveState, FormatKnownValue(output, result.ArchiveState));
        WriteField(output, CliMessageKeys.BackupLabelBytesDeleted, FormatBytes(output, result.DeletedBytes));
        WriteField(output, CliMessageKeys.BackupLabelDeletedUtc, FormatDate(output, result.DeletedAtUtc, CliMessageKeys.BackupNotRecorded));

        WriteRawBulletSection(
            output,
            CliMessageKeys.BackupWarningsTitle,
            result.Warnings);

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            output.WriteHumanLine();
            WriteField(output, CliMessageKeys.BackupLabelDetail, result.Detail);
        }

        if (string.Equals(result.Status, "deleted", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupUploadedZipDeleteSuccessArchiveRemoved);
            output.WriteLocalizedLine(CliMessageKeys.BackupUploadedZipDeleteSuccessCatalogRetained);
        }
    }

    private static void WriteRawBulletSection(
        CliOutput output,
        string titleKey,
        IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        output.WriteHumanLine();
        output.WriteLocalizedLine(titleKey);

        foreach (var value in values)
        {
            output.WriteLocalizedLine(
                CliMessageKeys.BackupBullet,
                Values(("value", value)));
        }
    }

    private static void WriteField(
        CliOutput output,
        string labelKey,
        object? value)
    {
        output.WriteLocalizedLine(
            CliMessageKeys.BackupField,
            Values(
                ("label", Text(output, labelKey)),
                ("value", value)));
    }

    private static void WriteIndentedField(
        CliOutput output,
        string labelKey,
        object? value)
    {
        output.WriteLocalizedLine(
            CliMessageKeys.BackupIndentedField,
            Values(
                ("label", Text(output, labelKey)),
                ("value", value)));
    }

    private static void WriteProvenanceField(
        CliOutput output,
        string labelKey,
        object? value)
    {
        output.WriteLocalizedLine(
            CliMessageKeys.BackupProvenanceField,
            Values(
                ("label", Text(output, labelKey)),
                ("value", value)));
    }

    private static string FormatKnownValue(
        CliOutput output,
        string? value,
        string missingValueKey = CliMessageKeys.BackupUnknown)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Text(output, missingValueKey);
        }

        var key = value.Trim().ToLowerInvariant() switch
        {
            "ok" => CliMessageKeys.BackupValueOk,
            "error" => CliMessageKeys.BackupValueError,
            "unreachable" => CliMessageKeys.BackupValueUnreachable,
            "warning" => CliMessageKeys.BackupValueWarning,
            "valid" => CliMessageKeys.BackupValueValid,
            "invalid" => CliMessageKeys.BackupValueInvalid,
            "unknown" => CliMessageKeys.BackupUnknown,
            "available" => CliMessageKeys.BackupValueAvailable,
            "materialising" => CliMessageKeys.BackupValueMaterialising,
            "failed" => CliMessageKeys.BackupValueFailed,
            "removed" => CliMessageKeys.BackupValueRemoved,
            "retained" => CliMessageKeys.BackupValueRetained,
            "missing" => CliMessageKeys.BackupValueMissing,
            "ambiguous" => CliMessageKeys.BackupValueAmbiguous,
            "local-captured" => CliMessageKeys.BackupValueLocalCaptured,
            "imported-zip" => CliMessageKeys.BackupValueImportedZip,
            "uploaded-zip" => CliMessageKeys.BackupValueUploadedZip,
            "created" => CliMessageKeys.BackupValueCreated,
            "deleted" => CliMessageKeys.BackupValueDeleted,
            "blocked" => CliMessageKeys.BackupValueBlocked,
            "write-failed" => CliMessageKeys.BackupValueWriteFailed,
            "downloaded" => CliMessageKeys.BackupValueDownloaded,
            _ => null
        };

        return key is null
            ? value
            : Text(output, key);
    }

    private static string FormatYesNo(
        CliOutput output,
        bool value)
    {
        return Text(
            output,
            value
                ? CliMessageKeys.BackupYes
                : CliMessageKeys.BackupNo);
    }

    private static string FormatNullableBytes(
        CliOutput output,
        long? value,
        string missingValueKey)
    {
        return value.HasValue
            ? FormatBytes(output, value.Value)
            : Text(output, missingValueKey);
    }

    private static string FormatBytes(
        CliOutput output,
        long value)
    {
        if (value < 1024)
        {
            return $"{output.FormatNumber(value, "0")} B";
        }

        string[] units = ["KB", "MB", "GB", "TB"];
        var size = value / 1024d;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024d;
            unitIndex++;
        }

        return $"{output.FormatNumber((decimal)size, "0.##")} {units[unitIndex]}";
    }

    private static string FormatDate(
        CliOutput output,
        DateTimeOffset? value,
        string missingValueKey)
    {
        return value.HasValue
            ? output.FormatDateTime(
                value.Value.ToUniversalTime(),
                "yyyy-MM-dd HH:mm:ss 'UTC'")
            : Text(output, missingValueKey);
    }

    private static string Text(
        CliOutput output,
        string key)
    {
        return output.FormatLocalized(key);
    }

    private static IReadOnlyDictionary<string, object?> Values(
        params (string Name, object? Value)[] pairs)
    {
        var values = new Dictionary<string, object?>(
            pairs.Length,
            StringComparer.Ordinal);

        foreach (var (name, value) in pairs)
        {
            values[name] = value;
        }

        return values;
    }
}
