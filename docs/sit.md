# SIT: black-box tests with Bruno

`task sit` runs the real app as a separate process and calls it over HTTP with Bruno. The log-file test (`task test:logfile`) uses the same stack.

## What `task sit` does

1. Builds the kernel and mock Logto (`task sit:build`, Release).
2. Starts Postgres and makes a fresh database:
   - `LT_PG` set (a connection string without `Database`, as `KernelApp` reads it): uses that server. CI does this with a service container.
   - `LT_PG` not set: starts (or reuses) a Docker container `lt-dotnet-sit-ci`, label `lazytax-model=1`, port `LT_SIT_PG_PORT` (default 55461), and removes it afterwards if this run started it.
3. Starts mock Logto, a local issuer with a JWKS and a `POST /token`. No real Logto, no secrets.
4. Starts the app (`LazyTax.Kernel.dll`) with `ASPNETCORE_URLS`, `ConnectionStrings__Main`, `Auth__Issuer`, `Auth__Audience` and `Workers__Enabled=true`. One process runs the API, the workers (the cron tick) and the outbox dispatcher. It waits for `GET /health`.
5. Runs every collection with `bunx @usebruno/cli@<pinned> run`, one JUnit file per collection in `reports/sit/`.
6. Stops everything, even when a collection fails or the app never starts: app, mock Logto, database, and the Postgres container if this run started it.

The Bruno version is pinned in one place: `BRUNO_VERSION` in `tools/sit/sit.ts` (override with `SIT_BRUNO_VERSION`).

`task sit -- --only <text>` runs only collections whose path contains the text, for example `--only contexts/habit/` or `--only flow2`.

## Which collections run

A collection is a folder with a `bruno.json`. These are found:

| Where                        | What                                       |
| ---------------------------- | ------------------------------------------ |
| `contexts/<name>/tests/sit/` | one per context; each context adds its own |
| `tests/sit/`                 | the kernel's own: health, 401, 403         |
| `tests/sit-flows/<flow>/`    | one collection per cross-context flow      |

To add a collection for your context: create `contexts/<name>/tests/sit/bruno.json` (copy `tests/sit/bruno.json`, change `name`) and put `.bru` files beside it. Nothing else to register.

## Tokens: your `.bru` files only name a user

Every collection gets these variables (Bruno `--env-var`). No `.bru` file mints a token.

| Variable                 | Meaning                                               |
| ------------------------ | ----------------------------------------------------- |
| `baseUrl`                | the app, for example `http://127.0.0.1:41233`         |
| `subA`, `tokenA`         | user A, no scopes                                     |
| `subB`, `tokenB`         | user B, no scopes (the "other user" in 403/404 tests) |
| `subC`, `tokenC`         | user C: use it when the test deletes the account      |
| `subD`, `tokenD`         | user D: owner in the skip-ownership flow              |
| `subAdmin`, `tokenAdmin` | an admin: scope `admin`                               |
| `tokenExpired`           | A's token, already expired (must give 401)            |
| `logtoUrl`               | mock Logto; `POST {{logtoUrl}}/token` mints more      |

A request picks a user in its auth block:

```
auth:bearer {
  token: {{tokenB}}
}
```

Need another user or scope? Add a request that posts `{ "sub": "x", "scope": "admin plus" }` to `{{logtoUrl}}/token` and keep `res.body.access_token` with `bru.setVar`. The body may also carry `"claims": { "email": "..." }`, `"aud"` and `"expiresIn"` (seconds; negative gives an expired token).

All collections in one run share the app and the database. Create your data under your own users, do not depend on another collection's data, and use `subC` for a test that deletes a user.

## Rules for a context's collection

- Cover the happy path, the 401 (no token), the 403 (another user's `userId` in the path, or a non-admin on an admin route) and the 404 (another user's id with your own `userId` in the path).
- Use LazyTax's paths, for example `{{baseUrl}}/api/v1/Vacation/{{subA}}`.

## Cross-context flows (`tests/sit-flows/`)

| Flow                   | What it proves                                                                                                                                                                                                               |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `flow1-missed-habit`   | sign up, configuration, habit with a stake, tick before the deadline: no failed execution yet. The missed part (failed execution, pending penalty, inbox item) is `contexts/check/tests/integration/MissedHabitFlowTests.cs` |
| `flow2-skip-ownership` | user B skipping user D's habit version gets 404 (own path) or 403 (D's path), and D has no new execution                                                                                                                     |
| `flow3-delete-account` | after `DELETE /api/v1/User/Me`, user C's habits and inbox are gone and admin search finds no user C                                                                                                                          |

They are written against `briefs/lanes.md` routes and LazyTax's bodies, so they go green as the contexts land. Assumptions to check when a context lands:

- Sign up posts the user's token as both `idToken` and `accessToken`.
- The deadline is `00:00` in UTC, so the tick only opens tomorrow and settles nothing of A.
- "Penalty pending" is the execution with `status: failed` and `paymentProcessed: false`: LazyTax has no penalty HTTP route to read.
- The tick is `POST /api/v1/habit-cron/mark-daily-failures` as admin, on the real clock. The app has no time override, so flow 1 stops before the deadline; the missed part runs in `contexts/check/tests/integration/MissedHabitFlowTests.cs` (the whole app, `CheckService.Tick` called with a later clock).

## The log-file test

`tests/logfile/logfile.test.ts` (`task test:logfile`) starts the app with `LOG_FILE` set and asserts on the file:

- one JSON line per request, with `context`, `route` or `tool`, `sub`, `requestId`, `status` and `durationMs`;
- no token, email or request body anywhere in the file (the token carries an email claim, and the bodies carry a marker string);
- an error request logs at level `error` with its problem type, and the line's `requestId` equals the `traceId` the caller got in the problem body;
- one MCP `tools/call` is logged with its tool name. The test calls the first tool `tools/list` returns, so it needs at least one tool in the app.

The app does not read a caller-supplied request id, so the test sends requests one after another and matches the n-th new line to the n-th request.

## Testing the harness without the app

`SIT_APP_CMD="bun path/to/stand-in.ts"` replaces the app. The stand-in reads `ASPNETCORE_URLS`, `ConnectionStrings__Main`, `Auth__Issuer`, `Auth__Audience` and `LOG_FILE`.

## Mock Logto changes made for this lane

`tools/MockLogto/Program.cs` accepts `claims` (extra claims, never overriding `sub` or `scope`) and a negative `expiresIn` (an already expired token). Both are used by the harness and the log-file test.
