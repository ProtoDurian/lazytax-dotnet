# protection

The user's freeze-day pool: how many freezes a user has banked, the one-per-day spend, and the weekly award for
perfect habit weeks. Port of LazyTax `Domain/Protection` and `App/Modules/Protection`.

## What it does

| Part | How |
| --- | --- |
| Balance | `GET /api/v1/Protection/{userId}` and MCP `protection.balance`. Reading cuts the balance to the cap first, so `balance` is never above `cap`. |
| Spend | `protection.v1 TryConsumeFreeze(userId, date)`. One freeze per user-local day; a second call for the same day is `true` and spends nothing. Takes the user lock and joins the caller's unit of work. |
| Cap | `ReconcileCap(userId, maxStreak)` asks `plan.v1 FreezeCap`, then cuts the balance. Cut freezes are gone for good. |
| Award | Worker `WeeklyAwardWorker` (hourly) and admin `POST /api/v1/Protection/award-weekly` (MCP `protection.awardWeekly`). One freeze per habit per perfect week, once ever per (habit, week). Emits `protection.awarded`. |
| Delete account | `ProtectionPurge` removes the user's rows from all four tables. |

## Contract (`protection.v1`)

`ReconcileCap(userId, maxStreak)`, `TryConsumeFreeze(userId, date)`, `Balance(owner)`.
Event `protection.awarded { userId, habitId, weekStart, balance, cap }`.

`needs: [account.v1, plan.v1]`. lanes.md lists only `plan`. `account` is added because the weekly award needs the
user's local today (`account.v1 Today`). It sits earlier in the `needs` order and makes no cycle.

## Tables (schema `protection`)

`UserProtections` (balance and last max streak), `FreezeConsumptions` (unique per user and date), `FreezeAwards`
(unique per habit and week, with a copy of the user id), `SettledDays` (new, see below). No foreign key to another
context; every row keeps a copy of `UserId`.

## Subscribes to

`check.settled { userId, habitId, date, status, maxStreak, ... }`: stored in `SettledDays` for the weekly award, and
`maxStreak` is remembered. The subscriber reads its own record of the payload (`CheckSettled`); no reference to check.

## Changed from LazyTax

1. **The weekly award reads protection's own ledger, not habit and streak tables.** LazyTax called
   `HabitRepository.SearchHabits` and `StreakService.GetStatusForHabit`. protection `needs` only plan and account, so
   it records every `check.settled` into `SettledDays`. A habit's week is perfect when it has at least one settled
   day and every one is `done`. LazyTax counted only the habit's scheduled days; every scheduled day settles, so the
   sets match. Two edge differences: a habit with no scheduled days was vacuously perfect in LazyTax and earns nothing
   here; a habit disabled mid-week is judged on the days it settled.
2. **The max streak comes from check.** LazyTax read `StreakRepository.GetUserMaxStreakAcrossHabits`. check now passes
   `maxStreak` on `check.settled` and in `ReconcileCap`; protection stores the last value
   (`UserProtections.MaxStreak`) and uses it for `GET` and for the award clamp. Between two settles the cap uses the
   value from the last settle.
3. **The award week is the last completed week, on any day.** LazyTax took the week holding yesterday, which is a
   completed week only when run on a Sunday (on a Monday it picks the week that began yesterday and could award early).
   Protection takes the Sunday-to-Saturday week before the user's current week, so the award is the same all week and
   idempotent. On a Sunday the result is identical.
4. **The award moved from a per-user call to a worker plus the admin route.** The worker looks at users with settled
   days in the last 14 days, one at a time, and finishes the week that ended. Settled days older than 45 days are pruned.
5. **Awards are an outbox event** (`protection.awarded`), written in the same unit of work as the award row.
   LazyTax emitted nothing.
6. **Ownership.** Every repository read takes an `Owner`. The balance has no id in any path, so the owner-scoped
   test is "user B's read shows none of A's and A's row is unchanged"; a user id in the path that is not the token's
   is a 403 from layer 3.
7. **One unit of work.** `TryConsumeFreeze` was its own EF transaction inside a repository; it is now one
   lock-then-write step in the caller's unit of work, so a settle that freezes a day and writes its execution commits
   or rolls back together (tested).

## Tests

- Unit (`tests/unit`): week maths, and `ProtectionService` against in-memory fakes.
- Integration (`tests/integration`): the whole app on Postgres, with plan and account swapped for fixed fakes
  (`ProtectionApp`) so the cap and "today" are the test's choice. Covers the routes (401, 403, 404, admin), the
  contract (idempotent consume, parallel consumes, rollback with the caller's unit, cap cut), the `check.settled`
  subscriber, the weekly award and its idempotence, and the purge (0 rows left for the user, other users untouched).
- SIT (`tests/sit/*.bru`): 401, 403 and 404 cases against the running app.
- `tests/.editorconfig` turns off xUnit1051 (pass `TestContext.Current.CancellationToken`) for this folder.

## Not done / open

- The award treats only status `done` as a success (`Weeks.IsDone`, case-insensitive). check must emit
  `check.settled` for done days too, not only missed ones, or no week is ever perfect, and must carry `maxStreak`.
- Plan and account are fakes in the integration tests; a run against the real plan and account is a check after both
  land on `main`.
