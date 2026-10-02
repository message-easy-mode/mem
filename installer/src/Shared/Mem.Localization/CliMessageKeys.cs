namespace Mem.Localization;

/// <summary>
/// Human-presentation message codes for mem-cli. Command names, flags, JSON
/// property names, IDs, statuses, and error codes stay outside resources.
/// </summary>
public static class CliMessageKeys
{
    public const string HelpTitle = "cli.help.title";
    public const string HelpUsage = "cli.help.usage";
    public const string HelpOptions = "cli.help.options";
    public const string HelpEnvironment = "cli.help.environment";
    public const string HelpPlanned = "cli.help.planned";
    public const string HelpCommands = "cli.help.commands";
    public const string HelpAuthorityLine1 = "cli.help.authority.line1";
    public const string HelpAuthorityLine2 = "cli.help.authority.line2";
    public const string HelpStableCommandNote = "cli.help.stable-command-note";

    public const string HelpOptionProfile = "cli.help.option.profile";
    public const string HelpOptionServer = "cli.help.option.server";
    public const string HelpOptionHostAgentUrl = "cli.help.option.host-agent-url";
    public const string HelpOptionInstallerToken = "cli.help.option.installer-token";
    public const string HelpOptionLanguage = "cli.help.option.language";
    public const string HelpOptionJson = "cli.help.option.json";

    public const string HelpEnvironmentServerUrl = "cli.help.environment.server-url";
    public const string HelpEnvironmentHostAgentUrl = "cli.help.environment.host-agent-url";
    public const string HelpEnvironmentInstallerToken = "cli.help.environment.installer-token";
    public const string HelpEnvironmentLanguage = "cli.help.environment.language";
    public const string HelpPlannedStackCompare = "cli.help.planned.stack-compare";

    public const string ErrorMissingLanguageValue = "cli.error.missing-language-value";
    public const string ErrorUnsupportedLanguage = "cli.error.unsupported-language";
    public const string ErrorInstallerTokenRequired = "cli.error.installer-token-required";
    public const string ErrorInstallerTokenRejected = "cli.error.installer-token-rejected";
    public const string ErrorAgentSecretRetired = "cli.error.agent-secret-retired";

    public const string ConfigLanguageTitle = "cli.config.language.title";
    public const string ConfigLanguageValue = "cli.config.language.value";
    public const string ConfigLanguageSystemDefault = "cli.config.language.system-default";
    public const string ConfigLanguageSaved = "cli.config.language.saved";
    public const string ConfigLanguageJsonContracts = "cli.config.language.json-contracts";

    public const string LoginTitle = "cli.login.title";
    public const string LoginProfileLine = "cli.login.profile-line";
    public const string LoginServerLine = "cli.login.server-line";
    public const string LoginOpenBrowser = "cli.login.open-browser";
    public const string LoginEnterDeviceCode = "cli.login.enter-device-code";
    public const string LoginWaitingApproval = "cli.login.waiting-approval";
    public const string LoginExistingSessionStored = "cli.login.existing-session-stored";
    public const string LoginExistingNoNewAuthorization = "cli.login.existing-no-new-authorization";
    public const string LoginApproved = "cli.login.approved";
    public const string LoginCredentialStored = "cli.login.credential-stored";
    public const string LoginIdleExpiry = "cli.login.idle-expiry";
    public const string LoginAbsoluteExpiry = "cli.login.absolute-expiry";

