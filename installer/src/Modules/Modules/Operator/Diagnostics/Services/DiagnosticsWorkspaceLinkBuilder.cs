using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsWorkspaceLinkBuilder
{
    public string? Build(MemDiagnosticResource? resource)
    {
        if (resource is null)
        {
            return null;
        }

        var kind = resource.Kind.Trim().ToLowerInvariant();
        var id = Uri.EscapeDataString(resource.Id);
        var stackRouteId = !string.IsNullOrWhiteSpace(resource.StackSlug)
            ? Uri.EscapeDataString(resource.StackSlug)
            : !string.IsNullOrWhiteSpace(resource.StackId)
                ? Uri.EscapeDataString(resource.StackId)
                : null;

        return kind switch
        {
            "restore" or "restore-attempt" or "restore-session" => $"/restores/{id}",
            "migration" or "migration-session" => $"/migrations/{id}",
            "stack" or "runtime-stack" => $"/stacks/{id}",
            "federation" when stackRouteId is not null => $"/stacks/{stackRouteId}/federation",
            "turn" when stackRouteId is not null => $"/stacks/{stackRouteId}/services",
            "service" when stackRouteId is not null => $"/stacks/{stackRouteId}",
            "coturn" or "turn-platform" => "/services/coturn",
            "installation" or "install" or "setup" => "/setup",
            _ => null
        };
    }
}
