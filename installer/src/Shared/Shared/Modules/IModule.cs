using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Modules;

public interface IModule
{
    void Register(IServiceCollection services, IConfiguration configuration);

    // Optional: add API endpoints via route groups
    void MapEndpoints(IEndpointRouteBuilder endpoints) { }

    // Optional: middleware, diagnostics, etc.
    void Use(WebApplication app) { }
}