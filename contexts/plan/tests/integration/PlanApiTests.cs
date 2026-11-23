using System.Net;
using System.Net.Http.Json;
using Ctx.Plan.Contract.V1;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Plan.Tests.Integration;

public sealed class PlanApiTests(KernelApp app)
{
  [Fact]
  public async Task Plans_are_public_and_cheapest_first()
  {
    var plans = await app.Anonymous().GetAsync("/api/v1/Subscription/plans").Expect(HttpStatusCode.OK);
    plans.EnumerateArray().Select(p => p.GetProperty("key").GetString()).Should().Equal("free", "pro", "ultimate");
    plans[2].GetProperty("habitsUnlimited").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task A_new_user_is_free()
  {
    var user = await app.SignUp();
    var sub = await app.ClientAs(user).GetAsync($"/api/v1/Subscription/{user}").Expect(HttpStatusCode.OK);
    sub.GetProperty("tier").GetString().Should().Be("free");
  }

  [Fact]
  public async Task Admin_sets_the_tier_and_quotas_follow()
  {
    var user = await app.SignUp();
    await app.SetTier(user, "pro");
    var q = await app.InScope(sp => sp.GetRequiredService<IPlanV1>().Quota(user, QuotaKind.VacationWindows));
    q.Should().Be(new Quota("pro", EntitlementKeys.VacationWindowsYearly, 6));
    (await app.InScope(sp => sp.GetRequiredService<IPlanV1>().FreezeCap(user, 30))).Should().Be(15);
  }

  [Fact]
  public async Task Only_admin_sets_a_tier()
  {
    var user = await app.SignUp();
    await app.ClientAs(user).PutAsJsonAsync($"/api/v1/Subscription/{user}/tier", new { tier = "ultimate" }).Expect(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Setting_a_tier_for_a_missing_user_is_404() =>
    await app.ClientAs("root", "admin").PutAsJsonAsync($"/api/v1/Subscription/{TestTokens.NewUser()}/tier", new { tier = "pro" })
      .Expect(HttpStatusCode.NotFound);

  [Fact]
  public async Task Ensure_tier_refuses_a_cheaper_plan()
  {
    var user = await app.SignUp();
    var act = () => app.InScope(sp => sp.GetRequiredService<IPlanV1>().EnsureTier(user, Tiers.Pro, "proof.nfc"));
    await act.Should().ThrowAsync<TierInsufficient>();
  }

  [Fact]
  public async Task Another_users_subscription_is_403()
  {
    var a = await app.SignUp();
    await app.ClientAs(TestTokens.NewUser()).GetAsync($"/api/v1/Subscription/{a}").Expect(HttpStatusCode.Forbidden);
  }

  [Fact]
  public Task No_token_is_401() => app.Anonymous().GetAsync($"/api/v1/Subscription/{TestTokens.NewUser()}").Expect(HttpStatusCode.Unauthorized);

  [Fact]
  public async Task Mcp_list_plans_is_public()
  {
    var res = await app.Anonymous().CallTool("plan.list_plans", new { });
    res.GetProperty("isError").GetBoolean().Should().BeFalse();
    res.GetProperty("structuredContent").GetArrayLength().Should().Be(3);
  }
}
