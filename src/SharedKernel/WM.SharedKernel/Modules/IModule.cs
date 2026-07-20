using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WM.SharedKernel.Modules;

/// <summary>
/// Contract every WM feature module implements. The API host discovers modules,
/// lets each register its own services/DbContext and map its own endpoints.
/// Modules must not reference each other's internals — only contracts/events.
/// </summary>
public interface IModule
{
    string Name { get; }

    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
