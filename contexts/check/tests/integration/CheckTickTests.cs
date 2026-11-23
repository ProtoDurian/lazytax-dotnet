using System.Text.Json;
using Ctx.Check.Contract.V1;
using Ctx.Check.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ctx.Check.Tests.Integration;

/// <summary>The tick, settles, events and retiming, on a fixed clock (in the past, so other tests' checks, which are around real today, are never due).</summary>
public sealed class CheckTickTests(CheckApp app)
{
  // A Wednesday. The habits below are scheduled on every day, deadline 23:59 UTC.
  private static readonly DateTime Noon = new(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);
  private static readonly DateOnly Day = new(2020, 1, 1);

  private Task<T> Svc<T>(Func<CheckService, Task<T>> f) => app.InScope(sp => f(sp.GetRequiredService<CheckService>()));

  private async Task<List<JsonElement>> Settled(Guid habitId)
  {
    await using var conn = new NpgsqlConnection(app.ConnectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(
      "SELECT payload::text FROM kernel.outbox_events WHERE name = 'check.settled' AND payload->>'habitId' = @h ORDER BY created_at", conn);
    cmd.Parameters.AddWithValue("h", habitId.ToString());
    var list = new List<JsonElement>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync()) list.Add(JsonDocument.Parse(r.GetString(0)).RootElement.Clone());
    return list;
  }

  private async Task<string?> CheckStatusOf(Guid habitId, DateOnly date) =>
    (await app.InScope(sp => sp.GetRequiredService<ICheckStore>().Get(habitId, date, CancellationToken.None)))?.Status.ToString();

  [Fact]
  public async Task Report_then_replay_changes_nothing()
  {
    var user = TestTokens.NewUser();
    var habit = app.World.Habits.Add(user);
    var ev = new Evidence("strava", "act-1", EvidenceVerdict.Passed, Noon.AddHours(-1));

    var first = await this.Svc(s => s.Report(user, habit.HabitId, ev, Noon, CancellationToken.None));
    first.Should().NotBeNull();
    first!.Status.Should().Be(CheckStatus.Done);
    first.Date.Should().Be(Day);
    (await this.Svc(s => s.Report(user, habit.HabitId, ev, Noon, CancellationToken.None))).Should().BeNull();

    var events = await this.Settled(habit.HabitId);
    events.Should().ContainSingle();
    var e = events[0];
    e.GetProperty("status").GetString().Should().Be("done");
    e.GetProperty("stakeCents").GetInt64().Should().Be(1000);
    e.GetProperty("ratioBps").GetInt32().Should().Be(10_000);
    e.GetProperty("currency").GetString().Should().Be("USD");
    e.GetProperty("charityId").GetGuid().Should().Be(habit.CharityId!.Value);
    e.GetProperty("maxStreak").GetInt32().Should().Be(1);
    e.GetProperty("date").GetString().Should().Be("2020-01-01");
    e.GetProperty("executionId").GetGuid().Should().Be(first.Id);
  }

  [Fact]
  public async Task Report_for_another_users_habit_is_404()
  {
    var a = TestTokens.NewUser();
    var habit = app.World.Habits.Add(a);
    var act = () => this.Svc(s => s.Report(TestTokens.NewUser(), habit.HabitId,
      new Evidence("manual", "x", EvidenceVerdict.Passed, Noon), Noon, CancellationToken.None));
    await act.Should().ThrowAsync<HabitNotSchedulable>();
  }

  [Fact]
  public async Task Late_evidence_after_grace_is_ignored()
  {
    var user = TestTokens.NewUser();
    var habit = app.World.Habits.Add(user);
    // The tick opened the day earlier; the evidence arrives an hour after the deadline (grace is 30 min).
    await this.Svc(s => s.PendingCheckAt(user, habit.HabitId, Noon, Noon, CancellationToken.None));
    var late = Day.AddDays(1).ToDateTime(new TimeOnly(1, 0), DateTimeKind.Utc);
    (await this.Svc(s => s.Report(user, habit.HabitId, new Evidence("nfc", "t1", EvidenceVerdict.Passed, Noon), late, CancellationToken.None)))
      .Should().BeNull();
  }

