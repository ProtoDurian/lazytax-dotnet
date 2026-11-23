using Ctx.Check.Contract.V1;
using Ctx.Check.Domain;
using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Check.Data;

/// <summary>
/// check's rows (LazyTax CheckRepository, StreakRepository and the execution parts of HabitRepository). Every
/// read of a user's rows takes an <see cref="Owner"/>; the tick's reads are by habit or check id only.
/// Note the schema is named <c>check</c>, a reserved word: raw SQL quotes it.
/// </summary>
public sealed class CheckStore(CheckDb db) : ICheckStore
{
  public async Task<int> CreatePending(IReadOnlyList<NewCheck> checks, CancellationToken ct)
  {
    if (checks.Count == 0) return 0;
    var ids = checks.Select(_ => Guid.CreateVersion7()).ToArray();
    var habitIds = checks.Select(c => c.HabitId).ToArray();
    var users = checks.Select(c => c.UserId).ToArray();
    var dates = checks.Select(c => c.LocalDate).ToArray();
    var tzs = checks.Select(c => c.Timezone).ToArray();
    var opens = checks.Select(c => Utc(c.OpensAtUtc)).ToArray();
    var deadlines = checks.Select(c => Utc(c.DeadlineUtc)).ToArray();
    return await db.Database.ExecuteSqlAsync($"""
      INSERT INTO "check"."HabitChecks" ("Id", "HabitId", "UserId", "LocalDate", "Timezone", "OpensAtUtc", "DeadlineUtc", "Status")
      SELECT x.id, x.h, x.u, x.d, x.tz, x.o, x.dl, 0
      FROM unnest({ids}::uuid[], {habitIds}::uuid[], {users}::text[], {dates}::date[], {tzs}::text[],
                  {opens}::timestamptz[], {deadlines}::timestamptz[]) AS x(id, h, u, d, tz, o, dl)
      ON CONFLICT ("HabitId", "LocalDate") DO NOTHING
      """, ct);
  }

  public async Task<IReadOnlyList<DailyCheck>> ListDuePending(DateTime deadlineBeforeUtc, int limit, CancellationToken ct)
  {
    var before = Utc(deadlineBeforeUtc);
    var rows = await db.HabitChecks.AsNoTracking()
      .Where(c => c.Status == CheckStatusData.Pending && c.DeadlineUtc <= before)
      .OrderBy(c => c.DeadlineUtc).Take(limit).ToListAsync(ct);
    return [.. rows.Select(ToCheck)];
  }

  public async Task<IReadOnlyList<DailyCheck>> ListPending(IReadOnlyList<Guid> habitIds, CancellationToken ct)
  {
    var ids = habitIds.ToList();
    var rows = await db.HabitChecks.AsNoTracking()
      .Where(c => c.Status == CheckStatusData.Pending && ids.Contains(c.HabitId)).ToListAsync(ct);
    return [.. rows.Select(ToCheck)];
  }

  public async Task<IReadOnlyList<DailyCheck>> ListForDate(Owner owner, IReadOnlyList<Guid> habitIds, DateOnly localDate, CancellationToken ct)
  {
    var ids = habitIds.ToList();
    var rows = await db.HabitChecks.AsNoTracking().OwnedBy(owner, c => c.UserId)
      .Where(c => c.LocalDate == localDate && ids.Contains(c.HabitId)).ToListAsync(ct);
    return [.. rows.Select(ToCheck)];
  }

  public async Task<IReadOnlyList<DailyCheck>> ListCovering(Guid habitId, DateTime atUtc, CancellationToken ct)
  {
    var at = Utc(atUtc);
    var rows = await db.HabitChecks.AsNoTracking()
      .Where(c => c.HabitId == habitId && c.OpensAtUtc <= at && c.DeadlineUtc > at).ToListAsync(ct);
    return [.. rows.Select(ToCheck)];
  }

