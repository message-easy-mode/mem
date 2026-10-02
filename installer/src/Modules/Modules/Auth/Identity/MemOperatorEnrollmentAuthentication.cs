namespace Modules.Auth.Identity;

/// <summary>
/// Cookie and claim names for the short-lived, one-time local-operator
/// enrolment journey. This scheme never authorizes normal control-plane routes
/// and is accepted only by the enrolment endpoints.
/// </summary>
public static class MemOperatorEnrollmentAuthentication
{
    public const string Scheme = "mem.enrollment";

    public const string GrantIdClaimType = "mem_operator_enrollment_grant_id";
}
