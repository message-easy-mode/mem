namespace Modules.Integrations.Seq.Services;

public sealed record SeqHealthSnapshot(
    string Status,
    bool SinkConfigured,
    bool Reachable,
    DateTimeOffset? LastCheckedAtUtc,
    DateTimeOffset? LastSuccessAtUtc,
    string? WarningCode);

public interface ISeqHealthReader
{
    SeqHealthSnapshot GetHealth();
}

public sealed class SeqHealthState(
    SeqDiagnosticsOptions options,
    SeqSecretResolver secrets) : ISeqHealthReader
{
    private readonly object _gate = new();
    private SeqHealthSnapshot _snapshot = Initial(options, secrets);

    public SeqHealthSnapshot GetHealth()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    public (SeqHealthSnapshot Previous, SeqHealthSnapshot Current) RecordReady(
        DateTimeOffset observedAtUtc)
    {
        lock (_gate)
        {
            var previous = _snapshot;
            _snapshot = previous with
            {
                Status = "ready",
                Reachable = true,
                LastCheckedAtUtc = observedAtUtc,
                LastSuccessAtUtc = observedAtUtc,
                WarningCode = null
            };
            return (previous, _snapshot);
        }
    }

    public (SeqHealthSnapshot Previous, SeqHealthSnapshot Current) RecordUnavailable(
        DateTimeOffset observedAtUtc,
        string warningCode)
    {
        lock (_gate)
        {
            var previous = _snapshot;
            _snapshot = previous with
            {
                Status = "unavailable",
                Reachable = false,
                LastCheckedAtUtc = observedAtUtc,
                WarningCode = warningCode
            };
            return (previous, _snapshot);
        }
    }


    public (SeqHealthSnapshot Previous, SeqHealthSnapshot Current) RecordStoppedIntentionally(
        DateTimeOffset observedAtUtc)
    {
        lock (_gate)
        {
            var previous = _snapshot;
            _snapshot = previous with
            {
                Status = "stopped-intentionally",
                Reachable = false,
                LastCheckedAtUtc = observedAtUtc,
                WarningCode = null
            };
            return (previous, _snapshot);
        }
    }

    public (SeqHealthSnapshot Previous, SeqHealthSnapshot Current) RecordRuntimeAbsent(
        DateTimeOffset observedAtUtc)
    {
        lock (_gate)
        {
            var previous = _snapshot;
            _snapshot = previous with
            {
                Status = "runtime-absent",
                Reachable = false,
                LastCheckedAtUtc = observedAtUtc,
                WarningCode = null
            };
            return (previous, _snapshot);
        }
    }

    private static SeqHealthSnapshot Initial(
        SeqDiagnosticsOptions options,
        SeqSecretResolver secrets)
    {
        if (!options.SinkEnabled)
        {
            return new SeqHealthSnapshot(
                "optional-disabled",
                SinkConfigured: false,
                Reachable: false,
                LastCheckedAtUtc: null,
                LastSuccessAtUtc: null,
                WarningCode: null);
        }

        var errors = SeqDiagnosticsOptionsValidator.Validate(options);
        var apiKey = secrets.ResolveApiKey(options);
        if (errors.Count > 0 || !apiKey.Available)
        {
            return new SeqHealthSnapshot(
                "configuration-error",
                SinkConfigured: false,
                Reachable: false,
                LastCheckedAtUtc: null,
                LastSuccessAtUtc: null,
                WarningCode: apiKey.WarningCode ??
                    "diagnostics.seq_configuration_invalid");
        }

        return new SeqHealthSnapshot(
            "configured-unprobed",
            SinkConfigured: true,
            Reachable: false,
            LastCheckedAtUtc: null,
            LastSuccessAtUtc: null,
            WarningCode: null);
    }
}
