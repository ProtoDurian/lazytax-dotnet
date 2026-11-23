using System.Collections.Concurrent;
using Ctx.Penalty.Contract.V1;
using Ctx.Penalty.Domain;
using Ctx.Penalty.Hooks;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Penalty.Tests.Integration;

/// <summary>The whole app, with penalty's two ports (payment.v1 charge, charity.v1 name) replaced by fakes.</summary>
public sealed class PenaltyApp : KernelApp
{
  public TestGateway Gateway { get; } = new();

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    base.ConfigureWebHost(builder);
    builder.ConfigureTestServices(s =>
    {
      s.AddSingleton<IChargeGateway>(this.Gateway);
      s.AddSingleton<ICharityNames>(new TestNames());
    });
  }

  /// <summary>Delivers a check.settled to penalty's subscriber, in its own unit of work (as the dispatcher does).</summary>
  public Task Settle(SettledCheck e) => this.InScope(async sp =>
  {
    var uow = sp.GetRequiredService<IUnitOfWork>();
    var handler = sp.GetRequiredService<CheckSettledHandler>();
    await uow.Start(() => handler.Handle(new OutboxEvent(Guid.NewGuid(), "check.settled", DateTime.UtcNow, 0), e, default));
  });

  /// <summary>A missed, staked check of <paramref name="userId"/>; returns the new penalty.</summary>
  public async Task<PenaltyInfo> MissedPenalty(string userId, long stakeCents = 1000, string currency = "USD", Guid? charityId = null)
  {
    var e = Missed(userId, stakeCents, currency, charityId);
    await this.Settle(e);
    return (await this.InScope(sp => sp.GetRequiredService<IPenaltyV1>().Search(Owner.User(userId), new PenaltySearch(Limit: 100))))
      .Single(p => p.HabitExecutionId == e.ExecutionId);
  }

  public static SettledCheck Missed(string userId, long stakeCents = 1000, string currency = "USD", Guid? charityId = null) =>
    new(userId, Guid.NewGuid(), Guid.NewGuid(), "missed", stakeCents, 10_000, currency, charityId ?? Guid.NewGuid());

  public Task<PenaltyInfo?> Read(Guid id) =>
    this.InScope(sp => sp.GetRequiredService<IPenaltyV1>().Get(Owner.Anyone(), id));

  public Task<int> Drain() => this.InScope(sp => sp.GetRequiredService<PenaltyService>().ProcessPending(default));
}

/// <summary>Charge outcome per user; default: succeeded.</summary>
public sealed class TestGateway : IChargeGateway
{
  public ConcurrentDictionary<string, Func<PenaltyInfo, ChargeOutcome>> ByUser { get; } = new();

  public Task<ChargeOutcome> Charge(PenaltyInfo penalty, CancellationToken ct) =>
    Task.FromResult(this.ByUser.TryGetValue(penalty.UserId, out var f) ? f(penalty) : new ChargeOutcome.Succeeded("int_" + penalty.Id.ToString("N")));
}

internal sealed class TestNames : ICharityNames
{
  public Task<string?> NameOf(Guid charityId, CancellationToken ct) => Task.FromResult<string?>("Test Charity");
}
