using Ctx.Account.Contract.V1;
using Ctx.Plan.Contract.V1;
using Ctx.Protection.Contract.V1;
using Ctx.Protection.Data;
using LazyTax.Platform.Data;
using LazyTax.Platform.Lifecycle;
using LazyTax.Platform.Outbox;
using LazyTax.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Protection.Tests.Integration;

/// <summary>
/// The kernel app with plan and account swapped for fixed fakes: the freeze cap and "today" are chosen by the test,
/// so these tests do not depend on how plan and account are configured. Protection itself is the real thing.
/// </summary>
internal sealed class ProtectionApp
{
  public int Cap { get; set; } = 3;
  public DateOnly Today { get; set; } = new(2026, 11, 22); // a Sunday: the last completed week is Nov 15..21
  public WebApplicationFactory<Program> Factory { get; }
  public KernelApp Root { get; }

  public ProtectionApp(KernelApp root)
  {
    this.Root = root;
    this.Factory = root.WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
      s.AddScoped<IPlanV1>(_ => new FixedPlan(this));
      s.AddScoped<IAccountV1>(_ => new FixedAccount(this));
    }));
  }

  public HttpClient ClientAs(string sub, params string[] scopes) => this.Factory.CreateClient().As(sub, scopes);

  public async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
  {
    await using var scope = this.Factory.Services.CreateAsyncScope();
    return await work(scope.ServiceProvider);
  }

  public async Task InScope(Func<IServiceProvider, Task> work)
  {
    await using var scope = this.Factory.Services.CreateAsyncScope();
    await work(scope.ServiceProvider);
  }

  public Task<T> Unit<T>(Func<IServiceProvider, Task<T>> work) =>
    this.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(() => work(sp)));

  public Task Unit(Func<IServiceProvider, Task> work) =>
    this.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(() => work(sp)));

  /// <summary>Banks <paramref name="n"/> freezes for the user, bypassing the cap.</summary>
  public Task Seed(string userId, int n) => this.InScope(async sp =>
  {
    var db = sp.GetRequiredService<ProtectionDb>();
    db.UserProtections.Add(new UserProtectionRow { UserId = userId, FreezeCurrent = n });
    await db.SaveChangesAsync();
  });

  /// <summary>Emits check.settled the way check does, then delivers it.</summary>
  public async Task Settled(string userId, Guid habitId, string date, string status, int maxStreak = 0)
  {
    await this.Unit(sp => sp.GetRequiredService<IOutbox>().Add("check.settled", new
    {
      userId,
      habitId,
      executionId = Guid.NewGuid(),
      date,
      status,
      stakeCents = 0,
      ratioBps = 0,
      currency = "USD",
      charityId = (Guid?)null,
      maxStreak,
    }));
    await this.Root.DispatchOutbox();
  }

  public Task<long> Count(string table, string userId) =>
    this.Root.Count($"SELECT count(*) FROM protection.\"{table}\" WHERE \"UserId\" = @u", ("u", userId));

  public async Task<FreezeBalance> BalanceOf(string userId) =>
    await this.InScope(sp => sp.GetRequiredService<IProtectionV1>().Balance(Owner.User(userId)));

  private sealed class FixedPlan(ProtectionApp app) : IPlanV1
  {
    public Task<string> EffectiveTier(string userId, CancellationToken ct = default) => Task.FromResult(Tiers.Pro);
    public Task<Quota> Quota(string userId, QuotaKind kind, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> FreezeCap(string userId, int maxStreak, CancellationToken ct = default) => Task.FromResult(app.Cap);
    public Task EnsureTier(string userId, string minTier, string feature, CancellationToken ct = default) => Task.CompletedTask;
  }

  private sealed class FixedAccount(ProtectionApp app) : IAccountV1
  {
    public Task<AccountProfile?> GetProfile(Owner owner, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> Exists(string userId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<string?> GetTimezone(string userId, CancellationToken ct = default) => Task.FromResult<string?>("UTC");
    public Task<TimeOnly> EndOfDay(string userId, CancellationToken ct = default) => Task.FromResult(new TimeOnly(23, 59));
    public Task<Guid?> DefaultCharity(string userId, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
    public Task<UserToday> Today(string userId, DateTime? nowUtc = null, CancellationToken ct = default) => Task.FromResult(new UserToday("UTC", app.Today));
  }
}
