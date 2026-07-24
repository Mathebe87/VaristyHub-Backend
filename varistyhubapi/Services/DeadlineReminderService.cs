using System.Data;
using Dapper;

namespace VarsityHub.Services;

/// <summary>
/// Daily sweep that inserts reminder notifications for bursaries and applications
/// closing soon. Runs on the service path (RLS bypassed).
///
/// NOTE: with multiple API instances this runs on every instance. A Postgres advisory
/// lock (pg_try_advisory_xact_lock) guards the sweep so only one instance does the work.
/// </summary>
public sealed class DeadlineReminderService(IServiceProvider sp, ILogger<DeadlineReminderService> log)
    : BackgroundService
{
    private const long SweepLockKey = 918273645; // arbitrary, stable app-wide lock id

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SupabaseDb>();

                await db.AsServiceAsync(async (c, tx) =>
                {
                    // Only one instance wins the transaction-scoped advisory lock.
                    var gotLock = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                        "select pg_try_advisory_xact_lock(@k)", new { k = SweepLockKey }, tx));
                    if (!gotLock) return 0;

                    // Bursaries closing in 3 days that a student has bookmarked.
                    await c.ExecuteAsync(new CommandDefinition("""
                        insert into public.notifications (user_id, category, title, body, action_url)
                        select bb.student_id, 'bursary'::notification_category,
                               'Bursary deadline in 3 days',
                               b.name || ' closes on ' || to_char(b.closes_on, 'DD Mon YYYY'),
                               '/bursaries'
                        from public.bursary_bookmarks bb
                        join public.bursaries b on b.id = bb.bursary_id
                        where b.closes_on = current_date + 3
                    """, transaction: tx));

                    // Programme application deadlines in 3 days for the student's applications.
                    await c.ExecuteAsync(new CommandDefinition("""
                        insert into public.notifications (user_id, category, title, body, action_url)
                        select a.student_id, 'application'::notification_category,
                               'Application deadline in 3 days',
                               p.name || ' closes on ' || to_char(p.application_deadline, 'DD Mon YYYY'),
                               '/applications'
                        from public.applications a
                        join public.programmes p on p.id = a.programme_id
                        where p.application_deadline = current_date + 3
                    """, transaction: tx));

                    return 0;
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Deadline reminder sweep failed");
            }

            try { await Task.Delay(TimeSpan.FromHours(24), ct); }
            catch (TaskCanceledException) { break; }
        }
    }
}
