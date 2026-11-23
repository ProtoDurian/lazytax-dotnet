using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace LazyTax.Testing;

/// <summary>Small HTTP helpers for integration tests.</summary>
public static class Http
{
  /// <summary>Asserts the status (showing the body on failure) and returns the JSON body (or default for 204).</summary>
  public static async Task<JsonElement> Expect(this Task<HttpResponseMessage> call, HttpStatusCode status)
  {
    var res = await call;
    var body = await res.Content.ReadAsStringAsync();
    res.StatusCode.Should().Be(status, body);
    return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
  }

  /// <summary>Calls an MCP tool at /mcp and returns the JSON-RPC result.</summary>
  public static async Task<JsonElement> CallTool(this HttpClient client, string tool, object args)
  {
    var res = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } });
    var body = await res.Content.ReadAsStringAsync();
    res.StatusCode.Should().Be(HttpStatusCode.OK, body);
    return JsonDocument.Parse(body).RootElement.GetProperty("result").Clone();
  }

  /// <summary>Signs a user up the LazyTax way (POST /api/v1/User with id and access tokens) and adds a configuration.</summary>
  public static async Task<string> SignUp(this KernelApp app, string? user = null, string timezone = "UTC")
  {
    user ??= TestTokens.NewUser();
    var client = app.ClientAs(user);
    await client.PostAsJsonAsync("/api/v1/User", new
    {
      idToken = TestTokens.IdToken(user, "n" + user, $"{user}@example.test"),
      accessToken = TestTokens.For(user),
    }).Expect(HttpStatusCode.OK);
    await client.PostAsJsonAsync($"/api/v1/Configuration/{user}", new { timezone }).Expect(HttpStatusCode.OK);
    return user;
  }

  /// <summary>Admin sets the user's plan tier (plan context, no billing).</summary>
  public static Task SetTier(this KernelApp app, string user, string tier) =>
    app.ClientAs("admin-" + Guid.NewGuid().ToString("N")[..6], "admin")
      .PutAsJsonAsync($"/api/v1/Subscription/{user}/tier", new { tier, days = 30 }).Expect(HttpStatusCode.OK);
}
