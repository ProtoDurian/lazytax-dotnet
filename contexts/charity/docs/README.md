# charity

Owns the charity and cause catalogue: `charity.Charities`, `Causes`, `CharityCauses`, `ExternalIds`. Needs no other context.
The data is global: no table holds a user id, so reads take no `Owner` and the purge hook has nothing to delete.

**Contract (`charity.v1`):** `Get`, `Exists`, `Search`, `GetByExternalId`, `GetExternalKey(charityId, source)`.

**Routes (same paths as LazyTax):** `/api/v1/Charity` (search, get, supported-countries; admin: create, update, delete, `PUT {id}/causes`),
`/api/v1/Causes` (search, get; admin: create, update, delete), `POST /api/v1/charities/sync/pledge` (admin).
**MCP tools:** `charity.search`, `charity.get`, `charity.causes`.

## Changed from LazyTax
- Reads need a valid token (policy `registered`). LazyTax's search and get had no explicit policy.
- The Pledge client is a **fake gateway** (`FakePledgeGateway`, no network): 2 causes, 2 organisations. LazyTax's `PledgeClient` is HTTP; the donation calls (`CreateDonation`, `GetDonations`) belong to the disbursement context.
- Pledge sync runs the country x cause batches one after another (LazyTax created all tasks with concurrency 1, same effect). Gateway calls run outside any transaction; each batch upsert is its own unit of work.
- Charity delete and cause delete are single `DELETE` statements; links cascade through database foreign keys, as before.
- **Foreign keys from other areas are gone:** habit version, configuration, penalty, disbursement and charity balance rows keep a plain `CharityId` and ask `charity.v1.Exists`. LazyTax's cascade (deleting a charity deleted habit versions and penalties) is NOT carried over; callers must handle a missing charity.
- Errors: 404 uses problem `entity_not_found`; duplicate cause key is 409; unknown cause keys in `PUT {id}/causes` are 400; bodies are validated by plain checks (no FluentValidation).
- Search results are ordered by name, then id (LazyTax returned database order).
- `charity.v1.GetExternalKey` was added for disbursement (latest-synced key for a source).
