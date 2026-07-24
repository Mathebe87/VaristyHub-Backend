using System.Data;
using Dapper;

namespace VarsityHub.Modules.Applications;

/// <summary>
/// Data access for applications.
/// Students can only see/create their own (RLS enforced).
/// Uni-admins can view applications for their university.
/// </summary>
public sealed class ApplicationRepo(SupabaseDb db, IUserContext me)
{
    /// <summary>
    /// Create a new application. Must have paid the application fee.
    /// RLS policy: students can only insert with their own student_id = auth.uid().
    /// </summary>
    public Task<Guid> CreateAsync(NewApplication a) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            // Gate: student must have a PAID application-fee payment
            var paid = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
                select exists(select 1 from public.payments
                              where student_id = auth.uid() and status = 'paid')
            """, transaction: tx));
            if (!paid)
                throw new InvalidOperationException("Application fee not paid. Please complete payment first.");

            // INSERT: RLS "applications: student create" ensures student_id = auth.uid()
            var appId = await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.applications (student_id, university_id, programme_id, status, aps_at_apply)
                values (auth.uid(), @UniversityId, @ProgrammeId, 'submitted',
                        (select aps from public.student_aps where student_id = auth.uid() limit 1))
                returning id
            """, a, tx));

            return appId;
        });

    /// <summary>
    /// Get all applications for the current student.
    /// RLS: only sees their own applications.
    /// </summary>
    public Task<IEnumerable<ApplicationSummary>> GetMyApplicationsAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<ApplicationSummary>(new CommandDefinition("""
                select a.id, u.name as UniversityName, p.name as ProgrammeName,
                       a.status, a.created_at as CreatedAt
                from public.applications a
                inner join public.universities u on a.university_id = u.id
                inner join public.programmes p on a.programme_id = p.id
                where a.student_id = auth.uid()
                order by a.created_at desc
            """, transaction: tx)));

    /// <summary>
    /// Get details for a specific application (student's own only, via RLS).
    /// </summary>
    public Task<ApplicationDetail?> GetByIdAsync(Guid id) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryFirstOrDefaultAsync<ApplicationDetail>(new CommandDefinition("""
                select id, student_id as StudentId, university_id as UniversityId,
                       programme_id as ProgrammeId, status, aps_at_apply as ApsAtApply,
                       created_at as CreatedAt, updated_at as UpdatedAt
                from public.applications
                where id = @id and student_id = auth.uid()
            """, new { id }, tx)));

    /// <summary>
    /// Update application status (admin/uni-admin only).
    /// Runs via service role (RLS bypassed).
    /// </summary>
    public Task UpdateStatusAsync(Guid id, string status) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            // Status values the uni-admin may set (must match the application_status enum).
            var validStatuses = new[]
            {
                "under_review", "pending_documents", "approved", "waitlisted", "rejected", "withdrawn"
            };
            if (!validStatuses.Contains(status))
                throw new ArgumentException($"Invalid status '{status}'");

            await c.ExecuteAsync(new CommandDefinition("""
                update public.applications
                set status = @status::application_status,
                    decision_at = case when @status in ('approved','waitlisted','rejected') then now() else decision_at end,
                    updated_at = now()
                where id = @id
            """, new { id, status }, tx));
            return 0;
        });

    /// <summary>
    /// Get applications for a specific university (uni-admin view).
    /// Runs via service role but filters by university_id.
    /// </summary>
    public Task<IEnumerable<ApplicationDetail>> GetByUniversityAsync(Guid universityId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<ApplicationDetail>(new CommandDefinition("""
                select id, student_id as StudentId, university_id as UniversityId,
                       programme_id as ProgrammeId, status, aps_at_apply as ApsAtApply,
                       created_at as CreatedAt, updated_at as UpdatedAt
                from public.applications
                where university_id = @universityId
                order by created_at desc
            """, new { universityId }, tx)));

    /// <summary>
    /// Add a document to an application (resume, transcript, etc).
    /// Invokes POST /api/applications/{id}/documents.
    /// </summary>
    public Task AddDocumentAsync(Guid applicationId, Guid documentId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            // Ensure this is the user's application
            var owns = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
                select exists(select 1 from public.applications where id = @applicationId and student_id = auth.uid())
            """, new { applicationId }, tx));
            if (!owns)
                throw new UnauthorizedAccessException("You do not own this application.");

            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.application_documents (application_id, document_id)
                values (@applicationId, @documentId)
            """, new { applicationId, documentId }, tx));
            return 0;
        });
}
