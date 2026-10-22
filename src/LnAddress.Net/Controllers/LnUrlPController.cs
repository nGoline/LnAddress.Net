using Microsoft.AspNetCore.Mvc;

namespace LnAddress.Net.Controllers;

using Helpers;
using Models.Responses;

[ApiController]
[Route(".well-known/[controller]")]
public class LnUrlPController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("{username}")]
    public ActionResult<LnAddressWellKnownResponse> Get(string username)
    {
        try
        {
            if (!LnUrlMetadata.TryNormalizeUsername(username, out var normalizedUsername))
            {
                return NotFound(new ErrorResponse("Not found"));
            }

            // Get the host called
            var domain = Request.Host.Value;

            if (!long.TryParse(configuration["Invoice:MinSendable"], out var minSendable))
            {
                minSendable = 1_000;
            }
            
            if (!long.TryParse(configuration["Invoice:MaxSendable"], out var maxSendable))
            {
                maxSendable = 100_000_000;
            }

            int? commentAllowed = null;
            if (int.TryParse(configuration["Invoice:MaxCommentAllowed"], out var commentAllowedFromConfig))
            {
                commentAllowed = commentAllowedFromConfig;
            }
            
            return new LnAddressWellKnownResponse($"https://{domain}/lnurl/callback/{normalizedUsername}", minSendable, maxSendable)
                .WithMetadata(LnUrlMetadata.Build(normalizedUsername, Request.Host.Host))
                .WithCommentAllowed(commentAllowed);
        }
        catch (Exception e)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse(e.Message));
        }
    }
}