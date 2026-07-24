using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VarsityHub.Modules.Me;

/// <summary>
/// The authenticated student's own profile, settings, NSC results, APS, and eligible programmes.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public sealed class MeController(MeRepo repo, EligibilityRepo eligibility) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeProfile>> GetProfile()
    {
        var profile = await repo.GetProfileAsync();
        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpPatch]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfile body)
    {
        await repo.UpdateProfileAsync(body);
        return NoContent();
    }

    [HttpGet("settings")]
    public async Task<ActionResult<UserSettingsDto>> GetSettings()
        => Ok(await repo.GetSettingsAsync() ?? new UserSettingsDto("{}", "system", "en"));

    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettings body)
    {
        await repo.UpsertSettingsAsync(body);
        return NoContent();
    }

    [HttpGet("results")]
    public async Task<ActionResult<IEnumerable<StudentResultDto>>> GetResults()
        => Ok(await repo.GetResultsAsync());

    [HttpPut("results")]
    public async Task<IActionResult> ReplaceResults([FromBody] List<ResultInput> body)
    {
        await repo.ReplaceResultsAsync(body);
        return NoContent();
    }

    [HttpGet("aps")]
    public async Task<ActionResult<object>> GetAps()
        => Ok(new { aps = await repo.GetApsAsync() });

    [HttpGet("eligible-programmes")]
    public async Task<ActionResult<IEnumerable<EligibleProgramme>>> GetEligibleProgrammes()
        => Ok(await eligibility.EligibleProgrammesAsync());
}
