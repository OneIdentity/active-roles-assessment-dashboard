using System.Text.Json;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ActiveRolesDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DrillDownController : ControllerBase
{
    private readonly ActiveRolesService _arService;

    public DrillDownController(ActiveRolesService arService)
    {
        _arService = arService;
    }

    private string? GetToken()
    {
        return HttpContext.Session.GetString("AccessToken");
    }

    [HttpGet("details/{objectGuid}")]
    public async Task<IActionResult> GetDetails(string objectGuid)
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var result = await _arService.GetObjectDetailsAsync(token, objectGuid);
        if (result == null)
            return Ok(new { error = "No data" });

        return Ok(JsonSerializer.Deserialize<object>(result.RootElement.GetRawText()));
    }

    [HttpGet("children/{objectGuid}")]
    public async Task<IActionResult> GetChildren(string objectGuid)
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var result = await _arService.GetChildrenAsync(token, objectGuid);
        if (result == null)
            return Ok(new { error = "No data" });

        return Ok(JsonSerializer.Deserialize<object>(result.RootElement.GetRawText()));
    }

    [HttpGet("domains")]
    public async Task<IActionResult> GetDomains()
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return Unauthorized();
        var result = await _arService.GetDomainsAsync(token);
        return Ok(result);
    }

    [HttpGet("servers")]
    public async Task<IActionResult> GetServers()
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return Unauthorized();
        var result = await _arService.GetServersAsync(token);
        return Ok(result);
    }

    [HttpGet("dynamicgroups")]
    public async Task<IActionResult> GetDynamicGroups()
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return Unauthorized();
        var result = await _arService.GetDynamicGroupsAsync(token);
        return Ok(result);
    }

    [HttpGet("managedunits")]
    public async Task<IActionResult> GetManagedUnits()
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return Unauthorized();
        var result = await _arService.GetManagedUnitsAsync(token);
        return Ok(result);
    }

    [HttpGet("workflows")]
    public async Task<IActionResult> GetWorkflows()
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return Unauthorized();
        var result = await _arService.GetWorkflowsAsync(token);
        return Ok(result);
    }

    [HttpGet("virtualattrs")]
    public async Task<IActionResult> GetVirtualAttributes()
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return Unauthorized();
        var result = await _arService.GetVirtualAttributesAsync(token);
        return Ok(result);
    }

    [HttpGet("policies")]
    public async Task<IActionResult> GetPolicies()
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return Unauthorized();
        var result = await _arService.GetPolicyObjectsAsync(token);
        return Ok(result);
    }

    [HttpGet("accesstemplates")]
    public async Task<IActionResult> GetAccessTemplates()
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return Unauthorized();
        var result = await _arService.GetAccessTemplatesAsync(token);
        return Ok(result);
    }
}
