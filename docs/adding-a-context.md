# Adding a context (.NET)

One bounded context = one folder under `contexts/<name>/`, with three projects: a contract, the context, and its tests.
The kernel finds it by pattern at build time. **A new context edits no shared file** (see the last section).

`<name>` is lowercase letters and digits only (`vacation`, `friendstake`). `<Name>` is the same with the first letter upper-cased (`Vacation`, `Friendstake`).

Copy the reference contexts: `contexts/vacation` (owner-scoped CRUD, a plan quota under the user lock, an MCP tool), `contexts/plan` (config-driven rules, an admin route) and `contexts/account` (the delete flow, hooks, outbox event).

All commands below run inside the repo's dev shell (`direnv exec . <cmd>`). On the shared box every `dotnet build` / `dotnet test` goes through the slot queue with the low-load flags:

```bash
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 ../tools/slot.sh dotnet direnv exec . timeout 600 \
  dotnet test contexts/<name>/tests -m:1 -p:UseSharedCompilation=false -nodeReuse:false -nologo
```

Below, `DN` stands for that prefix and those flags.

---

## 1. The folder

```
contexts/<name>/
  context.yaml
  contract/
    <name>.Contract.csproj      # one line
    v1/                         # interfaces, records, events, problems: what others may use
  src/
    <name>.csproj               # one line
    Context.cs                  # the one registration
    api/v1/                     # controllers, request/response records
    mcp/                        # MCP tools
    domain/                     # model, services, ports. NO EF, Npgsql, ASP.NET, HTTP, IO (LYR001)
    data/                       # DbContext, rows, repositories, migrations/
  tests/
    <name>.Tests.csproj         # one line
    unit/                       # namespace Ctx.<Name>.Tests.Unit
    integration/                # namespace Ctx.<Name>.Tests.Integration
    sit/*.bru                   # Bruno system tests
  docs/README.md                # what it owns; "Changed from LazyTax"
```

Each `.csproj` is exactly:

```xml
<Project Sdk="Microsoft.NET.Sdk" />
```

`contexts/Directory.Build.props` gives every project its assembly name, namespace and references from the folder:

| Project     | Assembly / namespace  | References (automatic)                                                    |
| ----------- | --------------------- | ------------------------------------------------------------------------- |
| `contract/` | `Ctx.<Name>.Contract` | `platform`                                                                |
| `src/`      | `Ctx.<Name>`          | `platform`, own contract, every contract in `needs:`, the LYR001 analyzer |
| `tests/`    | `Ctx.<Name>.Tests`    | own `src` + contract, `testing` (which brings the whole app)              |

You never write a `ProjectReference`. If `src` uses a contract it did not list in `needs:`, it does not compile. Another context's `src` is never reachable.

## 2. `context.yaml`

```yaml
id: ctx_v4c8tn # ctx_ + 6 random lowercase letters/digits; set once, never change
name: vacation # = folder name = DB schema = MCP prefix
needs: [account.v1, plan.v1] # contracts it calls. ONE LINE, flow style: the build reads this line
provides: [vacation.v1] # contracts it implements (only <name>.v<n>)
uses: [postgres.main] # must be listed in /dependency.yaml
http: { prefix: /api/v1/Vacation } # its controllers live under this
mcp: { prefix: vacation } # tools are named vacation.<verb>
```

- Only these fields. An unknown field, a bad id, a `needs` nobody provides, a `uses` not in `dependency.yaml`, or a `needs` cycle stops the app **before it listens**, with a line like
  `context 'vacation' (contexts/vacation): needs plan.v1, but no context provides it. Fix: add the context that provides plan.v1, or remove it from needs`.
- The kernel registers contexts in `needs` order. **The arrows must not form a loop.** To go "backwards", use an outbox event (section 10) or a kernel hook (section 9).
- `needs` decides what compiles. Put a contract in `needs` only if you call it.

## 3. The contract project

`contract/v1/I<Name>V1.cs`: interfaces, records, event payloads and problems. No logic.

```csharp
namespace Ctx.Vacation.Contract.V1;

public interface IVacationV1
{
  Task<bool> IsAway(string userId, DateOnly date, CancellationToken ct = default);
  Task<IReadOnlyList<VacationPeriod>> ListActive(string userId, DateOnly date, CancellationToken ct = default);
}

public sealed record VacationPeriod(Guid Id, string UserId, DateOnly StartDate, DateOnly EndDate, string Timezone);
```