    public const string LoginErrorInteractiveRequired = "cli.login.error.interactive-required";
    public const string LoginErrorDeviceRequired = "cli.login.error.device-required";
    public const string LoginErrorProfileRequired = "cli.login.error.profile-required";
    public const string LoginErrorProfileInvalid = "cli.login.error.profile-invalid";
    public const string LoginErrorSecureStoreUnavailable = "cli.login.error.secure-store-unavailable";
    public const string LoginErrorSecureStoreReadUnavailable = "cli.login.error.secure-store-read-unavailable";
    public const string LoginErrorCancelled = "cli.login.error.cancelled";
    public const string LoginErrorUnavailable = "cli.login.error.unavailable";
    public const string LoginErrorUnreachableWaiting = "cli.login.error.unreachable-waiting";
    public const string LoginErrorAuthorizationDenied = "cli.login.error.authorization-denied";
    public const string LoginErrorAuthorizationExpired = "cli.login.error.authorization-expired";
    public const string LoginErrorAuthorizationFailed = "cli.login.error.authorization-failed";
    public const string LoginErrorInvalidCredential = "cli.login.error.invalid-credential";
    public const string LoginErrorSecureStoreWriteFailed = "cli.login.error.secure-store-write-failed";
    public const string LoginErrorRateLimited = "cli.login.error.rate-limited";
    public const string LoginErrorUnreachableStart = "cli.login.error.unreachable-start";
    public const string LoginErrorAuthorizationUnavailable = "cli.login.error.authorization-unavailable";

    public const string BackupCatalogTitle = "cli.backups.catalog-title";
    public const string BackupMatchingCount = "cli.backups.matching-count";
    public const string BackupMatchingCountOne = "cli.backups.matching-count.one";
    public const string BackupMatchingCountOther = "cli.backups.matching-count.other";

    public const string BackupListTitle = "cli.backups.title.list";
    public const string BackupEntryTitle = "cli.backups.title.entry";
    public const string BackupLifecycleTitle = "cli.backups.title.lifecycle";
    public const string BackupUploadedZipTitle = "cli.backups.title.uploaded-zip";
    public const string BackupMatrixIdentityTitle = "cli.backups.section.matrix-identity";
    public const string BackupAdvisoriesTitle = "cli.backups.section.advisories";
    public const string BackupOriginalArchiveTitle = "cli.backups.section.original-archive";
    public const string BackupValidationTitle = "cli.backups.section.validation";
    public const string BackupManifestIdentityTitle = "cli.backups.section.manifest-identity";
    public const string BackupRetentionTitle = "cli.backups.section.retention";
    public const string BackupValidationErrorsTitle = "cli.backups.section.validation-errors";
    public const string BackupWarningsTitle = "cli.backups.section.warnings";

    public const string BackupField = "cli.backups.field";
    public const string BackupIndentedField = "cli.backups.field.indented";
    public const string BackupProvenanceField = "cli.backups.field.provenance";
    public const string BackupProvenanceIndentedField = "cli.backups.field.provenance-indented";
    public const string BackupBullet = "cli.backups.bullet";
    public const string BackupAdvisoryLine = "cli.backups.advisory-line";

    public const string BackupListEmpty = "cli.backups.list.empty";
    public const string BackupEntryNotFound = "cli.backups.entry.not-found";
    public const string BackupLifecycleNotFound = "cli.backups.lifecycle.not-found";

    public const string BackupTableCatalogEntryId = "cli.backups.table.catalog-entry-id";
    public const string BackupTableOrigin = "cli.backups.table.origin";
    public const string BackupTableDisplayName = "cli.backups.table.display-name";
    public const string BackupTableSourceStack = "cli.backups.table.source-stack";
    public const string BackupTableCaptured = "cli.backups.table.captured";
    public const string BackupTablePayload = "cli.backups.table.payload";
    public const string BackupTableIntegrity = "cli.backups.table.integrity";
    public const string BackupTableSize = "cli.backups.table.size";
    public const string BackupTableWarnings = "cli.backups.table.warnings";

