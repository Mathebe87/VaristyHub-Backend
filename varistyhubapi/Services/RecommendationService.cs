using System.Data;
using Dapper;

namespace VarsityHub.Services;

/// <summary>
/// AI-backed matching: ranks open jobs and bursaries against the student's profile,
/// and parses CV text. Candidate rows are read as the caller (RLS applies); Claude ranks.
/// </summary>
public sealed class RecommendationService(ClaudeClient claude, SupabaseDb db, IUserContext me)
{
    public async Task<List<RankedItem>> RecommendJobsAsync()
    {
        var (profileJson, jobsJson) = await db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            var profile = await c.ExecuteScalarAsync<string>(new CommandDefinition("""
                select coalesce(json_build_object(
                  'aps', (select aps from public.student_aps where student_id = auth.uid()),
                  'subjects', (select json_agg(subject_name) from public.student_results where student_id = auth.uid())
                )::text, '{}')
            """, transaction: tx));
            var jobs = await c.ExecuteScalarAsync<string>(new CommandDefinition("""
                select coalesce(json_agg(json_build_object(
                  'id', id, 'title', title, 'company', company, 'type', type, 'tags', tags))::text, '[]')
                from public.jobs where is_active limit 50
            """, transaction: tx));
            return (profile, jobs);
        });

        const string system = "You are a career-matching assistant for South African students. " +
            "Return ONLY a JSON array of objects {id, matchScore (0-100), reason}, best first, max 10.";
        var prompt = $"Student profile:\n{profileJson}\n\nAvailable jobs:\n{jobsJson}";
        return await claude.CompleteJsonAsync<List<RankedItem>>(system, prompt) ?? [];
    }

    public async Task<List<RankedItem>> RecommendBursariesAsync()
    {
        var (profileJson, bursariesJson) = await db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            var profile = await c.ExecuteScalarAsync<string>(new CommandDefinition("""
                select coalesce(json_build_object(
                  'aps', (select aps from public.student_aps where student_id = auth.uid()),
                  'subjects', (select json_agg(subject_name) from public.student_results where student_id = auth.uid())
                )::text, '{}')
            """, transaction: tx));
            var bursaries = await c.ExecuteScalarAsync<string>(new CommandDefinition("""
                select coalesce(json_agg(json_build_object(
                  'id', id, 'name', name, 'provider', provider, 'field', field, 'minAps', min_aps))::text, '[]')
                from public.bursaries where is_active limit 50
            """, transaction: tx));
            return (profile, bursaries);
        });

        const string system = "You are a bursary-matching assistant for South African students. " +
            "Return ONLY a JSON array of objects {id, matchScore (0-100), reason}, best first, max 10.";
        var prompt = $"Student profile:\n{profileJson}\n\nAvailable bursaries:\n{bursariesJson}";
        return await claude.CompleteJsonAsync<List<RankedItem>>(system, prompt) ?? [];
    }

    public Task<string> ParseCvAsync(string cvText) =>
        claude.CompleteAsync(
            system: "Extract from this CV as JSON: {skills[], qualifications[], yearsExperience, fields[]}.",
            userPrompt: cvText, maxTokens: 1024);
}

public sealed record RankedItem(Guid Id, int MatchScore, string Reason);
