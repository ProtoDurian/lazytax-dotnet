using System.Text;
using System.Text.Json;
using Ctx.Proof.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Ctx.Proof.Api.V1;

/// <summary>LazyTax's StravaController (/api/v1/Strava/...). Every endpoint is 404 when Strava is off.</summary>
[Route("Strava")]
public sealed class StravaController(
  StravaService service,
  IStravaInbox inbox,
  ProofOptions options,
  TimeProvider clock,
  ILogger<StravaController> logger) : ContextController
{
  // "read" is Strava's default profile scope; activity:read_all also sees "Only You" activities.
  private const string RequestedScope = "read," + StravaScopes.Required;
  private const int MaxLoggedBody = 1024;

  private StravaOptions Opt => options.Strava;

  // The Strava authorize URLs to open: AppUrl in the Strava app when installed, else Url in an in-app browser.
  [Policy(Policies.Registered), Subject("userId"), HttpGet("connect-url/{userId}")]
  public StravaConnectUrlRes ConnectUrl(string userId)
  {
    this.RequireEnabled();
    var query = new Dictionary<string, string?>
    {
      ["client_id"] = Opt.ClientId,
      ["redirect_uri"] = Opt.CallbackUrl,
      ["response_type"] = "code",
      ["approval_prompt"] = "auto",
      ["scope"] = RequestedScope,
      ["state"] = StravaState.Sign(userId, clock.GetUtcNow().UtcDateTime + TimeSpan.FromMinutes(Opt.StateLifetimeMinutes), Opt.ClientSecret),
    };
    return new StravaConnectUrlRes(
      QueryHelpers.AddQueryString("https://www.strava.com/oauth/mobile/authorize", query),
      QueryHelpers.AddQueryString("strava://oauth/mobile/authorize", query));
  }

  // Strava redirects here after the user approves or cancels. Always ends at the app's deep link with
  // ?result=connected|denied|scope_missing|athlete_taken|expired|error.
  [Policy(Policies.Public), HttpGet("callback")]
  public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? scope, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
  {
    this.RequireEnabled();
    var userId = StravaState.Verify(state, clock.GetUtcNow().UtcDateTime, Opt.ClientSecret);
    if (userId is null) return this.ToApp("expired");
    if (error is not null || code is null) return this.ToApp("denied");

    try
    {
      return this.ToApp(await service.Connect(userId, code, scope ?? "", ct) switch
      {
        StravaConnectOutcome.Connected => "connected",
        StravaConnectOutcome.ScopeMissing => "scope_missing",
        StravaConnectOutcome.AthleteTaken => "athlete_taken",
        _ => "error",
      });
    }
    catch (Exception e) when (e is not OperationCanceledException)
    {
      logger.LogError(e, "Strava connect failed for UserId={UserId}", userId);
      return this.ToApp("error");
    }
  }

  [Policy(Policies.Registered), Subject("userId"), HttpGet("{userId}")]
  public async Task<StravaConnectionRes> Get(string userId, CancellationToken ct)
  {
    this.RequireEnabled();
    var c = await service.Get(Owner.User(userId), ct);
    return new StravaConnectionRes(c is not null, c?.ConnectedAtUtc);
  }

  // Revokes LazyTax in Strava and deletes the user's Strava data; their Strava habits go back to manual check-in.
  [Policy(Policies.Registered), Subject("userId"), HttpDelete("{userId}")]
  public async Task<IActionResult> Disconnect(string userId, CancellationToken ct)
  {
    this.RequireEnabled();
    return await service.Disconnect(Owner.User(userId), ct) ? this.NoContent() : throw new NotFound("Strava Connection", userId);
  }

  // Strava's subscription check: echo hub.challenge when the verify token matches.
  [Policy(Policies.Public), HttpGet("webhook")]
  public IActionResult WebhookChallenge(
    [FromQuery(Name = "hub.mode")] string? mode,
    [FromQuery(Name = "hub.verify_token")] string? verifyToken,
    [FromQuery(Name = "hub.challenge")] string? challenge)
  {
    this.RequireEnabled();
    if (mode != "subscribe" || challenge is null || !FixedEquals(verifyToken, Opt.VerifyToken)) return this.StatusCode(403);
    return this.Ok(new Dictionary<string, string> { ["hub.challenge"] = challenge });
  }

  // Must answer within 2 seconds: verify the signature, queue the event and let the worker fetch the activity.
  [Policy(Policies.Public), HttpPost("webhook")]
  public async Task<IActionResult> Webhook(CancellationToken ct)
  {
    this.RequireEnabled();
    using var reader = new StreamReader(this.Request.Body, Encoding.UTF8);
    var raw = await reader.ReadToEndAsync(ct);

    var ok = WebhookSignature.Verify(
      Opt.WebhookSecret,
      this.Request.Headers["X-Lazytax-Timestamp"].ToString(),
      this.Request.Headers["X-Lazytax-Signature"].ToString(),
      raw, clock.GetUtcNow().UtcDateTime, Opt.WebhookToleranceSec);
    if (!ok) return this.StatusCode(403);

    StravaWebhookEvent? e = null;
    try
    {
      using var doc = JsonDocument.Parse(raw);
      e = StravaWebhookParser.Parse(doc.RootElement, Opt.SubscriptionId);
    }
    catch (JsonException)
    {
    }

    if (e is null)
    {
      logger.LogWarning("Ignoring Strava webhook event ({Length} chars): {Body}", raw.Length, raw.Length <= MaxLoggedBody ? raw : raw[..MaxLoggedBody]);
      return this.Ok();
    }

    await inbox.Enqueue(e, ct);
    return this.Ok();
  }

  private void RequireEnabled()
  {
    if (!Opt.Enabled) throw new NotFound("Strava", "disabled");
  }

  private IActionResult ToApp(string result) => this.Redirect(QueryHelpers.AddQueryString(Opt.AppRedirectUri, "result", result));

  private static bool FixedEquals(string? a, string b) =>
    a is not null && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