Rules:

- Method names as in `lanes.md`, PascalCase, no `Async` suffix (LazyTax style), `CancellationToken ct = default` last.
- A method that reads one user's rows on behalf of a caller takes `Owner owner` (from `LazyTax.Platform.Data`). A `string userId` argument means "a trusted caller (another context or a worker) already decided this user".
- Never rename or remove a method once merged. Add instead, **with a default body** (`=> throw new NotSupportedException(...)`, or a call to an existing member), so fakes other lanes wrote against the old interface still compile. Your implementation overrides it.
- The interface is named `I<Name>V<n>` in namespace `Ctx.<Name>.Contract.V<n>` (e.g. `Ctx.Habit.Contract.V1.IHabitV1`). The kernel relies on this while your context is pending (below).
- **Pending contexts:** once your contract and `context.yaml` are merged but `src/Context.cs` is not, the kernel boots you as _pending_: your contracts resolve to placeholders that answer 501 `context_not_built`. So lanes that need you boot and test their own code meanwhile; anything that really calls you waits for your src.
- Problems that other contexts throw or catch live here too (example: `Ctx.Plan.Contract.V1.TierInsufficient`).

## 4. `Context.cs`: the one registration

Class `Ctx.<Name>.<Name>Context : IContext`, file `src/Context.cs`. The kernel generates the call to it inside the build; nothing to add anywhere else.

```csharp
using Ctx.Vacation.Contract.V1;
using LazyTax.Platform.Context;
using LazyTax.Platform.Mcp;

namespace Ctx.Vacation;

public sealed class VacationContext : IContext
{
  public void Register(ContextBuilder c)
  {
    // services, repositories, guards: plain DI, scoped
    c.Services.AddScoped<VacationRepository>();
    c.Services.AddScoped<VacationService>();
    c.Services.AddScoped<VacationQuotaGuard>();

    c.Provide<IVacationV1, VacationV1>("vacation.v1"); // each entry of provides:, exactly once
    c.Db<VacationDb>();                                 // its one DbContext (section 5)
    c.AddControllers();                                 // serve this assembly's controllers (section 6)
    c.Tool(VacationTools.Create);                       // MCP tools (section 7)
    c.Worker<VacationSweepWorker>();                    // background jobs (IContextWorker), if any
    c.Subscribe<HabitChanged, HabitChangedHandler>("habit.changed"); // outbox subscribers (section 10)
    c.PurgeUser<VacationPurge>();                       // account deletion (section 9)
    // c.DeleteGuard<T>() and c.BeforePurge<T>() if you block or pre-clean deletion
  }
}
```

`c.Settings` is your own config section `Contexts:<name>` (from `kernel/appsettings.json`).

The kernel refuses to start if `provides:` and the `c.Provide` calls differ, if there are controllers but no `http.prefix`, or if a tool is not named `<mcp prefix>.<verb>`.

## 5. The DbContext and its migrations

One DbContext per context, in its own schema `<name>`, built on the request's shared connection (`c.Db<T>()` does that).

```csharp
// src/data/VacationDb.cs
using Microsoft.EntityFrameworkCore;

namespace Ctx.Vacation.Data;

public sealed class VacationDb(DbContextOptions<VacationDb> options) : DbContext(options)
{
  public DbSet<VacationPeriodRow> VacationPeriods => this.Set<VacationPeriodRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("vacation"); // REQUIRED, first line; the app refuses another schema
    b.Entity<VacationPeriodRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.UserId);
    });
  }
}
```

- **No foreign key to another context's table.** Keep a copy of `UserId` (a `string`, max 128) on your rows.
- Migrations go in `src/data/migrations/`. From the repo root:

```bash
direnv exec . dotnet tool restore
DN dotnet build kernel/Kernel.csproj        # dotnet ef needs the startup project built once
direnv exec . dotnet ef migrations add Init --no-build \
  --project contexts/vacation/src --startup-project kernel \
  --context VacationDb --output-dir data/migrations
```

- History lives in `vacation.__EFMigrationsHistory`. The kernel runs every context's migrations at start (and the test fixture does too).
- Two contexts adding tables never touch the same file (no shared model snapshot).