  public async Task<DailyCheck?> Get(Guid habitId, DateOnly localDate, CancellationToken ct)
  {
    var row = await db.HabitChecks.AsNoTracking().FirstOrDefaultAsync(c => c.HabitId == habitId && c.LocalDate == localDate, ct);
    return row is null ? null : ToCheck(row);
  }

  public async Task<bool> UpdatePendingWindow(Guid id, string timezone, DateTime opensAtUtc, DateTime deadlineUtc, CancellationToken ct)
  {
    var opens = Utc(opensAtUtc);
    var deadline = Utc(deadlineUtc);
    var n = await db.HabitChecks.Where(c => c.Id == id && c.Status == CheckStatusData.Pending)
      .ExecuteUpdateAsync(s => s.SetProperty(c => c.Timezone, timezone).SetProperty(c => c.OpensAtUtc, opens)
        .SetProperty(c => c.DeadlineUtc, deadline), ct);
    return n > 0;
  }

  public Task<int> DeletePending(IReadOnlyList<Guid> ids, CancellationToken ct)
  {
    var list = ids.ToList();
    return db.HabitChecks.Where(c => list.Contains(c.Id) && c.Status == CheckStatusData.Pending).ExecuteDeleteAsync(ct);
  }

  public async Task<HashSet<Guid>> ListRecorded(IReadOnlyList<Guid> checkIds, CancellationToken ct)
  {
    var ids = checkIds.ToList();
    var found = await db.HabitChecks.AsNoTracking()
      .Where(c => ids.Contains(c.Id) && db.HabitExecutions.Any(e => e.HabitId == c.HabitId && e.Date == c.LocalDate))
      .Select(c => c.Id).ToListAsync(ct);
    return [.. found];
  }

  public Task<bool> EvidenceExists(Guid habitId, string proofKey, string externalId, CancellationToken ct) =>
    db.HabitEvidence.AsNoTracking().AnyAsync(x => x.HabitId == habitId && x.ProofKey == proofKey && x.ExternalId == externalId, ct);

  public async Task<SettleOutcome?> Settle(SettleCheck cmd, CancellationToken ct)
  {
    // The caller holds the user lock. Lock the check row so concurrent settles serialize; only Pending settles.
    var check = (await db.HabitChecks.FromSql($"""SELECT * FROM "check"."HabitChecks" WHERE "Id" = {cmd.CheckId} FOR UPDATE""")
      .ToListAsync(ct)).FirstOrDefault();
    if (check is null || check.Status != CheckStatusData.Pending) return null;

    if (cmd.Evidence is { } ev && await db.HabitEvidence.AnyAsync(x =>
          x.HabitId == check.HabitId && x.ProofKey == ev.ProofKey && x.ExternalId == ev.ExternalId, ct))
      return null;

    // A user's skip counts against the month's skips, under the user's lock.
    if (cmd is { Status: CheckStatus.Skipped, SkipQuota: { } skips } &&
        await this.CountSkips(cmd.Habit.UserId, skips.MonthStart, skips.MonthEnd, ct) >= skips.Quota.Limit)
      throw new TierInsufficient(skips.Quota.Tier, skips.Quota.Key, skips.Quota.Limit);

    // A day that already has an execution stays as it is: the check adopts it instead of adding a second one.
    var executionId = Guid.CreateVersion7();
    var settledAt = Utc(cmd.SettledAtUtc);
    DateTime? completedAt = cmd.CompletedAtUtc is { } c ? Utc(c) : null;
    var h = cmd.Habit;
    var inserted = await db.Database.ExecuteSqlAsync($"""
      INSERT INTO "check"."HabitExecutions"
        ("Id", "HabitId", "HabitVersionId", "UserId", "Date", "Status", "CompletedAt", "Notes", "PaymentProcessed",
         "StakeCents", "RatioBps", "Currency", "CharityId", "HabitDeleted")
      VALUES ({executionId}, {check.HabitId}, {h.VersionId}, {h.UserId}, {check.LocalDate}, {(int)ToData(cmd.Status)},
              {completedAt}, {cmd.Notes}, false, {h.StakeCents}, {h.RatioBps}, {h.Currency}, {h.CharityId}, false)
      ON CONFLICT ("HabitId", "Date") DO NOTHING
      """, ct);
    var adopted = inserted == 0;
    var execution = await db.HabitExecutions.AsNoTracking()
      .FirstAsync(x => x.HabitId == check.HabitId && x.Date == check.LocalDate, ct);

    check.Status = execution.Status;
    check.SettledAtUtc = settledAt;
    check.SettledBy = adopted ? "adopted" : cmd.SettledBy;
    check.ExecutionId = execution.Id;

    if (cmd.Evidence is { } e && !adopted)
      db.HabitEvidence.Add(new HabitEvidenceRow
      {
        Id = Guid.CreateVersion7(),
        HabitId = check.HabitId,
        CheckId = check.Id,
        UserId = h.UserId,
        ProofKey = e.ProofKey,
        ExternalId = e.ExternalId,
        Passed = e.Verdict == EvidenceVerdict.Passed,
        OccurredAtUtc = Utc(e.OccurredAtUtc),
        ReceivedAtUtc = settledAt,
        Data = e.Data,
      });

    await db.SaveChangesAsync(ct);
    db.ChangeTracker.Clear();
    return new SettleOutcome(ToExecution(execution), check.Id, adopted);
  }

