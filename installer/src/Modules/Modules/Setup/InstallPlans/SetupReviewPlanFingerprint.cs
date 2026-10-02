using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Modules.Setup.InstallPlans;

public static class SetupReviewPlanFingerprint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(InstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var intent = plan with { Review = null };
        var json = JsonSerializer.Serialize(intent, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    public static bool Matches(InstallPlan plan)
    {
        if (plan.Review is null || string.IsNullOrWhiteSpace(plan.Review.PlanSha256))
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Compute(plan));
        var supplied = Encoding.ASCII.GetBytes(plan.Review.PlanSha256.Trim().ToLowerInvariant());
        return expected.Length == supplied.Length &&
            CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
