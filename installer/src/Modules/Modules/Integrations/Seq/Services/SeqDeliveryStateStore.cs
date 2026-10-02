using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Shared.ControlPlane.Runtime;

namespace Modules.Integrations.Seq.Services;

public sealed record SeqDeliveryState(
    bool EffectiveEnabled,
    bool DesiredEnabled,
    bool RestartRequired,
    DateTimeOffset? UpdatedAtUtc,
    string? WarningCode);

public interface ISeqDeliveryStateStore
{
    SeqDeliveryState GetState();

    Task<SeqDeliveryState> SetDesiredAsync(
        bool enabled,
        CancellationToken cancellationToken);
}

/// <summary>
/// Stores only the desired on/off state for the optional Seq sink. No API key,
/// URL, password hash, Docker identity, or host data path is written here.
/// </summary>
public sealed class SeqDeliveryStateStore(
    SeqDiagnosticsOptions options,
    TimeProvider timeProvider,
    IHostEnvironment environment) : ISeqDeliveryStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();

    public SeqDeliveryState GetState()
    {
        lock (_gate)
        {
            string path;
            try
            {
                path = ResolvePath(
                    options.DeliveryStatePath,
                    environment.ContentRootPath);
            }
            catch (Exception exception) when (IsPathException(exception))
            {
                return new SeqDeliveryState(
                    EffectiveEnabled: options.SinkEnabled,
                    DesiredEnabled: options.SinkEnabled,
                    RestartRequired: false,
                    UpdatedAtUtc: null,
                    WarningCode: "seq_delivery_state_path_invalid");
            }

            var read = ReadPreference(path);
            var desired = read.Preference?.Enabled ?? options.SinkEnabled;
            return new SeqDeliveryState(
                EffectiveEnabled: options.SinkEnabled,
                DesiredEnabled: desired,
                RestartRequired: desired != options.SinkEnabled,
                UpdatedAtUtc: read.Preference?.UpdatedAtUtc,
                WarningCode: read.WarningCode ?? options.DeliveryStartupWarningCode);
        }
    }

    public async Task<SeqDeliveryState> SetDesiredAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        string path;
        try
        {
            path = ResolvePath(options.DeliveryStatePath, environment.ContentRootPath);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            throw new SeqOperationException(
                "seq_delivery_state_path_invalid",
                StatusCodes.Status409Conflict,
                "The server-owned Seq delivery state path is invalid.");
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new SeqOperationException(
                "seq_delivery_state_path_invalid",
                StatusCodes.Status409Conflict,
                "The server-owned Seq delivery state path is invalid.");
        }

        var preference = new StoredPreference(
            SchemaVersion: 1,
            Enabled: enabled,
            UpdatedAtUtc: timeProvider.GetUtcNow());
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    preference,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporaryPath);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or NotSupportedException or
                ArgumentException)
        {
            TryDelete(temporaryPath);
            throw new SeqOperationException(
                "seq_delivery_state_write_failed",
                StatusCodes.Status503ServiceUnavailable,
                "MEM could not persist the requested Seq delivery state.");
        }

        return new SeqDeliveryState(
            EffectiveEnabled: options.SinkEnabled,
            DesiredEnabled: enabled,
            RestartRequired: enabled != options.SinkEnabled,
            UpdatedAtUtc: preference.UpdatedAtUtc,
            WarningCode: null);
    }

    /// <summary>
    /// Reads the desired state before logging is created. Enabling delivery is
    /// accepted only when verified server-owned bootstrap state can be resolved
    /// through the authoritative runtime-aware service-routing contract. Local
    /// development therefore uses the published host authority while containerized
    /// modes use Docker-network authorities. Invalid or incomplete state fails
    /// safely with the sink disabled while the desired preference remains visible
    /// as pending.
    /// </summary>
    public static void ApplyConfigurationOverride(
        ConfigurationManager configuration,
        string contentRootPath,
        MemControlPlaneRuntimeContext runtimeContext)
    {
        var options = new SeqDiagnosticsOptions();
        configuration
            .GetSection(SeqDiagnosticsOptions.SectionName)
            .Bind(options);
        string path;
        try
        {
            path = ResolvePath(options.DeliveryStatePath, contentRootPath);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return;
        }

        var stored = ReadPreference(path).Preference;
        if (stored is null)
        {
            return;
        }

        var values = new Dictionary<string, string?>
        {
            [$"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"] = "false"
        };

        if (!stored.Enabled)
        {
            configuration.AddInMemoryCollection(values);
            return;
        }

        var bootstrap = ReadBootstrapStateForStartup(options, contentRootPath);
        if (bootstrap is not null &&
            bootstrap.ManagementEnabled &&
            bootstrap.RuntimeVerifiedAtUtc is not null &&
            string.Equals(
                bootstrap.IngestionCredentialState,
                "available",
                StringComparison.Ordinal) &&
            bootstrap.DeliveryVerifiedAtUtc is not null &&
            bootstrap.SelectedHostPort is > 0 and <= 65535 &&
            SeqSecretResolver.ResolveApiKey(
                options,
                contentRootPath).Available)
        {
            try
            {
                var runtimeProfile = SeqRuntimeContextProfile.Create(
                    runtimeContext,
                    options);
                var source = SeqManagedServiceAuthoritySourceFactory.CreateForBootstrap(
                    options,
                    bootstrap.SelectedHostPort,
                    runtimeProfile);
                var ingestion = MemManagedServiceAuthorityResolver.Resolve(
                    runtimeContext,
                    MemManagedServicePurposes.Ingestion,
                    source);
                var health = MemManagedServiceAuthorityResolver.Resolve(
                    runtimeContext,
                    MemManagedServicePurposes.Health,
                    source);

                values[$"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"] = "true";
                values[$"{SeqDiagnosticsOptions.SectionName}:IngestionUrl"] =
                    ingestion.Authority.ToString();
                values[$"{SeqDiagnosticsOptions.SectionName}:HealthUrl"] =
                    health.Authority.ToString();
            }
            catch (MemManagedServiceAuthorityException)
            {
                values[$"{SeqDiagnosticsOptions.SectionName}:DeliveryStartupWarningCode"] =
                    "seq_delivery_runtime_authority_unavailable";
            }
        }
        else if (CanUseExistingManualConfiguration(
                     options,
                     contentRootPath))
        {
            values[$"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"] = "true";
        }
        else
        {
            values[$"{SeqDiagnosticsOptions.SectionName}:DeliveryStartupWarningCode"] =
                "seq_delivery_startup_prerequisites_unavailable";
        }

        configuration.AddInMemoryCollection(values);
    }

    private static bool CanUseExistingManualConfiguration(
        SeqDiagnosticsOptions options,
        string contentRootPath)
    {
        if (!options.SinkEnabled ||
            SeqDiagnosticsOptionsValidator.Validate(options).Count > 0 ||
            !SeqDiagnosticsOptionsValidator.TryNormalizeUrl(
                options.IngestionUrl,
                out _) ||
            !SeqDiagnosticsOptionsValidator.TryNormalizeUrl(
                options.HealthUrl,
                out _))
        {
            return false;
        }

        return SeqSecretResolver.ResolveApiKey(
            options,
            contentRootPath).Available;
    }

    private static SeqBootstrapState? ReadBootstrapStateForStartup(
        SeqDiagnosticsOptions options,
        string contentRootPath)
    {
        try
        {
            var path = SeqFileSystemSafety.ResolveFile(
                options.BootstrapStatePath,
                contentRootPath,
                "seq_bootstrap_state_path_invalid");
            SeqFileSystemSafety.EnsurePathContainsNoLinks(
                path,
                "seq_bootstrap_state_path_invalid");
            SeqFileSystemSafety.EnsureTargetIsNotLink(
                path,
                "seq_bootstrap_state_path_invalid");
            if (!File.Exists(path))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<SeqBootstrapState>(
                File.ReadAllText(path),
                JsonOptions);
            return state?.SchemaVersion == 1
                ? state
                : null;
        }
        catch (Exception exception) when (
            exception is SeqOperationException or JsonException or IOException or
                UnauthorizedAccessException or System.Security.SecurityException or
                NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    internal static string ResolvePath(string? configuredPath, string anchor)
    {
        var value = string.IsNullOrWhiteSpace(configuredPath)
            ? "data/diagnostics/seq-delivery.json"
            : configuredPath.Trim();
        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(anchor, value));
    }

    private static PreferenceReadResult ReadPreference(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new PreferenceReadResult(null, null);
            }

            var json = File.ReadAllText(path);
            var stored = JsonSerializer.Deserialize<StoredPreference>(json, JsonOptions);
            return stored?.SchemaVersion == 1
                ? new PreferenceReadResult(stored, null)
                : new PreferenceReadResult(null, "seq_delivery_state_invalid");
        }
        catch (JsonException)
        {
            return new PreferenceReadResult(null, "seq_delivery_state_invalid");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or NotSupportedException or
                ArgumentException)
        {
            return new PreferenceReadResult(null, "seq_delivery_state_read_failed");
        }
    }

    private static bool IsPathException(Exception exception) =>
        exception is ArgumentException or NotSupportedException or PathTooLongException;

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
            // Best-effort cleanup of a temporary, non-secret preference file.
        }
    }

    private sealed record PreferenceReadResult(
        StoredPreference? Preference,
        string? WarningCode);

    private sealed record StoredPreference(
        int SchemaVersion,
        bool Enabled,
        DateTimeOffset UpdatedAtUtc);
}
