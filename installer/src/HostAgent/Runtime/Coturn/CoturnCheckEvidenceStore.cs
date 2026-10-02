using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Coturn;

/// <summary>
/// Persists the latest browser-safe Coturn functional-check result beneath the
/// Control Plane state root. This evidence is deliberately separate from the
/// owner-only Coturn secret/configuration tree so host-native development can
/// read the latest sanitized result without gaining access to protected TURN
/// credentials.
/// </summary>
public sealed class CoturnCheckEvidenceStore
{
    private const int SchemaVersion = 1;
    private const int MaximumEvidenceBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly string _path;
    private readonly ILogger<CoturnCheckEvidenceStore>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CoturnCheckEvidenceStore(
        MemControlPlaneRuntimeContext runtimeContext,
        ILogger<CoturnCheckEvidenceStore>? logger = null)
        : this(
            Path.Combine(
                runtimeContext.StateRootPath,
                "service-evidence",
                "coturn"),
            logger)
    {
    }

    public CoturnCheckEvidenceStore(
        string directory,
        ILogger<CoturnCheckEvidenceStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "Coturn check-evidence directory is required.",
                nameof(directory));
        }

        _directory = Path.GetFullPath(directory);
        _path = Path.Combine(_directory, "latest-check.json");
        _logger = logger;
    }

    internal string EvidencePath => _path;

    public async Task<CoturnCheckEvidenceReadResult> ReadLatestAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return new CoturnCheckEvidenceReadResult(
                    Result: null,
                    WarningCode: null);
            }

            try
            {
                if (new FileInfo(_path).Length > MaximumEvidenceBytes)
                {
                    return new CoturnCheckEvidenceReadResult(
                        Result: null,
                        WarningCode: CoturnCheckEvidenceWarningCodes.InvalidDocument);
                }

                await using var stream = new FileStream(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                var document = await JsonSerializer.DeserializeAsync<CoturnCheckEvidenceDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken);

                if (document is null ||
                    document.SchemaVersion != SchemaVersion ||
                    document.Result is null ||
                    !IsValidResult(document.Result))
                {
                    return new CoturnCheckEvidenceReadResult(
                        Result: null,
                        WarningCode: CoturnCheckEvidenceWarningCodes.InvalidDocument);
                }

                return new CoturnCheckEvidenceReadResult(
                    document.Result,
                    WarningCode: null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger?.LogWarning(
                    exception,
                    "Coturn latest functional-check evidence could not be read from the Control Plane state root");

                return new CoturnCheckEvidenceReadResult(
                    Result: null,
                    WarningCode: CoturnCheckEvidenceWarningCodes.ReadFailed);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CoturnCheckEvidenceWriteResult> WriteLatestAsync(
        CoturnCheckResponse result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = _path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(_directory);

                var document = new CoturnCheckEvidenceDocument(
                    SchemaVersion,
                    result);
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    document,
                    JsonOptions);

                if (payload.Length > MaximumEvidenceBytes)
                {
                    _logger?.LogWarning(
                        "Coturn latest functional-check evidence exceeded the bounded storage size");
                    return new CoturnCheckEvidenceWriteResult(
                        Stored: false,
                        WarningCode: CoturnCheckEvidenceWarningCodes.WriteFailed);
                }

                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 4096,
                                 options: FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(payload, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(
                    temporaryPath,
                    _path,
                    overwrite: true);

                return new CoturnCheckEvidenceWriteResult(
                    Stored: true,
                    WarningCode: null);
            }
            catch (OperationCanceledException)
            {
                TryDelete(temporaryPath);
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                TryDelete(temporaryPath);
                _logger?.LogWarning(
                    exception,
                    "Coturn latest functional-check evidence could not be persisted beneath the Control Plane state root");

                return new CoturnCheckEvidenceWriteResult(
                    Stored: false,
                    WarningCode: CoturnCheckEvidenceWarningCodes.WriteFailed);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsValidResult(CoturnCheckResponse result)
    {
        var knownStatus = result.Status is
            CoturnCheckStatuses.Passed or
            CoturnCheckStatuses.Warning or
            CoturnCheckStatuses.Failed;

        return knownStatus &&
               result.CheckedAtUtc != default &&
               result.FreshUntilUtc >= result.CheckedAtUtc &&
               !string.IsNullOrWhiteSpace(result.Source) &&
               !string.IsNullOrWhiteSpace(result.ContainerState) &&
               !string.IsNullOrWhiteSpace(result.Readiness) &&
               !string.IsNullOrWhiteSpace(result.PublicHost) &&
               result.Checks is not null &&
               result.Allocation is not null &&
               result.Warnings is not null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup is best-effort and must never replace the original result.
        }
    }

    private sealed record CoturnCheckEvidenceDocument(
        int SchemaVersion,
        CoturnCheckResponse Result);
}

public sealed record CoturnCheckEvidenceReadResult(
    CoturnCheckResponse? Result,
    string? WarningCode);

public sealed record CoturnCheckEvidenceWriteResult(
    bool Stored,
    string? WarningCode);

public static class CoturnCheckEvidenceWarningCodes
{
    public const string InvalidDocument = "coturn_check_evidence_invalid";
    public const string ReadFailed = "coturn_check_evidence_read_failed";
    public const string WriteFailed = "coturn_check_evidence_write_failed";
}
