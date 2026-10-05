using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Lumen.Eval.Api;

public class LumenApiClient
{
    private readonly HttpClient _http;

    public LumenApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> LoginAsync(string email, string password, CancellationToken ct) =>
        (await SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/token", null, new { email, password }, ct)).Token;

    public Task<List<DocumentInfo>> GetDocumentsAsync(string token, CancellationToken ct) =>
        SendAsync<List<DocumentInfo>>(HttpMethod.Get, "api/documents", token, null, ct);

    public Task<List<DocumentChunkInfo>> GetChunksAsync(string token, Guid documentId, CancellationToken ct) =>
        SendAsync<List<DocumentChunkInfo>>(HttpMethod.Get, $"api/documents/{documentId}/chunks", token, null, ct);

    public Task<List<SearchHit>> SearchAsync(string token, string query, int topK, CancellationToken ct) =>
        SendAsync<List<SearchHit>>(HttpMethod.Post, "api/search", token, new { query, topK }, ct);

    public Task<AskResult> AskAsync(string token, string question, CancellationToken ct) =>
        SendAsync<AskResult>(HttpMethod.Post, "api/ask", token, new { question }, ct);

    public Task<JsonElement> GetSettingsAsync(string token, CancellationToken ct) =>
        SendAsync<JsonElement>(HttpMethod.Get, "api/settings", token, null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, string? token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonDefaults.Options);
        }

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{method} {path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options, ct))!;
    }
}
