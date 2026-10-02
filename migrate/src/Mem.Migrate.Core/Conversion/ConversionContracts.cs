namespace Mem.Migrate.Core.Conversion;

public interface IConversionJournal
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<StoredConversionRun?> GetAsync(string conversionId, CancellationToken cancellationToken);
    Task StartAsync(string conversionId, DateTimeOffset startedAtUtc, string archiveSha256, Guid sourceStackId, CancellationToken cancellationToken);
    Task CompleteAsync(ConversionReport report, string reportJson, CancellationToken cancellationToken);
    Task FailAsync(string conversionId, DateTimeOffset completedAtUtc, string errorCode, string errorMessage, CancellationToken cancellationToken);
}

public interface IVerifiedArchiveExtractor
{
    Task ExtractFilesAsync(
        string archivePath,
        IReadOnlyDictionary<string, string> archivePathToDestination,
        CancellationToken cancellationToken);
}

public interface ISynapseConversionService
{
    Task<ConversionReport> ConvertAsync(ConversionOptions options, CancellationToken cancellationToken);
}