  [Fact]
  public async Task Tick_settles_missed_vacation_and_frozen_days()
  {
    var missed = TestTokens.NewUser();
    var away = TestTokens.NewUser();
    var frozen = TestTokens.NewUser();
    var hMissed = app.World.Habits.Add(missed, h => h.InTick = true);
    var hAway = app.World.Habits.Add(away, h => h.InTick = true);
    var hFrozen = app.World.Habits.Add(frozen, h => h.InTick = true);
    app.World.Vacation.Away[(away, Day)] = true;
    app.World.Protection.Balances[frozen] = 1;

    foreach (var (u, h) in new[] { (missed, hMissed), (away, hAway), (frozen, hFrozen) })
      await this.Svc(s => s.PendingCheckAt(u, h.HabitId, Noon, Noon, CancellationToken.None));

    var afterGrace = Day.AddDays(1).ToDateTime(new TimeOnly(0, 45), DateTimeKind.Utc);
    (await this.Svc(s => s.CloseDue(afterGrace, CancellationToken.None))).Should().BeGreaterThanOrEqualTo(3);

    (await this.CheckStatusOf(hMissed.HabitId, Day)).Should().Be("Missed");
    (await this.CheckStatusOf(hAway.HabitId, Day)).Should().Be("Vacation");
    (await this.CheckStatusOf(hFrozen.HabitId, Day)).Should().Be("Frozen");
    app.World.Protection.Balances[frozen].Should().Be(0);
    app.World.Protection.LastMaxStreak.Should().ContainKey(frozen);

    (await this.Settled(hMissed.HabitId)).Single().GetProperty("status").GetString().Should().Be("missed");
    (await this.Settled(hAway.HabitId)).Single().GetProperty("status").GetString().Should().Be("vacation");
    (await this.Settled(hFrozen.HabitId)).Single().GetProperty("status").GetString().Should().Be("frozen");
  }

  [Fact]
  public async Task Tick_opens_today_and_tomorrow()
  {
    var user = TestTokens.NewUser();
    var habit = app.World.Habits.Add(user, h => h.InTick = true);
    await this.Svc(s => s.OpenUpcoming(Noon, CancellationToken.None));
    (await this.CheckStatusOf(habit.HabitId, Day)).Should().Be("Pending");
    (await this.CheckStatusOf(habit.HabitId, Day.AddDays(1))).Should().Be("Pending");
  }

  [Fact]
  public async Task Habit_changed_event_retimes_pending_checks()
  {
    var user = TestTokens.NewUser();
    var habit = app.World.Habits.Add(user, h => h.InTick = true);
    var tomorrow = Day.AddDays(1);
    await this.Svc(s => s.OpenUpcoming(Noon, CancellationToken.None));

    // The user drops tomorrow's weekday: habit emits habit.changed, check deletes that pending check.
    habit.Days = [.. habit.Days.Where(d => d != tomorrow.DayOfWeek.ToString())];
    await app.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(() =>
      sp.GetRequiredService<IOutbox>().Add("habit.changed", new { userId = user, habitIds = new[] { habit.HabitId } })));
    await app.DispatchOutbox();

    (await this.CheckStatusOf(habit.HabitId, tomorrow)).Should().BeNull();
  }

  [Fact]
  public async Task Contract_reads_are_owner_scoped()
  {
    var user = TestTokens.NewUser();
    var habit = app.World.Habits.Add(user);
    var e = await this.Svc(s => s.Report(user, habit.HabitId, new Evidence("manual", "m1", EvidenceVerdict.Passed, Noon), Noon, CancellationToken.None));
    await app.InScope(async sp =>
    {
      var check = sp.GetRequiredService<ICheckV1>();
      (await check.GetExecution(Owner.User(user), e!.Id)).Should().NotBeNull();
      (await check.GetExecution(Owner.User(TestTokens.NewUser()), e.Id)).Should().BeNull();
      (await check.OwnsExecution(user, e.Id)).Should().BeTrue();
      (await check.ExecutionForDate(Owner.User(user), habit.HabitId, Day))!.Id.Should().Be(e.Id);
      (await check.MaxStreak(user)).Should().Be(1);
      (await check.Streaks(user, [habit.HabitId])).Single().Should().Be(new HabitStreak(habit.HabitId, 1, 1));
      (await check.Streaks(TestTokens.NewUser(), [habit.HabitId])).Single().Max.Should().Be(0);
    });
  }
}