  public async Task<Dictionary<Guid, List<ExecutionDay>>> ExecutionsForHabits(Owner owner, IReadOnlyList<Guid> habitIds, CancellationToken ct)
  {
    var ids = habitIds.ToList();
    var rows = await db.HabitExecutions.AsNoTracking().OwnedBy(owner, e => e.UserId).Where(e => ids.Contains(e.HabitId)).OrderBy(e => e.Date)
      .Select(e => new { e.HabitId, e.Date, e.Status, e.CompletedAt }).ToListAsync(ct);
    return rows.GroupBy(r => r.HabitId)
      .ToDictionary(g => g.Key, g => g.Select(r => new ExecutionDay(r.Date, ToDomain(r.Status), r.CompletedAt)).ToList());
  }

  public async Task<int> MaxStreak(string userId, CancellationToken ct)
  {
    var rows = await db.HabitExecutions.AsNoTracking().Where(e => e.UserId == userId && !e.HabitDeleted)
      .OrderBy(e => e.HabitId).ThenBy(e => e.Date).Select(e => new { e.HabitId, e.Date, e.Status }).ToListAsync(ct);
    return rows.GroupBy(r => r.HabitId)
      .Select(g => Streaks.Max(g.Select(r => new ExecutionDay(r.Date, ToDomain(r.Status), null))))
      .DefaultIfEmpty(0).Max();
  }

  public Task<int> CountSkips(string userId, DateOnly monthStart, DateOnly monthEnd, CancellationToken ct) =>
    db.HabitExecutions.Where(e => e.UserId == userId && !e.HabitDeleted && e.Date >= monthStart && e.Date <= monthEnd
                                  && e.Status == CheckStatusData.Skipped).CountAsync(ct);

  public async Task<IReadOnlyList<HabitExecution>> SearchExecutions(Owner owner, IReadOnlyList<Guid> currentVersionIds,
    ExecutionSearch search, CancellationToken ct)
  {
    var versions = currentVersionIds.ToList();
    var q = db.HabitExecutions.AsNoTracking().OwnedBy(owner, e => e.UserId)
      .Where(e => !e.HabitDeleted && versions.Contains(e.HabitVersionId));
    if (search.HabitId is { } habitId) q = q.Where(e => e.HabitId == habitId);
    if (search.Date is { } date) q = q.Where(e => e.Date == date);
    var rows = await q.OrderBy(e => e.Date).ThenBy(e => e.Id).Skip(search.Skip).Take(search.Limit).ToListAsync(ct);
    return [.. rows.Select(ToExecution)];
  }

