namespace Shared.Diagnostics;

public interface IMemDiagnosticHealthReader
{
    MemDiagnosticStoreHealth GetHealth();
}
