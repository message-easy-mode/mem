using Mem.Cli.Models;
using Mem.Localization;

namespace Mem.Cli.Output;

/// <summary>
/// Human-facing presentation for the read-only Backup Catalog and uploaded ZIP
/// provenance commands. It translates CLI-owned structure and known values,
/// while preserving server-authored details, advisories, warnings, and
/// validation messages verbatim for audit and support use.
/// </summary>
public static class BackupReadPresenter
{
    public static void WriteCatalogList(
        CliOutput output,
        BackupCatalogListCliResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        output.WriteLocalizedLine(CliMessageKeys.BackupListTitle);
        output.WriteHumanLine();
        WriteField(output, CliMessageKeys.BackupLabelSource, result.Source);
        WriteField(output, CliMessageKeys.BackupLabelStatus, FormatKnownValue(output, result.Status));
        WriteField(output, CliMessageKeys.BackupLabelEntries, result.TotalCount);

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            WriteField(output, CliMessageKeys.BackupLabelDetail, result.Detail);
        }

        output.WriteHumanLine();

        if (result.Entries.Count == 0)
        {
            output.WriteLocalizedLine(CliMessageKeys.BackupListEmpty);
            return;
        }

        var catalogEntryIdHeader = output.FormatLocalized(
            CliMessageKeys.BackupTableCatalogEntryId);
        var catalogEntryIdColumnWidth = Math.Max(
            28,
            Math.Max(
                catalogEntryIdHeader.Length,
                result.Entries.Max(entry => entry.CatalogEntryId.Length)));

        output.WriteHumanLine(
            $"{Pad(catalogEntryIdHeader, catalogEntryIdColumnWidth)} " +
            $"{Pad(output.FormatLocalized(CliMessageKeys.BackupTableOrigin), 15)} " +
            $"{Pad(output.FormatLocalized(CliMessageKeys.BackupTableDisplayName), 26)} " +
            $"{Pad(output.FormatLocalized(CliMessageKeys.BackupTableSourceStack), 20)} " +
            $"{Pad(output.FormatLocalized(CliMessageKeys.BackupTableCaptured), 24)} " +
            $"{Pad(output.FormatLocalized(CliMessageKeys.BackupTablePayload), 14)} " +
            $"{Pad(output.FormatLocalized(CliMessageKeys.BackupTableIntegrity), 12)} " +
            $"{Pad(output.FormatLocalized(CliMessageKeys.BackupTableSize), 10)} " +
            output.FormatLocalized(CliMessageKeys.BackupTableWarnings));

