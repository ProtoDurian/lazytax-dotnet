using System.Net;
using FluentAssertions;

namespace LazyTax.Testing;

/// <summary>
/// Auth layer 4 check, one per entity (the guide requires it): user A creates a row; user B, calling the
/// route with B's OWN userId in the path and A's row id, gets 404 (never 403, never A's data).
/// </summary>
public static class OwnerIsolation
{
  /// <param name="app">The app fixture.</param>
  /// <param name="createAsA">Creates the entity as user A (client already signed in as A) and returns its id.</param>
  /// <param name="requestAsB">Builds B's request from (userB, idOfA), e.g. <c>(b, id) =&gt; new(HttpMethod.Get, $"/api/v1/Vacation/{b}/{id}")</c>.</param>
  public static async Task AssertOtherUserGets404(
    KernelApp app,
    Func<HttpClient, string, Task<string>> createAsA,
    Func<string, string, HttpRequestMessage> requestAsB)
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var id = await createAsA(app.ClientAs(a), a);
    var res = await app.ClientAs(b).SendAsync(requestAsB(b, id));
    res.StatusCode.Should().Be(HttpStatusCode.NotFound,
      $"user B must not see or change user A's row {id}; body: {await res.Content.ReadAsStringAsync()}");
  }
}
