namespace Lumen.IntegrationTests.Utils;

public static class TestAuth
{
    public static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/token", new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body!["token"];
    }

    public static async Task AuthenticateAsync(HttpClient client, string email, string password)
    {
        var token = await LoginAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
