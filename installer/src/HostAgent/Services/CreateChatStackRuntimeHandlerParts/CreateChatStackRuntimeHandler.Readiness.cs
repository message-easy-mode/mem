using HostAgent.Runtime.Readiness;

namespace HostAgent.Services;

public sealed partial class CreateChatStackRuntimeHandler
{
    private static string ReadinessSuccess(
        RuntimeReadinessVerificationResult? readiness,
        string code)
    {
        if (readiness is null)
        {
            return "false";
        }

        var check = readiness.Checks.FirstOrDefault(x =>
            string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));

        return check?.Success == true
            ? "true"
            : "false";
    }
}
