# penalty

The charity stakes a user owes for missed checks, the drain that charges them, and the
per-(charity, currency) accrual ledger. Ported from LazyTax `Domain/Penalty`, `App/Modules/Penalty`
and `Domain/Check/PenaltyStakeBinding.cs`.

`needs: [charity.v1, payment.v1]`. Emits `penalty.charged` and `penalty.failed`.

## What it owns

| Part                     | What                                                                                                                                                  |
| ------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| `penalty."Penalties"`    | one row per missed, staked check execution (unique `HabitExecutionId`); status text `pending`, `processing`, `charged`, `failed`, `skipped`          |
| `penalty."CharityBalances"` | one accrual row per (charity, currency), credited when a penalty is charged                                                                        |
| `check.settled`          | a `missed` check with a money stake and a charity enqueues one `pending` penalty (`stake * ratioBps / 10000`, exact cents), once per execution     |
| worker (15 min)          | claims a batch (`pending`, or `processing` older than the 10 min lease) with `FOR UPDATE SKIP LOCKED`, charges each through `payment.v1` outside any unit of work, then settles each row in its own unit |
| settle                   | `Succeeded` -> `charged` + charity credit + `penalty.charged`; `Pending` -> retry; `NoConsent` -> `skipped`; `Failed`/error -> retry, `failed` at 5 attempts + `penalty.failed` |
| `payment.completed`      | a penalty charge that settled after the drain's call returned is marked `charged` (no-op when the drain already did)                                 |
| delete guard             | blocks account deletion with 409 `account_deletion_blocked` while anything is owed                                                                   |
| purge                    | anonymise-retain: the user's rows get `UserId = 'deleted-user'`; amounts, charity and intent ids stay                                                |
| `penalty.v1`             | `Outstanding`, `CharityBalances`, `Search`, `Get`, and the payout methods disbursement uses (`PayoutCandidates`, `ClaimForPayout`, `ReleasePayout`) |

## Routes (`/api/v1/Penalty`)

| Route                          | Policy              |
| ------------------------------ | ------------------- |
| `GET /{userId}`                | registered, subject |
| `GET /{userId}/outstanding`    | registered, subject |
| `GET /{userId}/{id}`           | registered, subject |
| `GET /admin/search?userId=`    | admin               |
| `GET /admin/charity-balances`  | admin               |
| `POST /admin/process`          | admin               |

MCP: `penalty.search`, `penalty.get`, `penalty.outstanding`, `penalty.charityBalances` (admin).

Settings `Contexts:penalty` (defaults in code): `BatchSize` 100, `MaxAttempts` 5,
`ClaimLeaseMinutes` 10, `IntervalMinutes` 15, `BlockDeletionOnDebt` true.

## Changed from LazyTax

- **New routes and MCP tools.** LazyTax had no penalty controller; the routes above are new.
- **Debt = penalties not charged.** LazyTax 1.43.2 counted every failed staked execution with
  `PaymentProcessed = false`, a flag nothing ever set, so a user stayed in debt forever. Here debt is
  every penalty that is not `charged` (pending, processing, failed, skipped). A missed day with a
  stake but no charity makes no penalty row, so it is not debt here (in LazyTax it was).
- **Debt per currency.** LazyTax summed amounts across currencies and reported the first currency.
  `Outstanding` keeps that first total (the oldest unpaid currency) and adds `ByCurrency`.
- **Deletion keeps the ledger.** LazyTax's purge left a no-op seam (`AnonymizePenaltyLedger`); here
  the purge anonymises the rows instead of deleting them.
- **No foreign keys.** Charity, execution, habit, user and disbursement ids are copies. LazyTax's
  cascade from charity to penalties (deleting a charity deleted its money ledger) is gone.
- **Status is text**, not an int.
- **Payment owns the gateway phases.** LazyTax's penalty service passed `existingIntentId` and an
  `onIntentCreated` callback to Airwallex. Here penalty calls `payment.Charge` with its own id as the
  `requestId`; payment keeps the intent and its idempotency key `{requestId}-{attempt}`.
  `payment.failed` is not subscribed: the drain already counts each failed attempt.
- **Emails and inbox through events.** LazyTax emailed directly after the save, best effort. Penalty
  now writes `penalty.charged` / `penalty.failed` in the settle's unit of work; notification
  subscribes. The payload carries `CharityName` (from charity.v1); `HabitName` is gone (penalty
  does not need habit), so `notification.v1` is not in `needs`.
- **Write-back only while the claim holds.** Retry, skip and fail updates apply only while the row
  is still `processing`, so a late `payment.completed` that already charged it is never undone.
- **Charity credit is an upsert** (`ON CONFLICT ... AccruedCents + EXCLUDED`) instead of
  `SELECT ... FOR UPDATE` then insert, so two first credits of one charity no longer collide.
- **`Attempts` counts the final failed attempt too** (LazyTax's `MarkFailed` left it one short).
- **No in-process overlap gate.** The drain is idempotent under `SKIP LOCKED`; the kernel runs one
  tick at a time per replica.

## Tests

- `tests/unit`: stake binding, drain outcomes, debt, guard, search validation (in-memory fakes).
- `tests/integration`: the whole app on Postgres with the charge gateway and charity names faked:
  routes, owner-scoped 404, 401/403, MCP, drain + ledger + events, late `payment.completed`,
  payout claim/release, delete guard, purge (0 rows of the user left).
- `tests/sit`: Bruno; mints tokens from mock Logto (`{{logtoUrl}}/token`), covers 200, 401, 403, 404.