    public const string BackupLabelSource = "cli.backups.label.source";
    public const string BackupLabelStatus = "cli.backups.label.status";
    public const string BackupLabelEntries = "cli.backups.label.entries";
    public const string BackupLabelDetail = "cli.backups.label.detail";
    public const string BackupLabelCatalogEntryId = "cli.backups.label.catalog-entry-id";
    public const string BackupLabelDisplayName = "cli.backups.label.display-name";
    public const string BackupLabelOrigin = "cli.backups.label.origin";
    public const string BackupLabelSourceStack = "cli.backups.label.source-stack";
    public const string BackupLabelSourceBackup = "cli.backups.label.source-backup";
    public const string BackupLabelCapturedUtc = "cli.backups.label.captured-utc";
    public const string BackupLabelCreatedUtc = "cli.backups.label.created-utc";
    public const string BackupLabelImportedUtc = "cli.backups.label.imported-utc";
    public const string BackupLabelMaterialisedUtc = "cli.backups.label.materialised-utc";
    public const string BackupLabelPayloadState = "cli.backups.label.payload-state";
    public const string BackupLabelPayloadSize = "cli.backups.label.payload-size";
    public const string BackupLabelIntegrity = "cli.backups.label.integrity";
    public const string BackupLabelWarnings = "cli.backups.label.warnings";
    public const string BackupLabelIntegrityDetail = "cli.backups.label.integrity-detail";
    public const string BackupLabelUploadReference = "cli.backups.label.upload-reference";
    public const string BackupLabelServerName = "cli.backups.label.server-name";
    public const string BackupLabelMatrixHost = "cli.backups.label.matrix-host";
    public const string BackupLabelElementHost = "cli.backups.label.element-host";
    public const string BackupLabelPayloadPresent = "cli.backups.label.payload-present";
    public const string BackupLabelActiveRestore = "cli.backups.label.active-restore";
    public const string BackupLabelActiveRestoreSession = "cli.backups.label.active-restore-session";
    public const string BackupLabelCanPermanentlyDelete = "cli.backups.label.can-permanently-delete";
    public const string BackupLabelDeleteBlock = "cli.backups.label.delete-block";
    public const string BackupLabelValidationReference = "cli.backups.label.validation-reference";
    public const string BackupLabelArchiveState = "cli.backups.label.archive-state";
    public const string BackupLabelUploadedFile = "cli.backups.label.uploaded-file";
    public const string BackupLabelArchiveSize = "cli.backups.label.archive-size";
    public const string BackupLabelSourceKind = "cli.backups.label.source-kind";
    public const string BackupLabelRecordedUtc = "cli.backups.label.recorded-utc";
    public const string BackupLabelSummary = "cli.backups.label.summary";
    public const string BackupLabelZipEntries = "cli.backups.label.zip-entries";
    public const string BackupLabelUncompressedSize = "cli.backups.label.uncompressed-size";
    public const string BackupLabelManifestPresent = "cli.backups.label.manifest-present";
    public const string BackupLabelChecksumsPresent = "cli.backups.label.checksums-present";
    public const string BackupLabelPassedChecks = "cli.backups.label.passed-checks";
    public const string BackupLabelFailedChecks = "cli.backups.label.failed-checks";
    public const string BackupLabelVersion = "cli.backups.label.version";
    public const string BackupLabelMemVersion = "cli.backups.label.mem-version";
    public const string BackupLabelStackDisplayName = "cli.backups.label.stack-display-name";
    public const string BackupLabelMatrixServerName = "cli.backups.label.matrix-server-name";
    public const string BackupLabelIncludedFiles = "cli.backups.label.included-files";
    public const string BackupLabelCanDelete = "cli.backups.label.can-delete";
    public const string BackupLabelRemovedUtc = "cli.backups.label.removed-utc";
    public const string BackupLabelRemovedBy = "cli.backups.label.removed-by";

