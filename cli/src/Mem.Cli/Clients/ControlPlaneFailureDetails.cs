using System.Net;

namespace Mem.Cli.Clients;

/// <summary>
/// Produces safe CLI-visible failure details. Raw control-plane response bodies
/// and transport exception text can contain operational or secret-bearing
/// information, so they must never cross into human output or JSON results.
/// </summary>
internal static class ControlPlaneFailureDetails
{
    public const string EmptyResponse =
        "The control plane returned an empty response.";

    public const string Unreachable =
        "The control plane could not be reached. Check the private management connection and try again.";

    public static string ForHttpStatus(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "The control plane rejected the CLI device session. Run mem account show, then mem login --device if the session has expired or was revoked.",
            HttpStatusCode.Forbidden =>
                "The control plane refused this request (HTTP 403). The operation may require a stronger role or recent identity verification. MEM CLI fails closed here and does not accept passwords, TOTP codes, recovery codes, bearer tokens, or device credentials through flags, environment variables, or stdin.",
            _ =>
                $"The control plane request failed (HTTP {(int)statusCode})."
        };
}
