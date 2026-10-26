using ChatGPTClone.Api.Services.Ai;
using ChatGPTClone.Api.Services.Audio;
using ChatGPTClone.Api.Services.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatGPTClone.Api.Controllers;

/// <summary>The chat models users may pick (the Ai:Models allow-list). Ids and labels only: no model names or keys.</summary>
[ApiController]
[Route("api/models")]
[Authorize]
[Produces("application/json")]
public sealed class ModelsController(IAiModelCatalog models, IWebSearchProvider webSearch, ISpeechToText speech) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AiModelsDto), StatusCodes.Status200OK)]
    public ActionResult<AiModelsDto> List() => Ok(models.Describe() with { WebSearchAvailable = webSearch.IsConfigured, VoiceInputAvailable = speech.IsConfigured });
}
