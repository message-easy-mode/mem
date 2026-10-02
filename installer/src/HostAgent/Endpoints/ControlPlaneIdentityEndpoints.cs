using Carter;

namespace HostAgent.Endpoints;

/// <summary>
/// Legacy custom control-plane identity routes were deliberately retired from
/// the HTTP surface by SEC-AUTH-03A. Their old user/password model must not
/// run beside ASP.NET Core Identity. The domain code remains temporarily as
/// a migration/reference source until SEC-AUTH-04 replaces its management
/// capabilities with named operator administration.
/// </summary>
public sealed class ControlPlaneIdentityEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // Intentionally no routes.
    }
}
