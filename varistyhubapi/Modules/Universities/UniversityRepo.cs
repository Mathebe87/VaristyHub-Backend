using System.Data;
using Dapper;

namespace VarsityHub.Modules.Universities;

/// <summary>
/// Data access for universities.
/// Public catalog allows anon/authenticated read via RLS.
/// </summary>
public sealed class UniversityRepo(SupabaseDb db, IUserContext me)
{
    /// <summary>
    /// Search universities by name and province. RLS allows anonymous and authenticated users to read.
    /// </summary>
    public Task<IEnumerable<University>> SearchAsync(string? q, string? province) =>
        db.AsUserAsync(me.UserId ?? "", me.Email, async (c, tx) =>
            await c.QueryAsync<University>(new CommandDefinition("""
                select id, name, short_code as ShortCode, province,
                       min_aps as MinAps, tuition_from as TuitionFrom,
                       programmes_count as ProgrammesCount, faculties_count as FacultiesCount
                from public.universities
                where (@q is null or name ilike '%' || @q || '%')
                  and (@province is null or @province = 'All' or province = @province)
                order by name
            """, new { q, province }, tx)));

    /// <summary>
    /// Get a single university by ID.
    /// </summary>
    public Task<University?> GetByIdAsync(Guid id) =>
        db.AsUserAsync(me.UserId ?? "", me.Email, async (c, tx) =>
            await c.QueryFirstOrDefaultAsync<University>(new CommandDefinition("""
                select id, name, short_code as ShortCode, province,
                       min_aps as MinAps, tuition_from as TuitionFrom,
                       programmes_count as ProgrammesCount, faculties_count as FacultiesCount
                from public.universities
                where id = @id
            """, new { id }, tx)));

    /// <summary>
    /// Add a university to the current user's favorites.
    /// RLS allows authenticated users to insert their own favorite_universities rows.
    /// </summary>
    public Task AddFavoriteAsync(Guid universityId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.university_favourites (student_id, university_id)
                values (auth.uid(), @universityId)
                on conflict do nothing
            """, new { universityId }, tx));
            return 0;
        });

    /// <summary>
    /// Remove a university from the current user's favorites.
    /// </summary>
    public Task RemoveFavoriteAsync(Guid universityId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                delete from public.university_favourites
                where student_id = auth.uid() and university_id = @universityId
            """, new { universityId }, tx));
            return 0;
        });

    /// <summary>
    /// Get all universities favorited by the current user.
    /// </summary>
    public Task<IEnumerable<University>> GetFavoritesAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<University>(new CommandDefinition("""
                select u.id, u.name, u.short_code as ShortCode, u.province,
                       u.min_aps as MinAps, u.tuition_from as TuitionFrom,
                       u.programmes_count as ProgrammesCount, u.faculties_count as FacultiesCount
                from public.universities u
                inner join public.university_favourites f on u.id = f.university_id
                where f.student_id = auth.uid()
                order by u.name
            """, transaction: tx)));
}
