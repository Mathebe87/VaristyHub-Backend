using System.Data;
using Dapper;
using VarsityHub.Services;

namespace VarsityHub.Modules.Recruitment;

/// <summary>
/// Jobs + bursaries administration and employer self-service. Runs on the service path;
/// role gating is the controller's job. Job operations take an optional <c>owner</c>:
/// null = super-admin (all jobs), otherwise scoped to jobs the employer posted.
/// </summary>
public sealed class RecruitmentRepo(SupabaseDb db, INotificationService notify)
{
    private static readonly string[] JobAppStatuses = ["applied", "viewed", "interview", "offer", "rejected", "withdrawn"];
    private static readonly string[] BursaryAppStatuses = ["draft", "submitted", "under_review", "approved", "rejected"];

    private const string JobCols = """
        select j.id, j.title, j.company, j.type::text as Type, j.location, j.salary_text as SalaryText,
               j.description, j.tags as Tags, j.is_remote as IsRemote, j.closes_on as ClosesOn, j.is_active as IsActive,
               (select count(*)::int from public.job_applications ja where ja.job_id = j.id) as ApplicantCount
        from public.jobs j
        """;

    // ---- Jobs ----
    public Task<IEnumerable<AdminJob>> ListJobsAsync(Guid? owner) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<AdminJob>(new CommandDefinition(
                $"{JobCols} where (@owner is null or j.posted_by = @owner) order by j.created_at desc",
                new { owner }, tx)));

    public Task<AdminJob?> GetJobAsync(Guid id, Guid? owner) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryFirstOrDefaultAsync<AdminJob>(new CommandDefinition(
                $"{JobCols} where j.id = @id and (@owner is null or j.posted_by = @owner)",
                new { id, owner }, tx)));

    public async Task<AdminJob> CreateJobAsync(NewJob n, Guid caller, string? companyOverride)
    {
        var id = await db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.jobs (title, company, type, location, salary_text, description, tags, is_remote, closes_on, is_active, posted_by)
                values (@Title, @Company, @Type::job_type, @Location, @SalaryText, @Description,
                        coalesce(@Tags::text[], '{}'::text[]), coalesce(@IsRemote, false), @ClosesOn, true, @caller)
                returning id
            """, new
            {
                n.Title, Company = companyOverride ?? n.Company ?? "", n.Type, n.Location, n.SalaryText,
                n.Description, n.Tags, n.IsRemote, n.ClosesOn, caller
            }, tx)));
        return (await GetJobAsync(id, null))!;
    }

    public async Task<AdminJob?> UpdateJobAsync(Guid id, UpdateJob u, Guid? owner)
    {
        var affected = await db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("""
                update public.jobs set
                    title = coalesce(@Title, title), company = coalesce(@Company, company),
                    type = coalesce(@Type::job_type, type), location = coalesce(@Location, location),
                    salary_text = coalesce(@SalaryText, salary_text), description = coalesce(@Description, description),
                    tags = coalesce(@Tags::text[], tags), is_remote = coalesce(@IsRemote, is_remote),
                    closes_on = coalesce(@ClosesOn, closes_on), is_active = coalesce(@IsActive, is_active),
                    updated_at = now()
                where id = @id and (@owner is null or posted_by = @owner)
            """, new { id, owner, u.Title, u.Company, u.Type, u.Location, u.SalaryText, u.Description, u.Tags, u.IsRemote, u.ClosesOn, u.IsActive }, tx)));
        return affected == 0 ? null : await GetJobAsync(id, owner);
    }

    public Task<bool> DeleteJobAsync(Guid id, Guid? owner) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition(
                "delete from public.jobs where id = @id and (@owner is null or posted_by = @owner)",
                new { id, owner }, tx)) > 0);

    public Task<IEnumerable<JobApplicant>?> JobApplicantsAsync(Guid jobId, Guid? owner) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            var owns = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from public.jobs where id = @jobId and (@owner is null or posted_by = @owner))",
                new { jobId, owner }, tx));
            if (!owns) return (IEnumerable<JobApplicant>?)null;

            return await c.QueryAsync<JobApplicant>(new CommandDefinition("""
                select ja.id, ja.student_id as StudentId, p.full_name as StudentName, p.email::text as StudentEmail,
                       ja.cv_document_id as CvDocumentId, ja.status::text as Status, ja.applied_at as AppliedAt
                from public.job_applications ja
                join public.profiles p on p.id = ja.student_id
                where ja.job_id = @jobId
                order by ja.applied_at desc
            """, new { jobId }, tx));
        });

    public async Task<JobApplicant?> UpdateJobAppStatusAsync(Guid appId, string status, Guid? owner)
    {
        if (!JobAppStatuses.Contains(status)) throw new ArgumentException($"Invalid status '{status}'");

        var result = await db.AsServiceAsync(async (c, tx) =>
        {
            var row = await c.QueryFirstOrDefaultAsync<(Guid StudentId, string Title)>(new CommandDefinition("""
                update public.job_applications ja set status = @status::job_app_status
                where ja.id = @appId
                  and (@owner is null or exists (select 1 from public.jobs j where j.id = ja.job_id and j.posted_by = @owner))
                returning ja.student_id, (select title from public.jobs j where j.id = ja.job_id)
            """, new { appId, status, owner }, tx));
            if (row.Equals(default((Guid, string)))) return (JobApplicant?)null;

            return await c.QueryFirstOrDefaultAsync<JobApplicant>(new CommandDefinition("""
                select ja.id, ja.student_id as StudentId, p.full_name as StudentName, p.email::text as StudentEmail,
                       ja.cv_document_id as CvDocumentId, ja.status::text as Status, ja.applied_at as AppliedAt
                from public.job_applications ja join public.profiles p on p.id = ja.student_id
                where ja.id = @appId
            """, new { appId }, tx));
        });

        if (result is not null)
            await notify.NotifyAsync(result.StudentId, "job",
                $"Update on your job application", $"Your application is now '{status}'.", "/job-hub");
        return result;
    }

    // ---- Bursaries (super-admin) ----
    private const string BursaryCols = """
        select b.id, b.name, b.provider, b.field::text as Field, b.amount_text as AmountText, b.covers as Covers,
               b.min_aps as MinAps, b.description, b.closes_on as ClosesOn, b.is_active as IsActive,
               (select count(*)::int from public.bursary_applications ba where ba.bursary_id = b.id) as ApplicantCount
        from public.bursaries b
        """;

    public Task<IEnumerable<AdminBursary>> ListBursariesAsync() =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<AdminBursary>(new CommandDefinition($"{BursaryCols} order by b.created_at desc", transaction: tx)));

    public Task<AdminBursary?> GetBursaryAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryFirstOrDefaultAsync<AdminBursary>(new CommandDefinition($"{BursaryCols} where b.id = @id", new { id }, tx)));

    public async Task<AdminBursary> CreateBursaryAsync(NewBursary n, Guid caller)
    {
        var id = await db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.bursaries (name, provider, field, amount_text, covers, min_aps, description, closes_on, is_active, created_by)
                values (@Name, @Provider, @Field::bursary_field, @AmountText, coalesce(@Covers::text[], '{}'::text[]), @MinAps, @Description, @ClosesOn, true, @caller)
                returning id
            """, new { n.Name, n.Provider, n.Field, n.AmountText, n.Covers, n.MinAps, n.Description, n.ClosesOn, caller }, tx)));
        return (await GetBursaryAsync(id))!;
    }

    public async Task<AdminBursary?> UpdateBursaryAsync(Guid id, UpdateBursary u)
    {
        var affected = await db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("""
                update public.bursaries set
                    name = coalesce(@Name, name), provider = coalesce(@Provider, provider),
                    field = coalesce(@Field::bursary_field, field), amount_text = coalesce(@AmountText, amount_text),
                    covers = coalesce(@Covers::text[], covers), min_aps = coalesce(@MinAps, min_aps),
                    description = coalesce(@Description, description), closes_on = coalesce(@ClosesOn, closes_on),
                    is_active = coalesce(@IsActive, is_active), updated_at = now()
                where id = @id
            """, new { id, u.Name, u.Provider, u.Field, u.AmountText, u.Covers, u.MinAps, u.Description, u.ClosesOn, u.IsActive }, tx)));
        return affected == 0 ? null : await GetBursaryAsync(id);
    }

    public Task<bool> DeleteBursaryAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("delete from public.bursaries where id = @id", new { id }, tx)) > 0);

    public Task<IEnumerable<BursaryApplicant>> BursaryApplicantsAsync(Guid bursaryId) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.QueryAsync<BursaryApplicant>(new CommandDefinition("""
                select ba.id, ba.student_id as StudentId, p.full_name as StudentName, p.email::text as StudentEmail,
                       ba.status::text as Status, ba.submitted_at as SubmittedAt
                from public.bursary_applications ba
                join public.profiles p on p.id = ba.student_id
                where ba.bursary_id = @bursaryId
                order by ba.submitted_at desc
            """, new { bursaryId }, tx)));

    public async Task<BursaryApplicant?> UpdateBursaryAppStatusAsync(Guid appId, string status)
    {
        if (!BursaryAppStatuses.Contains(status)) throw new ArgumentException($"Invalid status '{status}'");

        var result = await db.AsServiceAsync(async (c, tx) =>
        {
            var studentId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                update public.bursary_applications set status = @status::bursary_app_status
                where id = @appId returning student_id
            """, new { appId, status }, tx));
            if (studentId is null) return (BursaryApplicant?)null;

            return await c.QueryFirstOrDefaultAsync<BursaryApplicant>(new CommandDefinition("""
                select ba.id, ba.student_id as StudentId, p.full_name as StudentName, p.email::text as StudentEmail,
                       ba.status::text as Status, ba.submitted_at as SubmittedAt
                from public.bursary_applications ba join public.profiles p on p.id = ba.student_id
                where ba.id = @appId
            """, new { appId }, tx));
        });

        if (result is not null)
            await notify.NotifyAsync(result.StudentId, "bursary",
                "Your bursary application was updated", $"Status is now '{status}'.", "/bursaries");
        return result;
    }

    // ---- Employer profile ----
    public Task<EmployerProfile> GetOrCreateEmployerAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.employers (id, company_name)
                values (@id, coalesce((select full_name from public.profiles where id = @id), ''))
                on conflict (id) do nothing
            """, new { id }, tx));
            return await c.QueryFirstAsync<EmployerProfile>(new CommandDefinition("""
                select id, company_name as CompanyName, website, logo_url as LogoUrl, is_verified as IsVerified
                from public.employers where id = @id
            """, new { id }, tx));
        });

    public Task<EmployerProfile?> UpdateEmployerAsync(Guid id, UpdateEmployerProfile u) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                update public.employers set
                    company_name = coalesce(@CompanyName, company_name),
                    website = coalesce(@Website, website), logo_url = coalesce(@LogoUrl, logo_url), updated_at = now()
                where id = @id
            """, new { id, u.CompanyName, u.Website, u.LogoUrl }, tx));
            return await c.QueryFirstOrDefaultAsync<EmployerProfile>(new CommandDefinition("""
                select id, company_name as CompanyName, website, logo_url as LogoUrl, is_verified as IsVerified
                from public.employers where id = @id
            """, new { id }, tx));
        });

    public Task<string?> GetCompanyNameAsync(Guid id) =>
        db.AsServiceAsync(async (c, tx) =>
            await c.ExecuteScalarAsync<string?>(new CommandDefinition(
                "select company_name from public.employers where id = @id", new { id }, tx)));

    /// <summary>Create the employers row for an admin-provisioned employer account.</summary>
    public Task CreateEmployerAsync(Guid id, string companyName) =>
        db.AsServiceAsync(async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.employers (id, company_name) values (@id, @companyName)
                on conflict (id) do update set company_name = excluded.company_name
            """, new { id, companyName }, tx));
            return 0;
        });
}