    public const string BackupYes = "cli.backups.value.yes";
    public const string BackupNo = "cli.backups.value.no";
    public const string BackupUnknown = "cli.backups.value.unknown";
    public const string BackupNone = "cli.backups.value.none";
    public const string BackupNotRecorded = "cli.backups.value.not-recorded";
    public const string BackupNotRemoved = "cli.backups.value.not-removed";
    public const string BackupValueOk = "cli.backups.value.ok";
    public const string BackupValueError = "cli.backups.value.error";
    public const string BackupValueUnreachable = "cli.backups.value.unreachable";
    public const string BackupValueWarning = "cli.backups.value.warning";
    public const string BackupValueValid = "cli.backups.value.valid";
    public const string BackupValueInvalid = "cli.backups.value.invalid";
    public const string BackupValueAvailable = "cli.backups.value.available";
    public const string BackupValueMaterialising = "cli.backups.value.materialising";
    public const string BackupValueFailed = "cli.backups.value.failed";
    public const string BackupValueRemoved = "cli.backups.value.removed";
    public const string BackupValueRetained = "cli.backups.value.retained";
    public const string BackupValueMissing = "cli.backups.value.missing";
    public const string BackupValueAmbiguous = "cli.backups.value.ambiguous";
    public const string BackupValueLocalCaptured = "cli.backups.value.local-captured";
    public const string BackupValueImportedZip = "cli.backups.value.imported-zip";
    public const string BackupValueUploadedZip = "cli.backups.value.uploaded-zip";

    public const string BackupErrorListDoesNotSupportStack = "cli.backups.error.list-does-not-support-stack";
    public const string BackupErrorListUseInspect = "cli.backups.error.list-use-inspect";
    public const string BackupErrorMissingCatalogEntryId = "cli.backups.error.missing-catalog-entry-id";
    public const string BackupErrorInspectRequiresCatalogEntry = "cli.backups.error.inspect-requires-catalog-entry";
    public const string BackupErrorLifecycleRequiresCatalogEntry = "cli.backups.error.lifecycle-requires-catalog-entry";
    public const string BackupErrorMissingValidationReference = "cli.backups.error.missing-validation-reference";

