using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using HostCraft.Api.Models.Domains;
using HostCraft.Api.Models.Shared;
using HostCraft.Api.Services;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/applications/{applicationId}/[controller]")]
[Authorize]
public class DomainsController : ControllerBase
{
    private readonly IDomainsWorkflowService _workflow;
    private readonly ILogger<DomainsController> _logger;

    public DomainsController(IDomainsWorkflowService workflow, ILogger<DomainsController> logger)
    {
        _workflow = workflow;
        _logger = logger;
    }

    /// <summary>
    /// Get all domains for an application
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<DomainDto>>> GetDomains(
        int applicationId,
        [FromQuery] bool paged = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _workflow.GetDomainsAsync(applicationId, paged, Math.Max(1, page), Math.Clamp(pageSize, 1, 200), HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Get a specific domain
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<DomainDto>> GetDomain(int applicationId, int id)
    {
        var result = await _workflow.GetDomainAsync(applicationId, id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Add a new domain to an application
    /// </summary>
    [HttpPost("add")]
    public async Task<ActionResult<DomainDto>> CreateDomain(int applicationId, [FromBody] CreateDomainRequest request)
    {
        var result = await _workflow.CreateDomainAsync(applicationId, request, HttpContext.RequestAborted);
        return ToActionResult(result, nameof(GetDomain));
    }

    /// <summary>
    /// Update a domain
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<DomainDto>> UpdateDomain(int applicationId, int id, [FromBody] UpdateDomainRequest request)
    {
        var result = await _workflow.UpdateDomainAsync(applicationId, id, request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Delete a domain
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteDomain(int applicationId, int id)
    {
        var result = await _workflow.DeleteDomainAsync(applicationId, id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Validate DNS for a domain
    /// </summary>
    [HttpPost("{id:int}/validate-dns")]
    public async Task<ActionResult<DnsValidationResult>> ValidateDns(int applicationId, int id)
    {
        var result = await _workflow.ValidateDnsAsync(applicationId, id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Validate DNS for all domains of an application
    /// </summary>
    [HttpPost("validate-all-dns")]
    public async Task<ActionResult<IEnumerable<DnsValidationResult>>> ValidateAllDns(int applicationId)
    {
        var result = await _workflow.ValidateAllDnsAsync(applicationId, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Get certificate information for an application.
    /// </summary>
    [HttpGet("certificates")]
    public async Task<ActionResult<List<CertificateInfo>>> GetCertificates(int applicationId)
    {
        var result = await _workflow.GetCertificatesAsync(applicationId, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Manually renew a certificate.
    /// </summary>
    [HttpPost("certificates/{certificateId}/renew")]
    public async Task<ActionResult<RenewCertificateResponse>> RenewCertificate(int applicationId, int certificateId)
    {
        var result = await _workflow.RenewCertificateAsync(applicationId, certificateId, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Delete a certificate.
    /// </summary>
    [HttpDelete("certificates/{certificateId}")]
    public async Task<IActionResult> DeleteCertificate(int applicationId, int certificateId)
    {
        var result = await _workflow.DeleteCertificateAsync(applicationId, certificateId, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    private ActionResult ToActionResult(ApiActionResult result)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }

    private ActionResult ToActionResult<T>(ApiActionResult<T> result)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode, result.Data);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }

    private ActionResult ToActionResult<T>(ApiActionResult<T> result, string actionName)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status201Created)
            {
                return CreatedAtAction(actionName, new { applicationId = (result.Data as DomainDto)?.ApplicationId, id = (result.Data as DomainDto)?.Id }, result.Data);
            }

            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode, result.Data);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }
}
