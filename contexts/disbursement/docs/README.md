# disbursement

Pays charities what their users' charged penalties add up to. One payout per (charity, currency), donated through Pledge.

- **Needs:** `charity.v1` (a charity's Pledge organisation), `penalty.v1` (the charged, unpaid penalties).
- **Provides:** `disbursement.v1` (`ProcessPending`, `Search`, `Get`).
- **Owns:** table `disbursement."Disbursements"`. It has no user column, so `purgeUser` has nothing to delete (it is registered anyway).
- **Routes** (all admin-only, prefix `/api/v1/disbursement`):

| Route | What |
|---|---|
| `POST /run` | one payout pass now (as LazyTax) |
| `GET /` | list payouts; `status`, `charityId`, `limit`, `skip` |
| `GET /{id}` | one payout; 404 when unknown |

- **MCP tools:** `disbursement.run`, `disbursement.list` (admin).
- **Worker:** `DisbursementWorker`, every `IntervalHours` (24), only when `Contexts:disbursement:Enabled` is true (default false, as LazyTax).
- **Settings** (`Contexts:disbursement`): `Enabled`, `MinPayoutCents`, `MaxGroupsPerRun` (100), `DonorFirstName`, `DonorLastName`, `DonorEmail`, `IntervalHours`.

## How a pass works

1. **Reconcile.** Each Pending payout older than 10 minutes is looked up at the vendor by its id. Found: mark Completed, no second donation. Conclusively absent: mark Failed and release the penalties. Lookup failed: leave it Pending.
2. **Claim** (one unit of work). `penalty.PayoutCandidates` locks the charged, unclaimed penalties; for each group with a Pledge org id, `penalty.ClaimForPayout` stamps them and a Pending row is written.
3. **Donate** (no transaction; the fake vendor has no network, but the real one would). Success: mark Completed. A definitive refusal (`DonationRejectedException`): mark Failed and release. Anything else is ambiguous (the donation may have landed): stay Pending for reconcile. Pledge has no idempotency key, so releasing on a guess could pay twice.
4. **Mark** (one unit of work). The payout row and `penalty.ReleasePayout` commit together; a Completed payout is never undone.

## Changed from LazyTax

- **Penalties and charities are reached through contracts.** LazyTax's `ClaimPendingPayouts` read `Penalties` and `ExternalIds` and stamped `Penalties.DisbursementId` itself. Now: `penalty.v1` `PayoutCandidates` / `ClaimForPayout` / `ReleasePayout`, and `charity.v1` `GetExternalKey(charityId, "pledge")` (a method this lane asked charity to add; it picks the most recently synced key).
- **No foreign keys.** The payout row keeps `CharityId` as a plain copy; the penalties keep `DisbursementId` as a plain copy. So the id is generated first, the penalties are stamped, then the row is inserted (LazyTax inserted first because of the FK).
- **Pledge is a fake.** `FakeDonationGateway` (in memory, scriptable per organisation id) replaces `PledgeDonationGateway` and its page-scanning reconcile. No network.
- **Money is cents plus a currency code** (`long`, `string`), not NodaMoney `Money`. The amount sent is still `cents / 100` with two decimals in the real adapter's job; the fake does not format it.
- **Status is text** (`pending`, `completed`, `failed`), not an int.
- **Errors are exceptions, not `Result`.** An ambiguous gateway failure is any exception except `DonationRejectedException`.
- **Failing a payout and releasing its penalties is one unit of work** (LazyTax: two statements in one repository transaction). If the release fails the payout stays Pending.
- **The in-process gate (`SemaphoreSlim`) is gone.** The scheduler runs one tick at a time per replica, and the claim's row lock keeps replicas apart.
- **The worker has no 5-minute start delay.** The kernel decides when the first tick runs. The interval is configurable.
- **Settings moved** from `Disbursement` to `Contexts:disbursement`.
- **New admin routes and tools** to list and read payouts (`GET /`, `GET /{id}`, `disbursement.list`, `disbursement.run`). LazyTax had only `POST /run`.
- **No owner-scoped 404.** A payout belongs to a charity, not a user. Every route is admin-only, so another user gets 403 (tested, with a real row, to show nothing leaks).
