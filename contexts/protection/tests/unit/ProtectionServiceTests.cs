using Ctx.Protection.Contract.V1;
using Ctx.Protection.Domain;
using LazyTax.Platform.Data;

namespace Ctx.Protection.Tests.Unit;

public sealed class ProtectionServiceTests
{
  private readonly FakeRepo repo = new();
  private readonly FakePlan plan = new(2);
  private readonly FakeAccount account = new();
  private readonly FakeUow uow = new();
  private readonly FakeOutbox outbox = new();
  private readonly ProtectionService sut;

  private const string U = "u1";
  private static readonly Guid H1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
  private static readonly Guid H2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

  public ProtectionServiceTests() => this.sut = new(this.repo, this.plan, this.account, this.uow, this.outbox);

  private Task Settle(Guid habit, string date, string status) =>
    this.sut.RecordSettled(U, habit, DateOnly.Parse(date), status, null, default);

  [Fact]
  public async Task Consume_with_no_freezes_is_false_and_spends_nothing()
  {
    (await this.sut.TryConsumeFreeze(U, new DateOnly(2026, 11, 20), default)).Should().BeFalse();
    this.repo.Consumed.Should().BeEmpty();
    this.uow.Locks.Should().Be(1);
  }

  [Fact]
  public async Task Consume_is_once_per_day()
  {
    await this.repo.Upsert(U, default);
    await this.repo.Add(U, 2, default);
    var d = new DateOnly(2026, 11, 20);
    (await this.sut.TryConsumeFreeze(U, d, default)).Should().BeTrue();
    (await this.sut.TryConsumeFreeze(U, d, default)).Should().BeTrue(); // same day: already frozen, nothing more spent
    this.repo.Users[U].Current.Should().Be(1);
    (await this.sut.TryConsumeFreeze(U, d.AddDays(1), default)).Should().BeTrue();
    this.repo.Users[U].Current.Should().Be(0);
    (await this.sut.TryConsumeFreeze(U, d.AddDays(2), default)).Should().BeFalse();
  }

  [Fact]
  public async Task Reconcile_cuts_to_cap_and_cut_freezes_stay_gone()
  {
    await this.repo.Upsert(U, default);
    await this.repo.Add(U, 5, default);
    var b = await this.sut.ReconcileCap(U, 0, default);
    b.Should().Be(new FreezeBalance(U, 2, 2));
    this.plan.BaseCap = 10; // upgrade later: the cut freezes do not come back
    (await this.sut.ReconcileCap(U, 0, default)).Balance.Should().Be(2);
  }

  [Fact]
  public async Task Streak_bonus_raises_the_cap()
  {
    await this.repo.Upsert(U, default);
    await this.repo.Add(U, 3, default);
    (await this.sut.ReconcileCap(U, 30, default)).Should().Be(new FreezeBalance(U, 3, 3));
  }

  [Fact]
  public async Task Balance_uses_the_streak_check_last_reported()
  {
    await this.sut.ReconcileCap(U, 30, default); // cap 3
    await this.repo.Add(U, 3, default);
    (await this.sut.Balance(Owner.User(U), default)).Cap.Should().Be(3);
  }

  [Fact]
  public async Task Balance_refuses_anyone()
  {
    var act = () => this.sut.Balance(Owner.Anyone(), default);
    await act.Should().ThrowAsync<ArgumentException>();
  }

  [Fact]
  public async Task Perfect_week_awards_one_freeze_per_habit_once()
  {
    // Today Mon 2026-11-23 -> the week holding yesterday is Nov 22..28 in the fake; use Sunday-start of the previous week.
    this.account.Today_ = new DateOnly(2026, 11, 22); // Sunday; yesterday Sat Nov 21 -> week Nov 15..21
    foreach (var day in new[] { "2026-11-16", "2026-11-17", "2026-11-18" })
    {
      await this.Settle(H1, day, "done");
      await this.Settle(H2, day, "done");
    }
    (await this.sut.AwardWeekly(U, null, default)).Should().Be(2);
    this.repo.Users[U].Current.Should().Be(2);
    this.outbox.Added.Should().HaveCount(2).And.OnlyContain(x => x.Name == ProtectionEvents.Awarded);
    ((ProtectionAwarded)this.outbox.Added[0].Payload).WeekStart.Should().Be(new DateOnly(2026, 11, 15));

    (await this.sut.AwardWeekly(U, null, default)).Should().Be(0); // idempotent per (habit, week)
    this.repo.Users[U].Current.Should().Be(2);
  }

  [Fact]
  public async Task A_missed_or_frozen_day_spoils_the_week()
  {
    this.account.Today_ = new DateOnly(2026, 11, 22);
    await this.Settle(H1, "2026-11-16", "done");
    await this.Settle(H1, "2026-11-17", "missed");
    await this.Settle(H2, "2026-11-16", "done");
    await this.Settle(H2, "2026-11-17", "frozen");
    (await this.sut.AwardWeekly(U, null, default)).Should().Be(0);
    this.outbox.Added.Should().BeEmpty();
  }

  [Fact]
  public async Task Days_outside_last_week_do_not_count()
  {
    this.account.Today_ = new DateOnly(2026, 11, 22);
    await this.Settle(H1, "2026-11-10", "done"); // two weeks ago
    await this.Settle(H1, "2026-11-23", "done"); // future of the evaluated week
    (await this.sut.AwardWeekly(U, null, default)).Should().Be(0);
  }

  [Fact]
  public async Task Award_is_clamped_to_the_cap()
  {
    this.account.Today_ = new DateOnly(2026, 11, 22);
    this.plan.BaseCap = 1;
    await this.repo.Upsert(U, default);
    await this.repo.Add(U, 1, default); // already at cap
    await this.Settle(H1, "2026-11-16", "done");
    (await this.sut.AwardWeekly(U, null, default)).Should().Be(1);
    this.repo.Users[U].Current.Should().Be(1);
  }

  [Fact]
  public async Task A_user_without_configuration_earns_nothing()
  {
    this.account.Today_ = null;
    await this.Settle(H1, "2026-11-16", "done");
    (await this.sut.AwardWeekly(U, null, default)).Should().Be(0);
  }

  [Fact]
  public async Task Sweep_awards_recent_users_and_prunes_old_days()
  {
    this.account.Today_ = new DateOnly(2026, 11, 22);
    await this.Settle(H1, "2026-11-16", "done");
    await this.Settle(H2, "2026-08-01", "done"); // older than 45 days
    var n = await this.sut.AwardWeeklyForRecentUsers(new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc), default);
    n.Should().Be(1);
    this.repo.Settled.Keys.Should().NotContain((H2, new DateOnly(2026, 8, 1)));
  }

  [Fact]
  public async Task Recording_a_settled_day_twice_keeps_the_last_status_and_remembers_the_streak()
  {
    await this.sut.RecordSettled(U, H1, new DateOnly(2026, 11, 16), "missed", 4, default);
    await this.sut.RecordSettled(U, H1, new DateOnly(2026, 11, 16), "done", 7, default);
    this.repo.Settled[(H1, new DateOnly(2026, 11, 16))].Status.Should().Be("done");
    this.repo.Users[U].Max.Should().Be(7);
  }
}
