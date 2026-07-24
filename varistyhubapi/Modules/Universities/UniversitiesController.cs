using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VarsityHub.Modules.Universities;

/// <summary>
/// Universities catalog and favorites endpoint.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class UniversitiesController(UniversityRepo repo) : ControllerBase
{
    /// <summary>
    /// Search universities by name and province.
    /// Returns public catalog enriched with programme/faculty counts.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<University>>> Search(
        [FromQuery] string? q,
        [FromQuery] string? province)
    {
        var results = await repo.SearchAsync(q, province);
        return Ok(results);
    }

    /// <summary>
    /// Get a single university by ID.
    /// Includes programme and faculty counts.
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<University>> GetById(Guid id)
    {
        var uni = await repo.GetByIdAsync(id);
        if (uni is null) return NotFound();
        return Ok(uni);
    }

    /// <summary>
    /// Add a university to the current user's favorites.
    /// Requires authentication.
    /// </summary>
    [HttpPost("{id}/favourite")]
    [Authorize]
    public async Task<IActionResult> AddFavorite(Guid id)
    {
        await repo.AddFavoriteAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Remove a university from the current user's favorites.
    /// </summary>
    [HttpDelete("{id}/favourite")]
    [Authorize]
    public async Task<IActionResult> RemoveFavorite(Guid id)
    {
        await repo.RemoveFavoriteAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Get all universities favorited by the current user.
    /// </summary>
    [HttpGet("favourites/my")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<University>>> GetFavorites()
    {
        var favorites = await repo.GetFavoritesAsync();
        return Ok(favorites);
    }
}
