using System.Data;
using Dapper;

namespace VarsityHub.Modules.Me;

/// <summary>
/// The current student's own profile, settings, results and APS. All queries run as the
/// caller (RLS enforced), keyed on auth.uid().
/// </summary>
public sealed class MeRepo(SupabaseDb db, IUserContext me)
{
    public Task<MeProfile?> GetProfileAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryFirstOrDefaultAsync<MeProfile>(new CommandDefinition("""
                select p.id, p.role, p.full_name as FullName, p.email::text as Email, p.phone,
                       p.avatar_url as AvatarUrl, p.email_verified as EmailVerified,
                       p.phone_verified as PhoneVerified,
                       s.student_type::text as StudentType, s.province, s.school_name as SchoolName, s.grade
                from public.profiles p
                left join public.students s on s.id = p.id
                where p.id = auth.uid()
            """, transaction: tx)));

    public Task UpdateProfileAsync(UpdateProfile u) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                update public.profiles
                set full_name  = coalesce(@FullName, full_name),
                    phone      = coalesce(@Phone, phone),
                    avatar_url = coalesce(@AvatarUrl, avatar_url),
                    updated_at = now()
                where id = auth.uid()
            """, u, tx));

            // Student-specific fields (row exists for student accounts).
            await c.ExecuteAsync(new CommandDefinition("""
                update public.students
                set province    = coalesce(@Province, province),
                    school_name  = coalesce(@SchoolName, school_name),
                    grade        = coalesce(@Grade, grade),
                    updated_at   = now()
                where id = auth.uid()
            """, u, tx));
            return 0;
        });

    public Task<UserSettingsDto?> GetSettingsAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryFirstOrDefaultAsync<UserSettingsDto>(new CommandDefinition("""
                select notification_prefs::text as NotificationPrefs, theme, locale
                from public.user_settings where user_id = auth.uid()
            """, transaction: tx)));

    public Task UpsertSettingsAsync(UpdateSettings u) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                insert into public.user_settings (user_id, notification_prefs, theme, locale)
                values (auth.uid(), coalesce(@NotificationPrefs::jsonb, '{}'::jsonb),
                        coalesce(@Theme, 'system'), coalesce(@Locale, 'en'))
                on conflict (user_id) do update
                set notification_prefs = coalesce(@NotificationPrefs::jsonb, public.user_settings.notification_prefs),
                    theme  = coalesce(@Theme, public.user_settings.theme),
                    locale = coalesce(@Locale, public.user_settings.locale),
                    updated_at = now()
            """, u, tx));
            return 0;
        });

    public Task<IEnumerable<StudentResultDto>> GetResultsAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<StudentResultDto>(new CommandDefinition("""
                select id, subject_name as SubjectName, level, percentage,
                       is_life_orientation as IsLifeOrientation
                from public.student_results where student_id = auth.uid()
                order by subject_name
            """, transaction: tx)));

    /// <summary>Replace the student's full result set in one transaction.</summary>
    public Task ReplaceResultsAsync(IReadOnlyList<ResultInput> results) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition(
                "delete from public.student_results where student_id = auth.uid()", transaction: tx));

            foreach (var r in results)
                await c.ExecuteAsync(new CommandDefinition("""
                    insert into public.student_results (student_id, subject_name, level, percentage, is_life_orientation)
                    values (auth.uid(), @SubjectName, @Level, @Percentage, @IsLifeOrientation)
                """, r, tx));
            return 0;
        });

    public Task<int?> GetApsAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.ExecuteScalarAsync<int?>(new CommandDefinition(
                "select aps from public.student_aps where student_id = auth.uid() limit 1", transaction: tx)));
}

/// <summary>
/// APS eligibility matching: programmes whose min_aps ≤ the student's APS and whose
/// per-subject requirements are all met by the student's captured results.
/// </summary>
public sealed class EligibilityRepo(SupabaseDb db, IUserContext me)
{
    public Task<IEnumerable<EligibleProgramme>> EligibleProgrammesAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<EligibleProgramme>(new CommandDefinition("""
                with my as (
                  select coalesce((select aps from public.student_aps where student_id = auth.uid()), 0) as aps
                )
                select p.id, p.name, p.min_aps as MinAps, u.name as University, u.short_code as ShortCode
                from public.programmes p
                join public.universities u on u.id = p.university_id
                cross join my
                where p.is_active
                  and my.aps >= p.min_aps
                  and not exists (
                    select 1 from public.programme_requirements r
                    where r.programme_id = p.id
                      and not exists (
                        select 1 from public.student_results sr
                        where sr.student_id = auth.uid()
                          and sr.subject_name = r.subject_name
                          and sr.level >= r.min_level))
                order by p.min_aps desc
            """, transaction: tx)));
}
