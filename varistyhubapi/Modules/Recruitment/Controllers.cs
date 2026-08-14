using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Recruitment;

// ============================ Super-admin: Jobs ============================
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "Admin")]
public sealed class AdminJobsController(RecruitmentRepo repo, IUserContext me, IAuditService audit) : ControllerBase
{
    private Guid Caller => Guid.Parse(me.UserId!);

    [HttpGet("jobs")]
    public async Task<ActionResult<IEnumerable<AdminJob>>> List() => Ok(await repo.ListJobsAsync(null));

    [HttpPost("jobs")]
    public async Task<ActionResult<AdminJob>> Create([FromBody] NewJob body)
    {
        var job = await repo.CreateJobAsync(body, Caller, null);
        await audit.LogAsync(Caller, "job.created", "job", job.Id, new { job.Title });
        return Ok(job);
    }

    [HttpPatch("jobs/{id}")]
    public async Task<ActionResult<AdminJob>> Update(Guid id, [FromBody] UpdateJob body)
    {
        var job = await repo.UpdateJobAsync(id, body, null);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpDelete("jobs/{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await repo.DeleteJobAsync(id, null)) return NotFound();
        await audit.LogAsync(Caller, "job.deleted", "job", id);
        return NoContent();
    }

    [HttpGet("jobs/{id}/applicants")]
    public async Task<ActionResult<IEnumerable<JobApplicant>>> Applicants(Guid id)
    {
        var applicants = await repo.JobApplicantsAsync(id, null);
        return applicants is null ? NotFound() : Ok(applicants);
    }

    [HttpPatch("job-applications/{id}")]
    public async Task<ActionResult<JobApplicant>> UpdateAppStatus(Guid id, [FromBody] UpdateJobAppStatus body)
    {
        try
        {
            var applicant = await repo.UpdateJobAppStatusAsync(id, body.Status, null);
            return applicant is null ? NotFound() : Ok(applicant);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}

// ========================== Super-admin: Bursaries =========================
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "Admin")]
public sealed class AdminBursariesController(RecruitmentRepo repo, IUserContext me, IAuditService audit) : ControllerBase
{
    private Guid Caller => Guid.Parse(me.UserId!);

    [HttpGet("bursaries")]
    public async Task<ActionResult<IEnumerable<AdminBursary>>> List() => Ok(await repo.ListBursariesAsync());

    [HttpPost("bursaries")]
    public async Task<ActionResult<AdminBursary>> Create([FromBody] NewBursary body)
    {
        var b = await repo.CreateBursaryAsync(body, Caller);
        await audit.LogAsync(Caller, "bursary.created", "bursary", b.Id, new { b.Name });
        return Ok(b);
    }

    [HttpPatch("bursaries/{id}")]
    public async Task<ActionResult<AdminBursary>> Update(Guid id, [FromBody] UpdateBursary body)
    {
        var b = await repo.UpdateBursaryAsync(id, body);
        return b is null ? NotFound() : Ok(b);
    }

    [HttpDelete("bursaries/{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await repo.DeleteBursaryAsync(id)) return NotFound();
        await audit.LogAsync(Caller, "bursary.deleted", "bursary", id);
        return NoContent();
    }

    [HttpGet("bursaries/{id}/applicants")]
    public async Task<ActionResult<IEnumerable<BursaryApplicant>>> Applicants(Guid id)
        => Ok(await repo.BursaryApplicantsAsync(id));

    [HttpPatch("bursary-applications/{id}")]
    public async Task<ActionResult<BursaryApplicant>> UpdateAppStatus(Guid id, [FromBody] UpdateBursaryAppStatus body)
    {
        try
        {
            var applicant = await repo.UpdateBursaryAppStatusAsync(id, body.Status);
            return applicant is null ? NotFound() : Ok(applicant);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}

// ============================= Employer (self) =============================
[ApiController]
[Route("api/employer")]
[Authorize(Policy = "Employer")]
public sealed class EmployerController(RecruitmentRepo repo, IUserContext me) : ControllerBase
{
    private Guid Caller => Guid.Parse(me.UserId!);

    [HttpGet("profile")]
    public async Task<ActionResult<EmployerProfile>> GetProfile() => Ok(await repo.GetOrCreateEmployerAsync(Caller));

    [HttpPatch("profile")]
    public async Task<ActionResult<EmployerProfile>> UpdateProfile([FromBody] UpdateEmployerProfile body)
    {
        await repo.GetOrCreateEmployerAsync(Caller); // ensure the row exists
        var profile = await repo.UpdateEmployerAsync(Caller, body);
        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<IEnumerable<AdminJob>>> Jobs() => Ok(await repo.ListJobsAsync(Caller));

    [HttpPost("jobs")]
    public async Task<ActionResult<AdminJob>> Create([FromBody] NewJob body)
    {
        var emp = await repo.GetOrCreateEmployerAsync(Caller);     // company inferred from profile
        return Ok(await repo.CreateJobAsync(body, Caller, emp.CompanyName));
    }

    [HttpPatch("jobs/{id}")]
    public async Task<ActionResult<AdminJob>> Update(Guid id, [FromBody] UpdateJob body)
    {
        var job = await repo.UpdateJobAsync(id, body, Caller);      // own only
        return job is null ? NotFound() : Ok(job);
    }

    [HttpDelete("jobs/{id}")]
    public async Task<IActionResult> Delete(Guid id)
        => await repo.DeleteJobAsync(id, Caller) ? NoContent() : NotFound();

    [HttpGet("jobs/{id}/applicants")]
    public async Task<ActionResult<IEnumerable<JobApplicant>>> Applicants(Guid id)
    {
        var applicants = await repo.JobApplicantsAsync(id, Caller);
        return applicants is null ? NotFound() : Ok(applicants);
    }

    [HttpPatch("job-applications/{id}")]
    public async Task<ActionResult<JobApplicant>> UpdateAppStatus(Guid id, [FromBody] UpdateJobAppStatus body)
    {
        try
        {
            var applicant = await repo.UpdateJobAppStatusAsync(id, body.Status, Caller);  // own job only
            return applicant is null ? NotFound() : Ok(applicant);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