## 6. Controllers with policies (auth layers 1-3, 5)

```csharp
// src/api/v1/VacationController.cs
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Vacation.Api.V1;

[Route("")]                              // relative to http.prefix (/api/v1/Vacation)
public sealed class VacationController(VacationService service) : ContextController
{
  [Policy(Policies.Registered), Subject("userId"), Guards(typeof(VacationQuotaGuard))]
  [HttpPost("{userId}")]
  public async Task<VacationRes> Create(string userId, [FromBody] CreateVacationReq req, CancellationToken ct)
    => (await service.Create(userId, req.ToRecord(), ct)).ToRes();

  [Policy(Policies.Registered), Subject("userId")]
  [HttpDelete("{userId}/{id:guid}")]
  public async Task<IActionResult> Delete(string userId, Guid id, CancellationToken ct)
  {
    await service.Delete(Owner.User(userId), id, ct); // throws NotFound -> 404
    return this.NoContent();
  }
}
```

- **Layer 1** (JWT: JWKS, issuer, audience, expiry) is the kernel's. Nothing to write.
- **Layer 2** `[Policy(...)]` is **required on every action or its controller**. `Policies.Public`, `Policies.Registered` (any valid token), `Policies.Admin` (`scope` has `admin`), or a configured one (`Auth:Policies:<name>: { Field: scope|roles, Type: Any|All, Target: [...] }`). The app refuses to start with an endpoint that has none.
- **Layer 3** `[Subject("userId")]`: the `{userId}` route value must be the token's `sub`, unless admin; else 403. Never hand-check `sub` in an action.
- **Layer 5** `[Guards(typeof(A), typeof(B))]`: section 8.
- `this.Caller` gives `Sub`, `IsAdmin`, `Scopes`, `RequireSub()`.
- **Errors:** throw a `Problem` (`LazyTax.Platform.Problems`): `NotFound` 404, `ValidationFailed` 400, `Unauthenticated` 401, `Forbidden` 403, `Conflict` 409, `PreconditionFailed` 412, `UserBusy` 409, or your own subclass `Problem(status, id, title, detail, data)`. The kernel writes problem-details JSON: `{ type: "/problems/v1/<id>", title, status, detail, data, traceId }`. No `Result` types, no try/catch in controllers.
- Keep LazyTax's paths: the prefix plus the action template must give the same URL as LazyTax.
- `kernel policies` prints the table: `direnv exec . dotnet run --project kernel -- policies`.

## 7. MCP tools

Served at `/mcp` (JSON-RPC: `initialize`, `tools/list`, `tools/call`). Same JWT, policy, subject rule and guards as a route, and **the same service method**.

```csharp
// src/mcp/VacationTools.cs
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Vacation.Mcp;

public static class VacationTools
{
  public sealed record CreateArgs(string UserId, string StartDate, string EndDate, string Timezone);

  public static readonly McpToolDefinition Create = McpTool.Define<CreateArgs>(
    "vacation.create", "Create a vacation period for a user", Policies.Registered,
    async call => (await call.Services.GetRequiredService<VacationService>()
        .Create(call.Args.UserId, ToRecord(call.Args), call.Ct)).ToRes(),
    subject: "userId",                  // the argument checked against sub (camelCase name)
    typeof(VacationQuotaGuard));        // guards, in order
}
```

Every context has at least one tool.

## 8. Layer 5: business guards, in this order

DESIGN.md section 4: **plan first, then the debt gate, then lock + count + write**.

1. **Declared guards** (`[Guards]` on the route, `guards` on the tool) run first, in the order given, and must be ordered by `GuardStage` (`Plan`, then `Debt`); the app refuses another order. A guard is cheap and reads only:

```csharp
public sealed class VacationQuotaGuard(IPlanV1 plan) : IGuard
{
  public GuardStage Stage => GuardStage.Plan;
  public string Label => "plan.quota(vacationWindows)";

  public async Task Check(GuardContext ctx, CancellationToken ct)
  {
    var q = await plan.Quota(ctx.UserId, QuotaKind.VacationWindows, ct);
    if (q.Limit <= 0) throw new TierInsufficient(q.Tier, q.Key, q.Limit);
  }
}
```

2. **Lock, then count, then write** stays in the service, inside ONE unit of work:

```csharp
public Task<VacationPrincipal> Create(string userId, VacationRecord rec, CancellationToken ct) =>
  uow.Start(async () =>
  {
    await uow.LockUser(userId, ct);                                  // first, before any row lock
    var q = await plan.Quota(userId, QuotaKind.VacationWindows, ct);
    if (await repo.CountWindowsForYear(Owner.User(userId), rec.StartDate.Year, ct) >= q.Limit)
      throw new TierInsufficient(q.Tier, q.Key, q.Limit);
    return await repo.Create(userId, rec, ct);
  }, ct);
```

- `IUnitOfWork.Start` = LazyTax's `TransactionScope(Required, AsyncFlow)`, **ReadCommitted by default**, so the count after `LockUser` sees every commit before the lock was granted. `uow.Start(UnitIsolation.RepeatableRead, ...)` is opt-in: then `LockUser` must be the first statement, and a busy lock re-runs the whole unit with a fresh snapshot. Nested calls join (and keep the outer isolation). Calls into other contexts' contracts inside it join the same transaction (one shared connection).
- It retries the whole function on serialization failure / deadlock (40001, 40P01) up to 3 times, so **no HTTP, email or payment inside**; use the outbox.
- `LockUser` waits up to 5 s, then throws `UserBusy` (409).

## 9. The owner-scoped repository (layer 4) and the purge hook

