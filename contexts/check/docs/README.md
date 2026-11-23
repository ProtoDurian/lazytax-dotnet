# check

The daily check of every habit, and its settled outcome.

## What it owns

| Thing | Where |
|---|---|
| Checks: one per scheduled habit per user-local date (LazyTax `HabitChecks`) | `check."HabitChecks"` |
| Evidence: what proved a check, idempotent per (habit, proof key, external id) | `check."HabitEvidence"` |
| Executions: the settled outcome of each day (LazyTax `HabitExecutions`) | `check."HabitExecutions"` |
| Streaks, the habit overview, the executions search | `domain/Streaks.cs`, `domain/OverviewService.cs` |
| The tick: open upcoming checks, settle due ones (vacation, then freeze, then missed) | `CheckTickWorker`, every 5 min |
| `check.settled` outbox event, one per settle | `CheckService.SettleLocked` |

## Routes (same URLs as LazyTax)

| Method | Path | Policy | Notes |
|---|---|---|---|
| POST | `/api/v1/Habit/{userId}/{habitVersionId}/executions` | registered, subject | complete (manual proof) |
| POST | `/api/v1/Habit/{userId}/{habitVersionId}/executions/skip` | registered, subject, guard `plan.quota(skips)` | skip within the month's skips |
| GET | `/api/v1/Habit/{userId}/executions` | registered, subject | `id` (habit), `date` (dd-MM-yyyy), `limit`, `skip` |
| GET | `/api/v1/Habit/{userId}/executions/{executionId}` | registered, subject | **new**, owner-scoped |
| GET | `/api/v1/Habit/{userId}/overview` | registered, subject | `limit`, `skip` |
| POST | `/api/v1/habit-cron/mark-daily-failures` | admin | one manual tick |

MCP tools: `check.complete`, `check.skip`, `check.overview`, `check.executions` (same service methods, policy, subject and guard).

## Contract `check.v1`

`Report`, `PendingCheckAt`, `Streaks` (the lanes.md minimum), plus `MaxStreak`, `GetExecution`, `OwnsExecution`, `ExecutionForDate`. Event `check.settled`: `CheckSettled` in `contract/v1/Events.cs` (status is a lowercase string: done, missed, skipped, frozen, vacation).

## Needs and events

- `needs: [account.v1, plan.v1, habit.v1, vacation.v1, protection.v1, charity.v1]`. `charity.v1` is extra to lanes.md: the overview shows each habit's charity name (no cycle: charity needs nothing). The Bun build made the same call.
- habit and protection are reached through check's own ports (`domain/Ports.cs`, adapters in `data/Adapters.cs`), so a change in those contracts touches one file.
- Subscribes to `habit.changed` (retime pending checks), `habit.deleted` (drop pending checks, hide the habits' executions) and `account.timezone_changed` (move pending checks to the new zone).
- Purge hook: deletes every check, evidence and execution row of the user.

## Changed from LazyTax

1. **No more CheckEvents tables or stake bindings.** A settle writes `check.settled` to the kernel outbox in its own transaction; penalty, protection and friend subscribe. `PenaltyStakeBinding` moves to penalty. The tick no longer dispatches events (the kernel's dispatcher does), so `mark-daily-failures` returns opened + closed only.
2. **The skip ownership bug is fixed.** `SkipHabit` (LazyTax `Domain/Habit/Service.cs:93-97`) loaded the version by id only. Here the first read is `habit.GetVersion(Owner.User(userId), versionId)`: another user's version id is 404 before anything is read or written. Complete does the same.
3. **Complete calls check directly, not the proof router.** proof needs check, so check cannot call proof. The manual plugin always passed, so complete = report a manual, passed evidence. NFC and Strava still go proof → `check.v1 Report`.
4. **habit and check talk by events.** `HabitService.Update` called `RetimePending` synchronously; now habit emits `habit.changed` and check retimes when the outbox delivers it (seconds later). Same for the timezone change (`account.timezone_changed`, was `MoveToTimezone`), so the "days already over are settled now and returned in the response" part happens after the response, on delivery.
5. **One unit of work per (user, day) at settle time.** LazyTax decided vacation and spent the freeze outside the settle transaction. Here the user lock, the vacation check, the freeze spend and every settle of that day commit together, or none do. The freeze cap is reconciled through `protection.ReconcileCap(userId, maxStreak)` before every spend (LazyTax only when the balance was above 0).
6. **Executions keep copies.** `HabitId`, `UserId` and the version's stake (cents, ratio, currency, charity) are stored on each execution, since there is no join to habit's tables. Executions are unique per (habit, date); LazyTax's "adopt an existing execution" path is kept for that case.
7. **Overview proof is always `manual`.** LazyTax read NFC tags and proof bindings to show `nfc` or `strava`; proof needs check, so check can't ask it. A later `proof.*` event could feed it.
8. **Overview charity name** comes from `charity.v1`; a charity id that no longer resolves shows an empty name instead of failing the whole overview (LazyTax returned 404).
9. **Executions search is ordered** (date, then id); LazyTax paged an unordered query.
10. **Stats** (`App/Modules/Stats`, `Domain/Stats`) is entirely commented out in LazyTax; nothing to port.
11. **Debt in the overview** keeps LazyTax's rule: Missed executions with `PaymentProcessed = false`, which LazyTax never sets to true. The real debt figure is penalty's `outstanding`.
12. **The missed-habit flow is an integration test, not SIT.** The tick runs on the real clock and the app has no time override, so a black-box run can never see a habit made "now" miss. `tests/integration/MissedHabitFlowTests.cs` runs SIT flow 1 on the whole app (every real context, signed payment consent webhook) and calls `CheckService.Tick(tomorrow 01:00 UTC)` directly: failed execution, pending penalty, then charged and one inbox item. `tests/sit-flows/flow1-missed-habit` keeps the part before the deadline.
