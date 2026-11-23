using System.Text.Json;
using Ctx.Proof.Domain;
using LazyTax.Platform.Problems;

namespace Ctx.Proof.Tests.Unit;

public sealed class HelperTests
{
  private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void State_round_trips_and_names_the_user()
  {
    var s = StravaState.Sign("u_1", Now.AddMinutes(15), "k");
    StravaState.Verify(s, Now, "k").Should().Be("u_1");
  }

  [Fact]
  public void State_is_refused_when_forged_expired_or_malformed()
  {
    var s = StravaState.Sign("u_1", Now.AddMinutes(15), "k");
    StravaState.Verify(s, Now, "other-key").Should().BeNull();
    StravaState.Verify(s, Now.AddMinutes(16), "k").Should().BeNull();
    StravaState.Verify(s[..^2] + "AA", Now, "k").Should().BeNull();
    StravaState.Verify("garbage", Now, "k").Should().BeNull();
    StravaState.Verify(null, Now, "k").Should().BeNull();
    var other = StravaState.Sign("u_2", Now.AddMinutes(15), "k");
    StravaState.Verify(s.Split('.')[0] + "." + other.Split('.')[1], Now, "k").Should().BeNull();
  }

  private static string Ts(DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds().ToString();

  [Fact]
  public void Webhook_signature_accepts_a_fresh_signed_body()
  {
    var ts = Ts(Now);
    var sig = WebhookSignature.Sign("s", ts, "{\"a\":1}");
    WebhookSignature.Verify("s", ts, sig, "{\"a\":1}", Now, 300).Should().BeTrue();
    WebhookSignature.Verify("s", ts, sig.ToUpperInvariant(), "{\"a\":1}", Now, 300).Should().BeTrue();
  }

  [Theory]
  [InlineData("wrong-secret", 0, false)]
  [InlineData("s", 301, true)]
  [InlineData("s", -301, true)]
  public void Webhook_signature_refuses_bad_secret_replay_and_future_stamp(string secret, int skewSec, bool _)
  {
    var ts = Ts(Now.AddSeconds(-skewSec));
    var sig = WebhookSignature.Sign(secret, ts, "{}");
    WebhookSignature.Verify("s", ts, sig, "{}", Now, 300).Should().BeFalse();
  }

  [Fact]
  public void Webhook_signature_refuses_a_changed_body_and_missing_headers()
  {
    var ts = Ts(Now);
    var sig = WebhookSignature.Sign("s", ts, "{}");
    WebhookSignature.Verify("s", ts, sig, "{ }", Now, 300).Should().BeFalse();
    WebhookSignature.Verify("s", null, sig, "{}", Now, 300).Should().BeFalse();
    WebhookSignature.Verify("s", ts, null, "{}", Now, 300).Should().BeFalse();
    WebhookSignature.Verify("s", "abc", sig, "{}", Now, 300).Should().BeFalse();
  }

  private static StravaWebhookEvent? Parse(string json, long sub = 0) => StravaWebhookParser.Parse(JsonDocument.Parse(json).RootElement, sub);

  [Fact]
  public void Parser_reads_an_activity_event_and_the_deauthorize_flag()
  {
    var e = Parse("""{"object_type":"activity","aspect_type":"create","object_id":99,"owner_id":7,"event_time":1700000000}""")!;
    (e.ObjectType, e.AspectType, e.ObjectId, e.OwnerId, e.Deauthorized).Should().Be(("activity", "create", 99L, 7L, false));
    Parse("""{"object_type":"athlete","aspect_type":"update","object_id":7,"owner_id":7,"event_time":1700000000,"updates":{"authorized":"false"}}""")!
      .Deauthorized.Should().BeTrue();
    Parse("""{"object_type":"activity","aspect_type":"update","object_id":7,"owner_id":7,"event_time":1700000000,"updates":{"authorized":"false"}}""")!
      .Deauthorized.Should().BeFalse();
  }

  [Theory]
  [InlineData("""[]""")]
  [InlineData("""{"object_type":"user","aspect_type":"create","object_id":1,"owner_id":1,"event_time":1}""")]
  [InlineData("""{"object_type":"activity","aspect_type":"purge","object_id":1,"owner_id":1,"event_time":1}""")]
  [InlineData("""{"object_type":"activity","aspect_type":"create","object_id":"1","owner_id":1,"event_time":1}""")]
  [InlineData("""{"object_type":"activity","aspect_type":"create","object_id":1,"owner_id":1}""")]
  public void Parser_drops_malformed_events(string json) => Parse(json).Should().BeNull();

  [Fact]
  public void Parser_checks_the_subscription_id_only_when_one_is_set()
  {
    const string body = """{"object_type":"activity","aspect_type":"create","object_id":1,"owner_id":1,"event_time":1700000000,"subscription_id":5}""";
    Parse(body, 5).Should().NotBeNull();
    Parse(body, 6).Should().BeNull();
    Parse(body, 0).Should().NotBeNull();
  }

  [Fact]
  public void Token_box_round_trips_and_never_stores_plain_text()
  {
    var box = new TokenBox(new ProofOptions());
    var c = box.Encrypt("secret-token");
    c.Should().NotContain("secret-token");
    box.Decrypt(c).Should().Be("secret-token");
    box.Encrypt("secret-token").Should().NotBe(c); // fresh nonce
    new TokenBox(new ProofOptions { Strava = { EncryptionKey = "another" } }).Invoking(b => b.Decrypt(c)).Should().Throw<Exception>();
  }

  [Fact]
  public void Strava_settings_are_validated_and_normalized()
  {
    var p = new StravaProofPlugin(new MemStrava());
    p.ValidateSettings("""{ "sportTypes": ["Run", "Ride"] }""").Should().Be("""{"sportTypes":["Run","Ride"]}""");
    new[] { """{"sportTypes":[]}""", """{"sportTypes":["Quidditch"]}""", """{"sportTypes":["Run"],"extra":1}""", "not json", "null" }
      .Should().OnlyContain(bad => Throws(() => p.ValidateSettings(bad)));
  }

  private static bool Throws(Action a)
  {
    try { a(); return false; }
    catch (ValidationFailed) { return true; }
  }

  [Fact]
  public void The_plugin_list_hides_strava_when_it_is_off_and_manual_and_nfc_are_always_bound()
  {
    IProofPlugin[] all = [new ManualProofPlugin(), new NfcProofPlugin(new NfcTagLookup(null!)), new StravaProofPlugin(new MemStrava())];
    var off = new PluginRegistry(all, new ProofOptions());
    off.List().Select(m => m.Key).Should().Equal("manual", "nfc");
    off.List().Should().OnlyContain(m => m.AlwaysBound);
    off.Get("strava").Should().BeNull();
    var on = new PluginRegistry(all, new ProofOptions { Strava = { Enabled = true } });
    on.List().Select(m => m.Key).Should().Equal("manual", "nfc", "strava");
    on.List().Single(m => m.Key == "strava").MinTier.Should().Be("pro");
  }
}
