namespace Shared.Diagnostics;

public interface IMemDiagnosticEventReader
{
    Task<MemDiagnosticEventPage> QueryAsync(
        MemDiagnosticQuery query,
        CancellationToken cancellationToken = default);
}