    public const string BackupImportTitle = "cli.backups.title.import";
    public const string BackupExportTitle = "cli.backups.title.export";
    public const string BackupPermanentDeleteTitle = "cli.backups.title.permanent-delete";
    public const string BackupUploadedZipDeleteTitle = "cli.backups.title.uploaded-zip-delete";
    public const string BackupImportIntegrityTitle = "cli.backups.section.import-integrity";
    public const string BackupImportManifestTitle = "cli.backups.section.import-manifest";
    public const string BackupChecksTitle = "cli.backups.section.checks";
    public const string BackupCatalogFirstNextStepsTitle = "cli.backups.section.catalog-first-next-steps";
    public const string BackupLabelMaterialisationAction = "cli.backups.label.materialisation-action";
    public const string BackupLabelZipSize = "cli.backups.label.zip-size";
    public const string BackupLabelChecksumLines = "cli.backups.label.checksum-lines";
    public const string BackupLabelCheckedFiles = "cli.backups.label.checked-files";
    public const string BackupLabelMissingFiles = "cli.backups.label.missing-files";
    public const string BackupLabelKind = "cli.backups.label.kind";
    public const string BackupLabelDatabasePresent = "cli.backups.label.database-present";
    public const string BackupLabelMatrixPresent = "cli.backups.label.matrix-present";
    public const string BackupLabelMediaFiles = "cli.backups.label.media-files";
    public const string BackupLabelMediaBytes = "cli.backups.label.media-bytes";
    public const string BackupLabelElementPresent = "cli.backups.label.element-present";
    public const string BackupLabelExportId = "cli.backups.label.export-id";
    public const string BackupLabelDownloadName = "cli.backups.label.download-name";
    public const string BackupLabelOutputPath = "cli.backups.label.output-path";
    public const string BackupLabelExportSize = "cli.backups.label.export-size";
    public const string BackupLabelBytesWritten = "cli.backups.label.bytes-written";
    public const string BackupLabelDeletedBy = "cli.backups.label.deleted-by";
    public const string BackupLabelPayloadDeleted = "cli.backups.label.payload-deleted";
    public const string BackupLabelOriginalArchiveDeleted = "cli.backups.label.original-archive-deleted";
    public const string BackupLabelPortableExportsDeleted = "cli.backups.label.portable-exports-deleted";
    public const string BackupLabelDetachedRestoreAttempts = "cli.backups.label.detached-restore-attempts";
    public const string BackupLabelDeletedUtc = "cli.backups.label.deleted-utc";
    public const string BackupLabelBytesDeleted = "cli.backups.label.bytes-deleted";
    public const string BackupValueCreated = "cli.backups.value.created";
    public const string BackupValueDeleted = "cli.backups.value.deleted";
    public const string BackupValueBlocked = "cli.backups.value.blocked";
    public const string BackupValueWriteFailed = "cli.backups.value.write-failed";
    public const string BackupValueDownloaded = "cli.backups.value.downloaded";
    public const string BackupNotCreated = "cli.backups.value.not-created";
    public const string BackupNotAvailable = "cli.backups.value.not-available";
    public const string BackupNotWritten = "cli.backups.value.not-written";
    public const string BackupCheckPass = "cli.backups.check.pass";
    public const string BackupCheckFail = "cli.backups.check.fail";
    public const string BackupErrorDeleteRequiresCatalogEntry = "cli.backups.error.delete-requires-catalog-entry";
    public const string BackupErrorRefusePermanentDeleteWithoutYes = "cli.backups.error.refuse-permanent-delete-without-yes";
    public const string BackupErrorPermanentDeleteImpact = "cli.backups.error.permanent-delete-impact";
    public const string BackupErrorPermanentDeleteActiveRestore = "cli.backups.error.permanent-delete-active-restore";
    public const string BackupErrorMissingCatalogEntryOrOutputPath = "cli.backups.error.missing-catalog-entry-or-output-path";
    public const string BackupErrorExportRequiresCatalogEntry = "cli.backups.error.export-requires-catalog-entry";
    public const string BackupErrorMissingPortableZipPath = "cli.backups.error.missing-portable-zip-path";
    public const string BackupErrorUnknownCommand = "cli.backups.error.unknown-command";
    public const string BackupErrorUnknownUploadsCommand = "cli.backups.error.unknown-uploads-command";
    public const string BackupErrorRefuseUploadDeleteWithoutYes = "cli.backups.error.refuse-upload-delete-without-yes";
    public const string BackupErrorUploadDeleteImpact = "cli.backups.error.upload-delete-impact";
    public const string BackupNextInspect = "cli.backups.next.inspect";
    public const string BackupNextInspectUploadedZip = "cli.backups.next.inspect-uploaded-zip";
    public const string BackupNextStartRestore = "cli.backups.next.start-restore";
    public const string BackupDeleteSuccessSourceRemoved = "cli.backups.delete.success.source-removed";
    public const string BackupDeleteSuccessHistoryRetained = "cli.backups.delete.success.history-retained";
    public const string BackupUploadedZipDeleteSuccessArchiveRemoved = "cli.backups.upload-delete.success.archive-removed";
    public const string BackupUploadedZipDeleteSuccessCatalogRetained = "cli.backups.upload-delete.success.catalog-retained";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        HelpTitle,
        HelpUsage,
        HelpOptions,
        HelpEnvironment,
        HelpPlanned,
        HelpCommands,
        HelpAuthorityLine1,
        HelpAuthorityLine2,
        HelpStableCommandNote,
        LoginTitle,
        LoginProfileLine,
        LoginServerLine,
        LoginOpenBrowser,
        LoginEnterDeviceCode,
        LoginWaitingApproval,
        LoginExistingSessionStored,
        LoginExistingNoNewAuthorization,
        LoginApproved,
        LoginCredentialStored,
        LoginIdleExpiry,
        LoginAbsoluteExpiry,
        LoginErrorInteractiveRequired,
        LoginErrorDeviceRequired,
        LoginErrorProfileRequired,
        LoginErrorProfileInvalid,
        LoginErrorSecureStoreUnavailable,
        LoginErrorSecureStoreReadUnavailable,
        LoginErrorCancelled,
        LoginErrorUnavailable,
        LoginErrorUnreachableWaiting,
        LoginErrorAuthorizationDenied,
        LoginErrorAuthorizationExpired,
        LoginErrorAuthorizationFailed,
        LoginErrorInvalidCredential,
        LoginErrorSecureStoreWriteFailed,
        LoginErrorRateLimited,
        LoginErrorUnreachableStart,
        LoginErrorAuthorizationUnavailable,
        HelpOptionProfile,
        HelpOptionServer,
        HelpOptionHostAgentUrl,
        HelpOptionInstallerToken,
        HelpOptionLanguage,
        HelpOptionJson,
        HelpEnvironmentServerUrl,
        HelpEnvironmentHostAgentUrl,
        HelpEnvironmentInstallerToken,
        HelpEnvironmentLanguage,
        HelpPlannedStackCompare,
        ErrorMissingLanguageValue,
        ErrorUnsupportedLanguage,
        ErrorInstallerTokenRequired,
        ErrorInstallerTokenRejected,
        ErrorAgentSecretRetired,
        ConfigLanguageTitle,
        ConfigLanguageValue,
        ConfigLanguageSystemDefault,
        ConfigLanguageSaved,
        ConfigLanguageJsonContracts,
        BackupCatalogTitle,
        BackupMatchingCountOne,
        BackupMatchingCountOther,
        BackupListTitle,
        BackupEntryTitle,
        BackupLifecycleTitle,
        BackupUploadedZipTitle,
        BackupMatrixIdentityTitle,
        BackupAdvisoriesTitle,
        BackupOriginalArchiveTitle,
        BackupValidationTitle,
        BackupManifestIdentityTitle,
        BackupRetentionTitle,
        BackupValidationErrorsTitle,
        BackupWarningsTitle,
        BackupField,
        BackupIndentedField,
        BackupProvenanceField,
        BackupProvenanceIndentedField,
        BackupBullet,
        BackupAdvisoryLine,
        BackupListEmpty,
        BackupEntryNotFound,
        BackupLifecycleNotFound,
        BackupTableCatalogEntryId,
        BackupTableOrigin,
        BackupTableDisplayName,
        BackupTableSourceStack,
        BackupTableCaptured,
        BackupTablePayload,
        BackupTableIntegrity,
        BackupTableSize,
        BackupTableWarnings,
        BackupLabelSource,
        BackupLabelStatus,
        BackupLabelEntries,
        BackupLabelDetail,
        BackupLabelCatalogEntryId,
        BackupLabelDisplayName,
        BackupLabelOrigin,
        BackupLabelSourceStack,
        BackupLabelSourceBackup,
        BackupLabelCapturedUtc,
        BackupLabelCreatedUtc,
        BackupLabelImportedUtc,
        BackupLabelMaterialisedUtc,
        BackupLabelPayloadState,
        BackupLabelPayloadSize,
        BackupLabelIntegrity,
        BackupLabelWarnings,
        BackupLabelIntegrityDetail,
        BackupLabelUploadReference,
        BackupLabelServerName,
        BackupLabelMatrixHost,
        BackupLabelElementHost,
        BackupLabelPayloadPresent,
        BackupLabelActiveRestore,
        BackupLabelActiveRestoreSession,
        BackupLabelCanPermanentlyDelete,
        BackupLabelDeleteBlock,
        BackupLabelValidationReference,
        BackupLabelArchiveState,
        BackupLabelUploadedFile,
        BackupLabelArchiveSize,
        BackupLabelSourceKind,
        BackupLabelRecordedUtc,
        BackupLabelSummary,
        BackupLabelZipEntries,
        BackupLabelUncompressedSize,
        BackupLabelManifestPresent,
        BackupLabelChecksumsPresent,
        BackupLabelPassedChecks,
        BackupLabelFailedChecks,
        BackupLabelVersion,
        BackupLabelMemVersion,
        BackupLabelStackDisplayName,
        BackupLabelMatrixServerName,
        BackupLabelIncludedFiles,
        BackupLabelCanDelete,
        BackupLabelRemovedUtc,
        BackupLabelRemovedBy,
        BackupYes,
        BackupNo,
        BackupUnknown,
        BackupNone,
        BackupNotRecorded,
        BackupNotRemoved,
        BackupValueOk,
        BackupValueError,
        BackupValueUnreachable,
        BackupValueWarning,
        BackupValueValid,
        BackupValueInvalid,
        BackupValueAvailable,
        BackupValueMaterialising,
        BackupValueFailed,
        BackupValueRemoved,
        BackupValueRetained,
        BackupValueMissing,
        BackupValueAmbiguous,
        BackupValueLocalCaptured,
        BackupValueImportedZip,
        BackupValueUploadedZip,
        BackupErrorListDoesNotSupportStack,
        BackupErrorListUseInspect,
        BackupErrorMissingCatalogEntryId,
        BackupErrorInspectRequiresCatalogEntry,
        BackupErrorLifecycleRequiresCatalogEntry,
        BackupErrorMissingValidationReference,
        BackupImportTitle,
        BackupExportTitle,
        BackupPermanentDeleteTitle,
        BackupUploadedZipDeleteTitle,
        BackupImportIntegrityTitle,
        BackupImportManifestTitle,
        BackupChecksTitle,
        BackupCatalogFirstNextStepsTitle,
        BackupLabelMaterialisationAction,
        BackupLabelZipSize,
        BackupLabelChecksumLines,
        BackupLabelCheckedFiles,
        BackupLabelMissingFiles,
        BackupLabelKind,
        BackupLabelDatabasePresent,
        BackupLabelMatrixPresent,
        BackupLabelMediaFiles,
        BackupLabelMediaBytes,
        BackupLabelElementPresent,
        BackupLabelExportId,
        BackupLabelDownloadName,
        BackupLabelOutputPath,
        BackupLabelExportSize,
        BackupLabelBytesWritten,
        BackupLabelDeletedBy,
        BackupLabelPayloadDeleted,
        BackupLabelOriginalArchiveDeleted,
        BackupLabelPortableExportsDeleted,
        BackupLabelDetachedRestoreAttempts,
        BackupLabelDeletedUtc,
        BackupLabelBytesDeleted,
        BackupValueCreated,
        BackupValueDeleted,
        BackupValueBlocked,
        BackupValueWriteFailed,
        BackupValueDownloaded,
        BackupNotCreated,
        BackupNotAvailable,
        BackupNotWritten,
        BackupCheckPass,
        BackupCheckFail,
        BackupErrorDeleteRequiresCatalogEntry,
        BackupErrorRefusePermanentDeleteWithoutYes,
        BackupErrorPermanentDeleteImpact,
        BackupErrorPermanentDeleteActiveRestore,
        BackupErrorMissingCatalogEntryOrOutputPath,
        BackupErrorExportRequiresCatalogEntry,
        BackupErrorMissingPortableZipPath,
        BackupErrorUnknownCommand,
        BackupErrorUnknownUploadsCommand,
        BackupErrorRefuseUploadDeleteWithoutYes,
        BackupErrorUploadDeleteImpact,
        BackupNextInspect,
        BackupNextInspectUploadedZip,
        BackupNextStartRestore,
        BackupDeleteSuccessSourceRemoved,
        BackupDeleteSuccessHistoryRetained,
        BackupUploadedZipDeleteSuccessArchiveRemoved,
        BackupUploadedZipDeleteSuccessCatalogRetained
    };
}
