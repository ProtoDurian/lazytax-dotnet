using Ctx.Protection.Contract.V1;
using LazyTax.Platform.Lifecycle;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Protection.Tests.Integration;

public sealed class ProtectionPurgeTests(KernelApp root)
{
  private readonly ProtectionApp app = new(root);
  private static readonly string[] Tables = ["UserProtections", "FreezeConsumptions", "FreezeAwards", "SettledDays"];

  [Fact]
  public async Task Purging_a_user_leaves_none_of_their_rows_and_keeps_everyone_elses()
  {
    var (u, other) = (TestTokens.NewUser(), TestTokens.NewUser());
    foreach (var who in new[] { u, other })
    {
      await this.app.Seed(who, 2);
      await this.app.InScope(sp => sp.GetRequiredService<IProtectionV1>().TryConsumeFreeze(who, new DateOnly(2026, 11, 20)));
      await this.app.Settled(who, Guid.NewGuid(), "2026-11-16", "done");
      await this.app.InScope(sp => sp.GetRequiredService<Ctx.Protection.Domain.ProtectionService>().AwardWeekly(who, null, default));
    }
    foreach (var t in Tables) (await this.app.Count(t, u)).Should().BeGreaterThan(0, $"{t} should have rows before the purge");

    await this.app.Unit(async sp =>
    {
      foreach (var purge in sp.GetServices<IUserPurge>()) await purge.Purge(u, default);
    });

    foreach (var t in Tables)
    {
      (await this.app.Count(t, u)).Should().Be(0, $"{t} must be empty for the purged user");
      (await this.app.Count(t, other)).Should().BeGreaterThan(0, $"{t} must keep the other user's rows");
    }
  }
}
