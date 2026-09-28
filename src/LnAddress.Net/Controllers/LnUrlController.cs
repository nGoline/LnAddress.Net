using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace LnAddress.Net.Controllers;

using Interfaces;
using Models.Responses;

[ApiController]
[Route("[controller]")]
public partial class LnUrlController(ILightningService lightningService, IConfiguration configuration) : ControllerBase
{
    [HttpGet("callback/{username}")]
    public async Task<ActionResult<LnUrlCallbackResponse>> Get(string username, [FromQuery] long amount, [FromQuery] string? comment)
    {
        try
        {
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
            
            if (amount < minSendable || amount > maxSendable)
            {
                return BadRequest(new ErrorResponse("Amount is outside bounds."));
            }

            if (commentAllowed is null)
            {
                comment = null;
            }
            else if(comment?.Length > commentAllowed.Value)
            {
                comment = comment[..commentAllowed.Value];
            }

            var invoice = await lightningService.FetchInvoiceAsync(amount, username, comment);
            var verify = $"https://{Request.Host.Value}/lnurl/verify/{invoice.PaymentHash}";
            return new LnUrlCallbackResponse(invoice.PaymentRequest, verify);
        }
        catch (Exception e)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse(e.Message));
        }
    }

    [HttpGet("verify/{paymentHash}")]
    public async Task<ActionResult<LnUrlVerifyResponse>> Verify(string paymentHash)
    {
        try
        {
            if (!PaymentHashRegex().IsMatch(paymentHash))
            {
                return NotFound(new ErrorResponse("Not found"));
            }

            var invoice = await lightningService.LookupInvoiceAsync(paymentHash.ToLowerInvariant());
            if (invoice is null)
            {
                return NotFound(new ErrorResponse("Not found"));
            }

            return new LnUrlVerifyResponse(invoice.PaymentRequest, invoice.Settled, invoice.Preimage);
        }
        catch (Exception e)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse(e.Message));
        }
    }

    [GeneratedRegex(@"^[0-9a-fA-F]{64}\z")]
    private static partial Regex PaymentHashRegex();
}
