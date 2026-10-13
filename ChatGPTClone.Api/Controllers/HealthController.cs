using Microsoft.AspNetCore.Mvc;

namespace ChatGPTClone.Api.Controllers;

/// <summary>Lightweight liveness check used to confirm the API is reachable (e.g. from Angular or Swagger).</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class HealthController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get() =>
        Ok(new HealthResponse("Healthy", environment.EnvironmentName, DateTime.UtcNow));

    public sealed record HealthResponse(string Status, string Environment, DateTime UtcTime);
}
