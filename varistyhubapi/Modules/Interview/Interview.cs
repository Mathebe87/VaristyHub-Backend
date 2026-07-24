using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarsityHub.Services;

namespace VarsityHub.Modules.Interview;

public record NewSession(string Category);
public record SessionDto(Guid Id, string Category, int? Score, DateTime StartedAt, DateTime? CompletedAt);
public record AnswerRequest(string Question, string Answer);

// Init-property record so Dapper can map the text[] Strengths/Improvements columns.
public record FeedbackDto
{
    public Guid Id { get; init; }
    public string Question { get; init; } = "";
    public string? Answer { get; init; }
    public int? Clarity { get; init; }
    public int? Confidence { get; init; }
    public int? Relevance { get; init; }
    public int? Structure { get; init; }
    public string[] Strengths { get; init; } = [];
    public string[] Improvements { get; init; } = [];
}

// Shape Claude returns for a scored answer.
public record InterviewScore(int Clarity, int Confidence, int Relevance, int Structure, string[] Strengths, string[] Improvements);

public sealed class InterviewRepo(SupabaseDb db, IUserContext me, ClaudeClient claude)
{
    public Task<Guid> CreateSessionAsync(string category) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.interview_sessions (student_id, category)
                values (auth.uid(), @category)
                returning id
            """, new { category }, tx)));

    public Task<IEnumerable<SessionDto>> GetSessionsAsync() =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<SessionDto>(new CommandDefinition("""
                select id, category, score, started_at as StartedAt, completed_at as CompletedAt
                from public.interview_sessions
                where student_id = auth.uid()
                order by started_at desc
            """, transaction: tx)));

    public Task<IEnumerable<FeedbackDto>> GetFeedbackAsync(Guid sessionId) =>
        db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
            await c.QueryAsync<FeedbackDto>(new CommandDefinition("""
                select f.id, f.question, f.answer, f.clarity, f.confidence, f.relevance, f.structure,
                       coalesce(f.strengths, '{}') as Strengths, coalesce(f.improvements, '{}') as Improvements
                from public.interview_feedback f
                join public.interview_sessions s on s.id = f.session_id
                where f.session_id = @sessionId and s.student_id = auth.uid()
                order by f.created_at
            """, new { sessionId }, tx)));

    /// <summary>Score an answer with Claude and persist the feedback for the session.</summary>
    public async Task<FeedbackDto> ScoreAndSaveAsync(Guid sessionId, string question, string answer)
    {
        var score = await claude.CompleteJsonAsync<InterviewScore>(
            system: "You are an interview coach. Given a question and a candidate answer, return ONLY JSON " +
                    "{clarity, confidence, relevance, structure (each integer 0-100), strengths: string[], improvements: string[]}.",
            userPrompt: $"Question: {question}\n\nAnswer: {answer}")
            ?? new InterviewScore(0, 0, 0, 0, [], []);

        return await db.AsUserAsync(me.UserId!, me.Email, async (c, tx) =>
        {
            // Ownership guard: session must belong to the caller.
            var owns = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from public.interview_sessions where id = @sessionId and student_id = auth.uid())",
                new { sessionId }, tx));
            if (!owns) throw new UnauthorizedAccessException("Not your interview session.");

            var id = await c.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into public.interview_feedback
                    (session_id, question, answer, clarity, confidence, relevance, structure, strengths, improvements)
                values (@sessionId, @question, @answer, @Clarity, @Confidence, @Relevance, @Structure, @Strengths, @Improvements)
                returning id
            """, new
            {
                sessionId, question, answer,
                score.Clarity, score.Confidence, score.Relevance, score.Structure,
                Strengths = score.Strengths, Improvements = score.Improvements
            }, tx));

            // Roll the session score up to the average of its answers.
            await c.ExecuteAsync(new CommandDefinition("""
                update public.interview_sessions
                set score = (select round(avg((clarity + confidence + relevance + structure) / 4.0))
                             from public.interview_feedback where session_id = @sessionId)
                where id = @sessionId
            """, new { sessionId }, tx));

            return new FeedbackDto
            {
                Id = id, Question = question, Answer = answer,
                Clarity = score.Clarity, Confidence = score.Confidence,
                Relevance = score.Relevance, Structure = score.Structure,
                Strengths = score.Strengths, Improvements = score.Improvements
            };
        });
    }
}

[ApiController]
[Route("api/interview")]
[Authorize]
public sealed class InterviewController(InterviewRepo repo) : ControllerBase
{
    [HttpPost("sessions")]
    public async Task<ActionResult<object>> CreateSession([FromBody] NewSession body)
        => Ok(new { id = await repo.CreateSessionAsync(body.Category) });

    [HttpGet("sessions")]
    public async Task<ActionResult<IEnumerable<SessionDto>>> GetSessions()
        => Ok(await repo.GetSessionsAsync());

    [HttpGet("sessions/{id}/feedback")]
    public async Task<ActionResult<IEnumerable<FeedbackDto>>> GetFeedback(Guid id)
        => Ok(await repo.GetFeedbackAsync(id));

    [HttpPost("sessions/{id}/feedback")]
    public async Task<ActionResult<FeedbackDto>> AddFeedback(Guid id, [FromBody] AnswerRequest body)
    {
        try { return Ok(await repo.ScoreAndSaveAsync(id, body.Question, body.Answer)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
