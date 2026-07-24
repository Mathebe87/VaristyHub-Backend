using System.Net.Http.Json;
using System.Text.Json;

namespace VarsityHub.Services;

/// <summary>
/// Thin client over the Claude Messages API (POST /v1/messages). Used for job/bursary
/// recommendations, interview feedback, and CV parsing. Registered on the "claude" named
/// HttpClient which has a standard resilience handler (retry on 429/5xx).
/// </summary>
public sealed class ClaudeClient(IHttpClientFactory httpFactory, IConfiguration cfg)
{
    // Default to the most capable model; switch to claude-sonnet-5 / claude-haiku-4-5 for cost/latency.
    private readonly string _model = cfg["Claude:Model"] ?? "claude-opus-4-8";
    private readonly string _apiKey = cfg["Claude:ApiKey"] ?? "";

    public async Task<string> CompleteAsync(string system, string userPrompt, int maxTokens = 2048, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Claude:ApiKey is not configured.");

        var client = httpFactory.CreateClient("claude");
        client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var body = new
        {
            model = _model,
            max_tokens = maxTokens,
            system,
            messages = new[] { new { role = "user", content = userPrompt } }
        };

        var resp = await client.PostAsJsonAsync("https://api.anthropic.com/v1/messages", body, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }

    /// <summary>
    /// Ask Claude for JSON and deserialize it, tolerating markdown code fences the model may add.
    /// </summary>
    public async Task<T?> CompleteJsonAsync<T>(string system, string userPrompt, int maxTokens = 2048, CancellationToken ct = default)
    {
        var text = (await CompleteAsync(system, userPrompt, maxTokens, ct)).Trim();
        if (text.StartsWith("```"))
            text = text.Trim('`').Replace("json\n", "").Replace("json\r\n", "").Trim();

        return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
