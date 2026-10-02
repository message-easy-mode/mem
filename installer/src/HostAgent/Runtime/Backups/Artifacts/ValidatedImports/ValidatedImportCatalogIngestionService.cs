using HostAgent.Runtime.Backups.Catalog;
using Microsoft.AspNetCore.Http;

namespace HostAgent.Runtime.Backups.Artifacts.ValidatedImports;

/// <summary>
/// Validates an externally uploaded portable export and, when validation passes,
/// materialises it into the canonical Backup Catalog. Importing an archive does
/// not start a restore workspace; restore work begins only when an operator
/// explicitly opens the resulting catalog entry.
/// </summary>
public sealed class ValidatedImportCatalogIngestionService
{
    private readonly ImportValidationService _validationService;
    private readonly ImportedZipBackupCatalogMaterialisationService _materialisationService;

    public ValidatedImportCatalogIngestionService(
        ImportValidationService validationService,
        ImportedZipBackupCatalogMaterialisationService materialisationService)
    {
        _validationService = validationService;
        _materialisationService = materialisationService;
    }

    public async Task<ImportValidationResponse> ValidateAndIngestAsync(
        IFormFile file,
        CancellationToken ct)
    {
        var validation = await _validationService.ValidateUploadAsync(file, ct);

        if (!string.Equals(validation.Status, "valid", StringComparison.OrdinalIgnoreCase))
        {
            return validation;
        }

        var materialised = await _materialisationService.MaterialiseAsync(
            validation.ValidationId,
            ct);

        return validation with
        {
            CatalogEntryId = materialised.CatalogEntryId,
            CatalogPayloadState = materialised.PayloadState,
            CatalogMaterialisationAction = materialised.Action,
            Detail = materialised.Detail
        };
    }
}
