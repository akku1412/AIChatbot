using ChatGPTClone.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace ChatGPTClone.Api.Controllers;

/// <summary>Liveness check used to confirm the API and its database are reachable (e.g. from Angular or Swagger).</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class HealthController(IWebHostEnvironment environment, AppDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
    {
        var databaseReachable = await db.Database.CanConnectAsync(cancellationToken);

        var response = new HealthResponse(
            databaseReachable ? "Healthy" : "Unhealthy",
            databaseReachable ? "Connected" : "Unreachable",
            environment.EnvironmentName,
            DateTime.UtcNow);

        return databaseReachable
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    public sealed record HealthResponse(string Status, string Database, string Environment, DateTime UtcTime);
}