**Every read takes an `Owner`. The first read of every request is owner-scoped.** (LazyTax's `SkipHabit` loaded by id only; do not.)

```csharp
public sealed class VacationRepository(VacationDb db)
{
  public Task<VacationPeriodRow?> Get(Owner owner, Guid id, CancellationToken ct) =>
    db.VacationPeriods.AsNoTracking().OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(x => x.Id == id, ct);
}
```

- `Owner.User(userId)` for a user; `Owner.Anyone()` only on admin routes and workers, spelled out. Never `string? userId` meaning "everyone".
- Chains through your own foreign keys: `.OwnedBy(owner, v => v.Habit.UserId)`; EF makes the JOIN. Across contexts: ask the other contract (`habit.Owns(userId, id)`) or keep a copy of `UserId`.
- 0 rows → throw `NotFound` (404), never 403.

**Account deletion hooks** (account owns the flow; you only register):

| Hook                                        | When                                 | Use                              |
| ------------------------------------------- | ------------------------------------ | -------------------------------- |
| `c.DeleteGuard<T>()` (`IDeleteGuard.Check`) | first; throw a `Problem` to block    | penalty: outstanding debt        |
| `c.BeforePurge<T>()` (`IBeforePurge.Run`)   | outside any transaction, best effort | payment: revoke consents         |
| `c.PurgeUser<T>()` (`IUserPurge.Purge`)     | inside account's one unit of work    | **every context with user rows** |

```csharp
public sealed class VacationPurge(VacationDb db) : IUserPurge
{
  public Task Purge(string userId, CancellationToken ct) =>
    db.VacationPeriods.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
}
```

## 10. Outbox events and subscribers

Emit, inside the unit of work that changes the data:

```csharp
await outbox.Add("habit.changed", new { userId, habitIds }, ct); // IOutbox; name = "<your context>.<event>"
```

- The row commits with your transaction; the kernel's dispatcher delivers it after commit, per subscriber, with backoff and an attempt cap.
- Put the payload record and the event name constant in your contract (`HabitEvents.Changed`).

Subscribe (in `Context.cs`): `c.Subscribe<HabitChanged, HabitChangedHandler>("habit.changed");`

```csharp
// The subscriber's OWN record of the fields it reads; no need to reference the emitter's contract.
public sealed record HabitChanged(string UserId, Guid[] HabitIds);

public sealed class HabitChangedHandler(CheckService checks) : IOutboxSubscriber<HabitChanged>
{
  public Task Handle(OutboxEvent meta, HabitChanged e, CancellationToken ct) => checks.Retime(e.UserId, e.HabitIds, ct);
}
```

- Each delivery runs in its own unit of work and is recorded, so it runs once per subscriber. Be idempotent anyway; no HTTP inside.
- Subscribing needs no `needs:` entry, so events are how a context reacts to one that depends on it.

## 11. Tests (three kinds, plus the owner-scoped test)

Start Postgres once: `task pg:up PORT=<your port>` (lanes.md lists one per builder), then `export LT_PG_PORT=<your port>`.

`tests/AssemblyInfo.cs`:

```csharp
[assembly: AssemblyFixture(typeof(LazyTax.Testing.KernelApp))]
```

**Unit** (`tests/unit/`, namespace `Ctx.<Name>.Tests.Unit`): domain logic with fakes, no DB.

**Integration** (`tests/integration/`, namespace `Ctx.<Name>.Tests.Integration`): the whole app on real Postgres, in-process, a fresh database per test assembly.

```csharp
namespace Ctx.Vacation.Tests.Integration;

public sealed class VacationApiTests(KernelApp app)
{
  [Fact]
  public async Task Create_then_list()
  {
    var user = TestTokens.NewUser();                 // fresh sub per test: tests never share rows
    var client = app.ClientAs(user);                 // or app.ClientAs(user, "admin"), app.Anonymous()
    var res = await client.PostAsJsonAsync($"/api/v1/Vacation/{user}", new { /* ... */ });
    res.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public Task Other_user_gets_404() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (client, a) => (await (await client.PostAsJsonAsync($"/api/v1/Vacation/{a}", Body()))
      .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!,
    requestAsB: (b, id) => new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/Vacation/{b}/{id}"));
}
```

- **One owner-scoped test per entity** (user B, with B's own userId in the path and A's id, gets 404).
- Cover 401 (no token), 403 (other user's path) and 404.
- Helpers (`LazyTax.Testing`): `await app.SignUp()` creates a user the LazyTax way (id + access token, then a UTC configuration) and returns its sub; `await app.SetTier(user, "pro")` sets the plan as admin; `await client.GetAsync(url).Expect(HttpStatusCode.OK)` asserts the status (printing the body) and returns the JSON; `await client.CallTool("vacation.create", new { ... })` calls an MCP tool and returns the JSON-RPC result (`isError`, `structuredContent`).
- In tests a 500 problem's `detail` names the exception (Development only), so read the body first.
- Need a fake or an extra service for one class? Subclass `KernelApp`, override `ConfigureTestServices`, and use it as an `IClassFixture` (see `tests/kernel/OutboxTests.cs`).
- `app.InScope(sp => ...)` to call a contract directly, `app.DispatchOutbox()` to deliver events now, `app.Count("SELECT count(*) FROM vacation.\"VacationPeriods\" WHERE \"UserId\" = @u", ("u", user))` for "0 rows left".
- Run: `DN dotnet test contexts/<name>/tests --filter "FullyQualifiedName~.Unit."` (or `~.Integration.`).

**SIT** (`tests/sit/`): a Bruno collection (`bruno.json` + `.bru` files) run against the real app by `task sit` (see `docs/sit.md`). Variables: `baseUrl`, `subA`/`tokenA`, `subB`/`tokenB`, `tokenAdmin`, `tokenExpired`, `logtoUrl`. All collections share one database, so make your own users: `POST {{logtoUrl}}/token { "sub": "sit-<name>-a", "scope": "", "claims": { "username": "...", "email": "...", "email_verified": true } }`, keep `res.body.access_token` with `bru.setVar`, and sign up with it as both `idToken` and `accessToken` (see `contexts/vacation/tests/sit/`). Cover 401, 403 and 404.

## 12. Shared files a new context touches

**None.** Adding `contexts/<name>/` is enough:

| Would-be shared file             | Why it is not touched                                                                  |
| -------------------------------- | -------------------------------------------------------------------------------------- |
| `all.proj`                       | finds `contexts/*/contract`, `src`, `tests` by pattern                                 |
| `kernel/Kernel.csproj`           | references `contexts/*/src/*.csproj` by pattern; the registry is generated into `obj/` |
| `contexts/Directory.Build.props` | derives names and references from the folder and `needs:`                              |
| a DbContext / model snapshot     | one per context, in its own folder                                                     |
| a central error switch           | problems are subclasses, mapped by status                                              |
| a policy table                   | policies are attributes on your routes                                                 |
| a lock file                      | none in this repo                                                                      |

The only exceptions, when you need them:

- `dependency.yaml`: only if you use new infrastructure (a new `uses:` entry).
- `kernel/appsettings.json`: only if your context has settings (`Contexts:<name>`). Prefer defaults in code.
- `Directory.Packages.props`: only for a new NuGet package.