        foreach (var entry in result.Entries)
        {
            output.WriteHumanLine(
                $"{Pad(entry.CatalogEntryId, catalogEntryIdColumnWidth)} " +
                $"{Pad(FormatKnownValue(output, entry.OriginKind), 15)} " +
                $"{Pad(entry.DisplayName, 26)} " +
                $"{Pad(entry.SourceStackSlug ?? "-", 20)} " +
                $"{Pad(FormatCatalogDate(output, entry.CapturedAtUtc), 24)} " +
                $"{Pad(FormatKnownValue(output, entry.PayloadState), 14)} " +
                $"{Pad(FormatKnownValue(output, entry.IntegrityStatus), 12)} " +
                $"{Pad(FormatNullableBytes(output, entry.PayloadBytes, CliMessageKeys.BackupUnknown), 10)} " +
                entry.WarningCount);
        }
    }

    public static void WriteCatalogEntry(
        CliOutput output,
        BackupCatalogDetailCliResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        output.WriteLocalizedLine(CliMessageKeys.BackupEntryTitle);
        output.WriteHumanLine();
        WriteField(output, CliMessageKeys.BackupLabelSource, result.Source);
        WriteField(output, CliMessageKeys.BackupLabelStatus, FormatKnownValue(output, result.Status));

        if (result.Entry is null)
        {
            output.WriteHumanLine();

            if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                WriteField(output, CliMessageKeys.BackupLabelDetail, result.Detail);
            }
            else
            {
                output.WriteLocalizedLine(CliMessageKeys.BackupEntryNotFound);
            }

            return;
        }

        var entry = result.Entry;

        output.WriteHumanLine();
        WriteField(output, CliMessageKeys.BackupLabelCatalogEntryId, entry.CatalogEntryId);
        WriteField(output, CliMessageKeys.BackupLabelDisplayName, entry.DisplayName);
        WriteField(output, CliMessageKeys.BackupLabelOrigin, FormatKnownValue(output, entry.OriginKind));
        WriteField(output, CliMessageKeys.BackupLabelSourceStack, entry.SourceStackSlug ?? Text(output, CliMessageKeys.BackupUnknown));
        WriteProvenanceField(output, CliMessageKeys.BackupLabelSourceBackup, entry.SourceBackupId ?? Text(output, CliMessageKeys.BackupUnknown));
        WriteField(output, CliMessageKeys.BackupLabelCapturedUtc, FormatCatalogDate(output, entry.CapturedAtUtc));
        WriteField(output, CliMessageKeys.BackupLabelCreatedUtc, FormatCatalogDate(output, entry.CreatedAtUtc));
        WriteField(output, CliMessageKeys.BackupLabelImportedUtc, FormatCatalogDate(output, entry.ImportedAtUtc));
        WriteField(output, CliMessageKeys.BackupLabelMaterialisedUtc, FormatCatalogDate(output, entry.MaterialisedAtUtc));
        WriteField(output, CliMessageKeys.BackupLabelPayloadState, FormatKnownValue(output, entry.PayloadState));
        WriteField(output, CliMessageKeys.BackupLabelPayloadSize, FormatNullableBytes(output, entry.PayloadBytes, CliMessageKeys.BackupUnknown));
        WriteField(output, CliMessageKeys.BackupLabelIntegrity, FormatKnownValue(output, entry.IntegrityStatus));
        WriteField(output, CliMessageKeys.BackupLabelWarnings, entry.WarningCount);

        if (!string.IsNullOrWhiteSpace(entry.IntegritySummary))
        {
            WriteField(output, CliMessageKeys.BackupLabelIntegrityDetail, entry.IntegritySummary);
        }

        if (!string.IsNullOrWhiteSpace(entry.ValidationId))
        {
            WriteProvenanceField(output, CliMessageKeys.BackupLabelUploadReference, entry.ValidationId);
        }

        if (!string.IsNullOrWhiteSpace(entry.MatrixServerName) ||
            !string.IsNullOrWhiteSpace(entry.MatrixHost) ||
            !string.IsNullOrWhiteSpace(entry.ElementHost))
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupMatrixIdentityTitle);
            WriteIndentedField(output, CliMessageKeys.BackupLabelServerName, entry.MatrixServerName ?? Text(output, CliMessageKeys.BackupUnknown));
            WriteIndentedField(output, CliMessageKeys.BackupLabelMatrixHost, entry.MatrixHost ?? Text(output, CliMessageKeys.BackupUnknown));
            WriteIndentedField(output, CliMessageKeys.BackupLabelElementHost, entry.ElementHost ?? Text(output, CliMessageKeys.BackupUnknown));
        }

        if (entry.Advisories is { Count: > 0 })
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupAdvisoriesTitle);

            foreach (var advisory in entry.Advisories)
            {
                output.WriteLocalizedLine(
                    CliMessageKeys.BackupAdvisoryLine,
                    Values(
                        ("category", advisory.Category),
                        ("title", advisory.Title),
                        ("message", advisory.Message)));
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            output.WriteHumanLine();
            WriteField(output, CliMessageKeys.BackupLabelDetail, result.Detail);
        }
    }

    public static void WriteCatalogLifecycle(
        CliOutput output,
        BackupCatalogLifecycleCliResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        output.WriteLocalizedLine(CliMessageKeys.BackupLifecycleTitle);
        output.WriteHumanLine();
        WriteField(output, CliMessageKeys.BackupLabelSource, result.Source);
        WriteField(output, CliMessageKeys.BackupLabelStatus, FormatKnownValue(output, result.Status));

        if (result.Lifecycle is null)
        {
            output.WriteHumanLine();

            if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                WriteField(output, CliMessageKeys.BackupLabelDetail, result.Detail);
            }
            else
            {
                output.WriteLocalizedLine(CliMessageKeys.BackupLifecycleNotFound);
            }

            return;
        }

        var lifecycle = result.Lifecycle;

        output.WriteHumanLine();
        WriteField(output, CliMessageKeys.BackupLabelCatalogEntryId, lifecycle.CatalogEntryId);
        WriteField(output, CliMessageKeys.BackupLabelPayloadState, FormatKnownValue(output, lifecycle.PayloadState));
        WriteField(output, CliMessageKeys.BackupLabelPayloadPresent, FormatYesNo(output, lifecycle.PayloadPresent));
        WriteField(output, CliMessageKeys.BackupLabelActiveRestore, FormatYesNo(output, lifecycle.HasActiveRestore));
        WriteField(output, CliMessageKeys.BackupLabelActiveRestoreSession, lifecycle.ActiveRestoreSessionId ?? Text(output, CliMessageKeys.BackupNone));
        WriteField(output, CliMessageKeys.BackupLabelCanPermanentlyDelete, FormatYesNo(output, lifecycle.CanDelete));
        WriteField(output, CliMessageKeys.BackupLabelDeleteBlock, lifecycle.DeleteBlockReason ?? Text(output, CliMessageKeys.BackupNone));

        if (lifecycle.OriginalArchive is not null)
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupOriginalArchiveTitle);
            WriteProvenanceIndentedField(output, CliMessageKeys.BackupLabelValidationReference, lifecycle.OriginalArchive.ValidationId);
            WriteIndentedField(output, CliMessageKeys.BackupLabelArchiveState, FormatKnownValue(output, lifecycle.OriginalArchive.ArchiveState));
            WriteIndentedField(output, CliMessageKeys.BackupLabelUploadedFile, lifecycle.OriginalArchive.OriginalFileName ?? Text(output, CliMessageKeys.BackupNotRecorded));
            WriteIndentedField(output, CliMessageKeys.BackupLabelArchiveSize, FormatNullableBytes(output, lifecycle.OriginalArchive.ArchiveBytes, CliMessageKeys.BackupNotRecorded));
        }

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            output.WriteHumanLine();
            WriteField(output, CliMessageKeys.BackupLabelDetail, result.Detail);
        }
    }

    public static void WriteUploadedZipProvenance(
        CliOutput output,
        UploadedZipArchiveDetailCliResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        output.WriteLocalizedLine(CliMessageKeys.BackupUploadedZipTitle);
        output.WriteHumanLine();
        WriteField(output, CliMessageKeys.BackupLabelSource, result.Source);
        WriteField(output, CliMessageKeys.BackupLabelStatus, FormatKnownValue(output, result.Status));
        WriteProvenanceField(output, CliMessageKeys.BackupLabelValidationReference, result.ValidationId);
        WriteField(output, CliMessageKeys.BackupLabelSourceKind, FormatKnownValue(output, result.SourceKind));
        WriteField(output, CliMessageKeys.BackupLabelUploadedFile, result.UploadedFileName ?? Text(output, CliMessageKeys.BackupNotRecorded));
        WriteField(output, CliMessageKeys.BackupLabelRecordedUtc, FormatCatalogDate(output, result.RecordedAtUtc));
        WriteField(output, CliMessageKeys.BackupLabelArchiveState, FormatKnownValue(output, result.ArchiveState));
        WriteField(output, CliMessageKeys.BackupLabelArchiveSize, FormatNullableBytes(output, result.ArchiveBytes, CliMessageKeys.BackupNotRecorded));
        output.WriteHumanLine();

        output.WriteLocalizedLine(CliMessageKeys.BackupValidationTitle);
        WriteIndentedField(output, CliMessageKeys.BackupLabelStatus, FormatKnownValue(output, result.Validation.Status));
        WriteIndentedField(output, CliMessageKeys.BackupLabelSummary, result.Validation.Summary);
        WriteIndentedField(output, CliMessageKeys.BackupLabelZipEntries, result.Validation.ZipEntryCount);
        WriteIndentedField(output, CliMessageKeys.BackupLabelUncompressedSize, FormatBytes(output, result.Validation.TotalUncompressedBytes));
        WriteIndentedField(output, CliMessageKeys.BackupLabelManifestPresent, FormatYesNo(output, result.Validation.ManifestPresent));
        WriteIndentedField(output, CliMessageKeys.BackupLabelChecksumsPresent, FormatYesNo(output, result.Validation.ChecksumsPresent));
        WriteIndentedField(output, CliMessageKeys.BackupLabelPassedChecks, result.Validation.PassedChecks);
        WriteIndentedField(output, CliMessageKeys.BackupLabelFailedChecks, result.Validation.FailedChecks);
        WriteIndentedField(output, CliMessageKeys.BackupLabelWarnings, result.Validation.WarningCount);

        if (result.Manifest is not null)
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupManifestIdentityTitle);
            WriteIndentedField(output, CliMessageKeys.BackupLabelVersion, result.Manifest.ManifestVersion?.ToString() ?? Text(output, CliMessageKeys.BackupNotRecorded));
            WriteIndentedField(output, CliMessageKeys.BackupLabelMemVersion, result.Manifest.MemVersion ?? Text(output, CliMessageKeys.BackupNotRecorded));
            WriteIndentedField(output, CliMessageKeys.BackupLabelSourceStack, result.Manifest.SourceStackSlug ?? Text(output, CliMessageKeys.BackupNotRecorded));
            WriteIndentedField(output, CliMessageKeys.BackupLabelStackDisplayName, result.Manifest.SourceStackDisplayName ?? Text(output, CliMessageKeys.BackupNotRecorded));
            WriteIndentedField(output, CliMessageKeys.BackupLabelMatrixServerName, result.Manifest.MatrixServerName ?? Text(output, CliMessageKeys.BackupNotRecorded));
            WriteIndentedField(output, CliMessageKeys.BackupLabelIncludedFiles, result.Manifest.IncludedFileCount);
        }

        output.WriteHumanLine();
        output.WriteLocalizedLine(CliMessageKeys.BackupRetentionTitle);
        WriteIndentedField(output, CliMessageKeys.BackupLabelCanDelete, FormatYesNo(output, result.Retention.CanDelete));
        WriteIndentedField(output, CliMessageKeys.BackupLabelDeleteBlock, result.Retention.DeleteBlockReason ?? Text(output, CliMessageKeys.BackupNone));
        WriteIndentedField(output, CliMessageKeys.BackupLabelRemovedUtc, FormatCatalogDate(output, result.Retention.RemovedAtUtc));
        WriteIndentedField(output, CliMessageKeys.BackupLabelRemovedBy, result.Retention.RemovedBy ?? Text(output, CliMessageKeys.BackupNotRemoved));

        if (result.Validation.Errors.Count > 0)
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupValidationErrorsTitle);

            foreach (var error in result.Validation.Errors)
            {
                WriteBullet(output, error);
            }
        }

        if (result.Warnings.Count > 0)
        {
            output.WriteHumanLine();
            output.WriteLocalizedLine(CliMessageKeys.BackupWarningsTitle);

            foreach (var warning in result.Warnings)
            {
                WriteBullet(output, warning);
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            output.WriteHumanLine();
            WriteField(output, CliMessageKeys.BackupLabelDetail, result.Detail);
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

    private static void WriteProvenanceIndentedField(
        CliOutput output,
        string labelKey,
        object? value)
    {
        output.WriteLocalizedLine(
            CliMessageKeys.BackupProvenanceIndentedField,
            Values(
                ("label", Text(output, labelKey)),
                ("value", value)));
    }

    private static void WriteBullet(
        CliOutput output,
        string value)
    {
        output.WriteLocalizedLine(
            CliMessageKeys.BackupBullet,
            Values(("value", value)));
    }

    private static string Text(
        CliOutput output,
        string key)
    {
        return output.FormatLocalized(key);
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

    private static string FormatKnownValue(
        CliOutput output,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

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
            _ => null
        };

        return key is null
            ? value
            : Text(output, key);
    }

    private static string FormatCatalogDate(
        CliOutput output,
        DateTime? value)
    {
        return value.HasValue
            ? output.FormatDateTime(
                new DateTimeOffset(value.Value.ToUniversalTime()),
                "yyyy-MM-dd HH:mm:ss 'UTC'")
            : Text(output, CliMessageKeys.BackupUnknown);
    }

    private static string FormatCatalogDate(
        CliOutput output,
        DateTimeOffset? value)
    {
        return value.HasValue
            ? output.FormatDateTime(
                value.Value.ToUniversalTime(),
                "yyyy-MM-dd HH:mm:ss 'UTC'")
            : Text(output, CliMessageKeys.BackupNotRecorded);
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

    private static string Pad(
        string value,
        int length)
    {
        return value.Length >= length
            ? value[..length]
            : value.PadRight(length);
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
