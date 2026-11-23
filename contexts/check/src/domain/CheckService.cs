using Ctx.Account.Contract.V1;
using Ctx.Check.Contract.V1;
using Ctx.Plan.Contract.V1;
using Ctx.Vacation.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;
using Microsoft.Extensions.Logging;

namespace Ctx.Check.Domain;

/// <summary>404 for complete and skip, with LazyTax's message (HabitService.NotCompletable).</summary>
public sealed class NotCompletable(Guid habitVersionId)
  : Problem(404, "entity_not_found", "Entity Not Found",
    "Habit not found, not enabled, not scheduled for this day, or already has execution",
    new { requestIdentifier = habitVersionId.ToString(), typeName = "HabitExecution" });

/// <summary>404 when the habit is not the user's, not live or not enabled (LazyTax "Habit not found or not enabled").</summary>
public sealed class HabitNotSchedulable(Guid habitId)
  : Problem(404, "entity_not_found", "Entity Not Found", "Habit not found or not enabled",
    new { requestIdentifier = habitId.ToString(), typeName = "Habit" });

/// <summary>
/// Checks: open, settle (evidence, skip, the tick), retime (LazyTax Domain/Check/Service.cs). Every settle
/// runs in ONE unit of work under the user lock and writes a <c>check.settled</c> outbox event, which
/// replaces LazyTax's CheckEvents table and its stake bindings.
/// </summary>
public sealed class CheckService(
  ICheckStore store,
  IHabitSchedule habits,
  IVacationV1 vacation,
  IFreezes freezes,
  IPlanV1 plan,
  IAccountV1 account,
  IUnitOfWork uow,
  IOutbox outbox,
  ILogger<CheckService> logger)
{
  private const int CloseBatchSize = 500;

  // ---- the routes (complete, skip) --------------------------------------------------------------

  /// <summary>POST Habit/{userId}/{habitVersionId}/executions: the manual proof (always passes).</summary>
  public async Task<HabitExecution> CompleteHabit(string userId, Guid habitVersionId, string? notes, DateTime? nowUtc, CancellationToken ct)
  {
    // The first read is owner-scoped: another user's version id is a 404 before anything else runs.
    var v = await habits.GetVersion(Owner.User(userId), habitVersionId, ct) ?? throw new NotCompletable(habitVersionId);
    var now = nowUtc ?? DateTime.UtcNow;
    var evidence = new Evidence(ProofKeys.Manual, Guid.NewGuid().ToString(), EvidenceVerdict.Passed, now, null, notes);
    return await this.Report(userId, v.HabitId, evidence, now, ct) ?? throw new NotCompletable(habitVersionId);
  }

  /// <summary>
  /// POST Habit/{userId}/{habitVersionId}/executions/skip. LazyTax's SkipHabit loaded the version by id
  /// only (Domain/Habit/Service.cs:93-97); here the first read is owner-scoped, so user B on user A's
  /// version id gets 404 and nothing changes.
  /// </summary>
  public async Task<HabitExecution> SkipHabit(string userId, Guid habitVersionId, string? notes, DateTime? nowUtc, CancellationToken ct)
  {
    var v = await habits.GetVersion(Owner.User(userId), habitVersionId, ct) ?? throw new NotCompletable(habitVersionId);
    var now = nowUtc ?? DateTime.UtcNow;
    var quota = await plan.Quota(userId, QuotaKind.Skips, ct);
    var today = await account.Today(userId, now, ct);
    var (start, end) = CheckWindow.MonthOf(today.Today);
    return await this.Skip(userId, v.HabitId, notes, new SkipQuota(quota, start, end), now, ct)
           ?? throw new NotCompletable(habitVersionId);
  }

  /// <summary>GET Habit/{userId}/executions: executions of the owner's live habits' current versions (as LazyTax).</summary>
  public async Task<IReadOnlyList<HabitExecution>> SearchExecutions(Owner owner, ExecutionSearch search, CancellationToken ct)
  {
    var versions = await habits.CurrentVersionIds(owner, ct);
    return versions.Count == 0 ? [] : await store.SearchExecutions(owner, versions, search, ct);
  }

  // ---- check.v1 -----------------------------------------------------------------------------------

  /// <summary>Settles the check that was open at the evidence's moment. Null when nothing changed.</summary>
  public async Task<HabitExecution?> Report(string userId, Guid habitId, Evidence evidence, DateTime? nowUtc, CancellationToken ct)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var habit = await this.Schedulable(userId, habitId, ct);
    if (await store.EvidenceExists(habitId, evidence.ProofKey, evidence.ExternalId, ct)) return null;

    // Clock skew: evidence can't be from the future.
    var occurred = evidence.OccurredAtUtc > now ? now : evidence.OccurredAtUtc;
    var check = await this.OpenCheckAt(habit, occurred, ct);
    if (check is null || now > check.DeadlineUtc + CheckWindow.Grace) return null;

    var status = evidence.Verdict == EvidenceVerdict.Passed ? CheckStatus.Done : CheckStatus.Missed;
    var outcome = await this.SettleOne(new SettleCheck
    {
      CheckId = check.Id,
      Habit = habit,
      Status = status,
      SettledBy = evidence.ProofKey,
      SettledAtUtc = now,
      CompletedAtUtc = status == CheckStatus.Done ? occurred : null,
      Notes = evidence.Notes,
      Evidence = evidence with { OccurredAtUtc = occurred },
    }, ct);
    return outcome is { Adopted: false } ? outcome.Execution : null;
  }

  public async Task<DailyCheck?> PendingCheckAt(string userId, Guid habitId, DateTime atUtc, DateTime? nowUtc, CancellationToken ct)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var habit = await this.Schedulable(userId, habitId, ct);
    var check = await this.OpenCheckAt(habit, atUtc > now ? now : atUtc, ct);
    return check is null || now > check.DeadlineUtc + CheckWindow.Grace ? null : check;
  }

  /// <summary>Skips the check that is open now, within the month's skips (counted under the user lock).</summary>
  public async Task<HabitExecution?> Skip(string userId, Guid habitId, string? notes, SkipQuota quota, DateTime? nowUtc, CancellationToken ct)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var habit = await this.Schedulable(userId, habitId, ct);
    var check = await this.OpenCheckAt(habit, now, ct);
    if (check is null) return null;
    var outcome = await this.SettleOne(new SettleCheck
    {
      CheckId = check.Id,
      Habit = habit,
      Status = CheckStatus.Skipped,
      SettledBy = ProofKeys.Manual,
      SettledAtUtc = now,
      Notes = notes,
      SkipQuota = quota,
    }, ct);
    return outcome is { Adopted: false } ? outcome.Execution : null;
  }

  // ---- the tick ----------------------------------------------------------------------------------

  /// <summary>Opens upcoming checks, then settles the due ones. (The kernel's outbox dispatcher replaces LazyTax's DispatchEvents.)</summary>
  public async Task<int> Tick(DateTime nowUtc, CancellationToken ct)
  {
    var opened = await this.OpenUpcoming(nowUtc, ct);
    var closed = await this.CloseDue(nowUtc, ct);
    logger.LogInformation("Check tick: opened {Opened}, closed {Closed}", opened, closed);
    return opened + closed;
  }

  /// <summary>Creates the Pending checks for today and tomorrow (user-local) of every enabled habit.</summary>
  public async Task<int> OpenUpcoming(DateTime nowUtc, CancellationToken ct)
  {
    var all = await habits.ListSchedulable(null, null, ct);
    return await store.CreatePending([.. all.SelectMany(h => Retime.Upcoming(h, nowUtc))], ct);
  }

  /// <summary>Settles every Pending check whose deadline + grace has passed: vacation, then freeze, then missed.</summary>
  public async Task<int> CloseDue(DateTime nowUtc, CancellationToken ct)
  {
    var due = await store.ListDuePending(nowUtc - CheckWindow.Grace, CloseBatchSize, ct);
    return due.Count == 0 ? 0 : (await this.SettleDue(due, nowUtc, ct)).Count;
  }

  // ---- retiming (habit.changed, account.timezone_changed) ----------------------------------------

  /// <summary>Re-times Pending checks after a habit change. Started checks never get a shorter deadline; unscheduled ones are removed.</summary>
  public Task<int> RetimePending(string userId, IReadOnlyList<Guid>? habitIds, DateTime? nowUtc, CancellationToken ct) =>
    this.RetimeCore(userId, habitIds, nowUtc ?? DateTime.UtcNow, followTimezone: false, ct);

  /// <summary>
  /// Moves the user's Pending checks to their new timezone. Days whose new deadline (+ grace) has already
  /// passed are settled now and returned. Settled checks never change.
  /// </summary>
  public async Task<IReadOnlyList<SettledCheck>> MoveToTimezone(string userId, DateTime? nowUtc, CancellationToken ct)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    await this.RetimeCore(userId, null, now, followTimezone: true, ct);
    var mine = await habits.ListSchedulable(userId, null, ct);
    if (mine.Count == 0) return [];
    var pending = await store.ListPending([.. mine.Select(h => h.HabitId)], ct);
    var due = pending.Where(c => c.DeadlineUtc <= now - CheckWindow.Grace).ToList();
    return due.Count == 0 ? [] : await this.SettleDue(due, now, ct);
  }

  private async Task<int> RetimeCore(string userId, IReadOnlyList<Guid>? habitIds, DateTime now, bool followTimezone, CancellationToken ct)
  {
    var mine = await habits.ListSchedulable(userId, habitIds, ct);
    var byId = mine.ToDictionary(h => h.HabitId);
    var pending = (await store.ListPending(habitIds ?? [.. byId.Keys], ct)).Where(c => c.UserId == userId).ToList();

    var changed = 0;
    var stale = new List<Guid>();
    foreach (var c in pending)
    {
      switch (Retime.For(c, byId.GetValueOrDefault(c.HabitId), now, followTimezone))
      {
        case Retime.Remove:
          stale.Add(c.Id);
          break;
        case Retime.Move m:
          if (await store.UpdatePendingWindow(c.Id, m.Timezone, m.OpensAtUtc, m.DeadlineUtc, ct)) changed++;
          break;
      }
    }
    if (stale.Count > 0) changed += await store.DeletePending(stale, ct);
    return changed;
  }

  // ---- settling ----------------------------------------------------------------------------------

  private async Task<SchedulableHabit> Schedulable(string userId, Guid habitId, CancellationToken ct) =>
    (await habits.ListSchedulable(userId, [habitId], ct)).FirstOrDefault() ?? throw new HabitNotSchedulable(habitId);

  /// <summary>One settle in one unit of work: the user lock first, then the check's row lock, the writes and the event.</summary>
  private Task<SettleOutcome?> SettleOne(SettleCheck cmd, CancellationToken ct) =>
    uow.Start(async () =>
    {
      await uow.LockUser(cmd.Habit.UserId, ct);
      return await this.SettleLocked(cmd, ct);
    }, ct);

  private async Task<SettleOutcome?> SettleLocked(SettleCheck cmd, CancellationToken ct)
  {
    var outcome = await store.Settle(cmd, ct);
    if (outcome is null) return null;
    var e = outcome.Execution;
    var h = cmd.Habit;
    await outbox.Add(CheckEvents.Settled, new CheckSettled(
      h.UserId, h.HabitId, e.HabitVersionId, outcome.CheckId, e.Id, e.Date, StatusName(e.Status),
      h.StakeCents, h.RatioBps, h.Currency, h.CharityId, await store.MaxStreak(h.UserId, ct), outcome.Adopted), ct);
    return outcome;
  }

  /// <summary>
  /// Settles Pending checks past deadline + grace, per user and date. Each (user, date) is ONE unit of work:
  /// the lock, the vacation and freeze decision, and every settle commit together. Returns what was newly settled.
  /// </summary>
  private async Task<List<SettledCheck>> SettleDue(IReadOnlyList<DailyCheck> due, DateTime nowUtc, CancellationToken ct)
  {
    var live = await habits.ListSchedulable(null, [.. due.Select(c => c.HabitId).Distinct()], ct);
    var byId = live.ToDictionary(h => h.HabitId);

    // Checks of habits that were disabled, deleted or unscheduled for that weekday since they opened.
    var stale = due.Where(c => !byId.TryGetValue(c.HabitId, out var h) || !h.IsScheduledOn(c.LocalDate)).Select(c => c.Id).ToHashSet();
    if (stale.Count > 0) await store.DeletePending([.. stale], ct);

    var rest = due.Where(c => !stale.Contains(c.Id)).ToList();
    var recorded = await store.ListRecorded([.. rest.Select(c => c.Id)], ct);

    var settled = new List<SettledCheck>();
    foreach (var group in rest.GroupBy(c => (byId[c.HabitId].UserId, c.LocalDate)))
    {
      var (userId, date) = group.Key;
      try
      {
        var done = await uow.Start(async () =>
        {
          await uow.LockUser(userId, ct);
          // Days already recorded only get adopted; protections are decided only when something is
          // actually missing, so a fully recorded day never spends a freeze.
          var missing = group.Any(c => !recorded.Contains(c.Id));
          var status = missing ? await this.ProtectionFor(userId, date, ct) : CheckStatus.Missed;
          var list = new List<SettledCheck>();
          foreach (var c in group)
          {
            var outcome = await this.SettleLocked(new SettleCheck
            {
              CheckId = c.Id,
              Habit = byId[c.HabitId],
              Status = status,
              SettledBy = status switch
              {
                CheckStatus.Vacation => "vacation",
                CheckStatus.Frozen => "freeze",
                _ => "scheduler",
              },
              SettledAtUtc = nowUtc,
            }, ct);
            if (outcome is { Adopted: false }) list.Add(new SettledCheck(c.HabitId, date, status));
          }
          return list;
        }, ct);
        settled.AddRange(done);
      }
      catch (Exception e) when (e is not OperationCanceledException)
      {
        // One user's failure must not stop the batch; the checks stay Pending for the next tick.
        logger.LogError(e, "CloseDue failed for UserId={UserId} Date={Date}", userId, date);
      }
    }
    return settled;
  }

  /// <summary>Vacation first, then one freeze for the whole day (never above the current cap), else missed.</summary>
  private async Task<CheckStatus> ProtectionFor(string userId, DateOnly date, CancellationToken ct)
  {
    if (await vacation.IsAway(userId, date, ct)) return CheckStatus.Vacation;
    await freezes.ReconcileCap(userId, await store.MaxStreak(userId, ct), ct);
    return await freezes.TryConsumeFreeze(userId, date, ct) ? CheckStatus.Frozen : CheckStatus.Missed;
  }

  /// <summary>The Pending check whose window holds <paramref name="at"/>; opened on demand when the tick has not yet.</summary>
  private async Task<DailyCheck?> OpenCheckAt(SchedulableHabit habit, DateTime at, CancellationToken ct)
  {
    var covering = await store.ListCovering(habit.HabitId, at, ct);
    var pending = covering.Where(c => c.Status == CheckStatus.Pending).OrderBy(c => c.LocalDate).FirstOrDefault();
    if (pending is not null) return pending;
    if (covering.Count > 0) return null; // that moment's day is already settled

    var tz = CheckWindow.Resolve(habit.Timezone);
    var date = CheckWindow.LocalDate(at, tz);
    if (!habit.IsScheduledOn(date)) return null;
    var (opens, deadline) = CheckWindow.For(date, tz, habit.DeadlineTime);
    if (at < opens || at >= deadline) return null;

    await store.CreatePending([new NewCheck(habit.HabitId, habit.UserId, date, tz.Id, opens, deadline)], ct);
    var check = await store.Get(habit.HabitId, date, ct);
    return check is { Status: CheckStatus.Pending } && check.OpensAtUtc <= at && at < check.DeadlineUtc ? check : null;
  }

  public static string StatusName(CheckStatus s) => s switch
  {
    CheckStatus.Done => SettledStatus.Done,
    CheckStatus.Missed => SettledStatus.Missed,
    CheckStatus.Skipped => SettledStatus.Skipped,
    CheckStatus.Frozen => SettledStatus.Frozen,
    CheckStatus.Vacation => SettledStatus.Vacation,
    _ => "pending",
  };
}
