# habit

Owns habits, their versions and routines (LazyTax `Habit`, `HabitVersion`, `Routine`). Schema `habit`:
`Routines`, `Habits`, `HabitVersions`. Needs `account.v1` (timezone), `plan.v1` (habit quota), `charity.v1` (charity exists).
Provides `habit.v1` (`ListSchedulable`, `GetVersion`, `GetCurrent`, `GetCurrentMany`, `ListCurrent`, `ListVersions`, `Owns`, `CountHabits`).

Routes (prefix `/api/v1`): `Habit/{userId}` (search, create), `Habit/{userId}/{id}` (get, update, delete),
`Habit/{userId}/order` (reorder), `Routine/{userId}[/{id}]` (search, get, create, update, delete).
MCP: `habit.list|get|create|update|delete|reorder|routine_list|routine_create|routine_delete`.

Events emitted: `habit.changed` {userId, habitIds} (every create/update/delete), `habit.deleted` {userId, habitIds}.
Hooks: `PurgeUser` removes every habit, version and routine of the user.

Not here (check owns them): complete, skip, executions search, overview, stats, streaks, the tick.

## Changed from LazyTax
- **Retime is an event.** `HabitService.Update` called `checkService.RetimePending` directly; it now emits `habit.changed`, and check retimes on delivery (after commit, so slightly later).
- **NFC tags are released by an event.** `HabitRepository.Delete` and routine delete removed `NfcTags` rows; habit now emits `habit.deleted` and proof releases its own tags.
- **Charity foreign key is gone.** `FK_HabitVersions_Charities` crossed contexts; habit asks `charity.Exists` and returns 400 on `CharityId`, the same answer as before.
- **Quota is a guard plus the locked count.** `[Guards(HabitQuotaGuard)]` refuses a plan with 0 habits first; the count against the limit runs under `LockUser` in one unit of work (as `HabitRepository.Create` did).
- **Admin may use the habit routes.** LazyTax's habit routes were self-only (`GuardAsync`); the kernel's subject rule (layer 3) lets an admin act on any `{userId}`, as the routine routes already did.
- **Every read is owner-scoped.** `GetHabit(id)` / `GetVersion(id)` took no owner; now `GetVersion` joins version -> habit -> user, so another user's version id is null (check's skip test relies on it).
- **Stake is stored as cents only**, no `NodaMoney`; currency fixed to USD as LazyTax's mapper did. Ratio is fixed at 100%.
- **Validation** is hand written (no FluentValidation) with the same rules and messages; errors are the kernel's `validation_error` problem with per-field `data.errors`. Plan errors now come before validation errors because guards run first.
- **`SearchHabitQuery.UserId` is ignored**: the path user is always the owner (LazyTax overwrote it too).
- **Timezone for check** is resolved in `habit.ListSchedulable` through `account.GetTimezone`, falling back to the habit's own timezone (LazyTax joined `Configurations`).