  public async Task<IReadOnlyList<HabitDebt>> OpenDebts(Owner owner, CancellationToken ct)
  {
    // LazyTax: Failed executions not yet PaymentProcessed, cents * bps / 10000 each (deleted habits included).
    var rows = await db.HabitExecutions.AsNoTracking().OwnedBy(owner, e => e.UserId)
      .Where(e => e.Status == CheckStatusData.Missed && !e.PaymentProcessed)
      .Select(e => new { e.HabitId, e.StakeCents, e.RatioBps }).ToListAsync(ct);
    return [.. rows.GroupBy(r => r.HabitId).Select(g => new HabitDebt(g.Key, g.Sum(r => r.StakeCents * r.RatioBps / 10_000L)))];
  }

  public async Task<HabitExecution?> GetExecution(Owner owner, Guid executionId, CancellationToken ct)
  {
    var row = await db.HabitExecutions.AsNoTracking().OwnedBy(owner, e => e.UserId).FirstOrDefaultAsync(e => e.Id == executionId, ct);
    return row is null ? null : ToExecution(row);
  }

  public async Task<HabitExecution?> ExecutionForDate(Owner owner, Guid habitId, DateOnly date, CancellationToken ct)
  {
    var row = await db.HabitExecutions.AsNoTracking().OwnedBy(owner, e => e.UserId)
      .Where(e => e.HabitId == habitId && e.Date == date)
      .OrderByDescending(e => e.Status == CheckStatusData.Done).ThenByDescending(e => e.Id).FirstOrDefaultAsync(ct);
    return row is null ? null : ToExecution(row);
  }

  public async Task MarkHabitsDeleted(string userId, IReadOnlyList<Guid> habitIds, CancellationToken ct)
  {
    var ids = habitIds.ToList();
    await db.HabitChecks.Where(c => c.UserId == userId && ids.Contains(c.HabitId) && c.Status == CheckStatusData.Pending)
      .ExecuteDeleteAsync(ct);
    await db.HabitExecutions.Where(e => e.UserId == userId && ids.Contains(e.HabitId))
      .ExecuteUpdateAsync(s => s.SetProperty(e => e.HabitDeleted, true), ct);
  }

  public async Task Purge(string userId, CancellationToken ct)
  {
    await db.HabitEvidence.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    await db.HabitChecks.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    await db.HabitExecutions.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
  }

  private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

  private static DailyCheck ToCheck(HabitCheckRow r) => new(r.Id, r.HabitId, r.UserId, r.LocalDate, r.Timezone,
    Utc(r.OpensAtUtc), Utc(r.DeadlineUtc), ToDomain(r.Status), r.SettledAtUtc is { } s ? Utc(s) : null, r.SettledBy, r.ExecutionId);

  private static HabitExecution ToExecution(HabitExecutionRow r) => new(r.Id, r.HabitId, r.HabitVersionId, r.UserId, r.Date,
    ToDomain(r.Status), r.CompletedAt is { } c ? Utc(c) : null, r.Notes);

  private static CheckStatus ToDomain(CheckStatusData s) => s switch
  {
    CheckStatusData.Done => CheckStatus.Done,
    CheckStatusData.Missed => CheckStatus.Missed,
    CheckStatusData.Skipped => CheckStatus.Skipped,
    CheckStatusData.Frozen => CheckStatus.Frozen,
    CheckStatusData.Vacation => CheckStatus.Vacation,
    _ => CheckStatus.Pending,
  };

  private static CheckStatusData ToData(CheckStatus s) => s switch
  {
    CheckStatus.Done => CheckStatusData.Done,
    CheckStatus.Missed => CheckStatusData.Missed,
    CheckStatus.Skipped => CheckStatusData.Skipped,
    CheckStatus.Frozen => CheckStatusData.Frozen,
    CheckStatus.Vacation => CheckStatusData.Vacation,
    _ => CheckStatusData.Pending,
  };
}
