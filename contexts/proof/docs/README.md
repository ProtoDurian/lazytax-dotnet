# proof

Proof plugins (manual, nfc, strava), proof bindings, NFC tags and the Strava link. Ported from LazyTax
`App/Modules/{Proof,NfcTag,Strava}` and `Domain/{Proof,NfcTag,Strava}`.

- Contract: `proof.v1` (`Route`, `ListPlugins`, `GetBinding`, `ListPluginKeys`, `ListTaggedHabits`).
- Needs: `account.v1` (NFC resolve's "today"), `plan.v1` (`EnsureTier` for a plugin's `minTier`), `habit.v1`
  (`Owns`, `GetCurrent`), `check.v1` (`PendingCheckAt`, `Report`, `ExecutionForDate`).
- Routes (`/api/v1`): `Proof/plugins`, `Proof/{userId}/{habitId}` get, bind (PUT), unbind (DELETE);
  `NfcTag/{userId}/{tagId}` link (PUT), resolve (GET), unlink (DELETE), tap (POST `.../tap`);
  `Strava/connect-url/{userId}`, `Strava/callback`, `Strava/{userId}` status and disconnect,
  `Strava/webhook` (GET challenge, POST event).
- MCP: `proof.plugins|binding|bind|unbind|nfcLink|nfcResolve|nfcTap|stravaStatus|stravaDisconnect`.
- Events: emits `proof.evidence_revoked {userId, proofKey, externalId|null}`; subscribes to `habit.deleted`.
- Worker: `StravaInboxWorker` (every 20 s, only when Strava is on) drains the webhook inbox.
- Hooks: `PurgeUser` deletes bindings, NFC tags, the Strava link and queued Strava events; `BeforePurge`
  (best effort, outside any unit) revokes LazyTax at Strava.
- Config (`Contexts:proof:Strava:*`): `Enabled` (default false, as LazyTax), `ClientId`, `ClientSecret` (also
  signs the connect state), `VerifyToken`, `WebhookSecret`, `WebhookToleranceSec` (300), `EncryptionKey`,
  `SubscriptionId`, `CallbackUrl`, `AppRedirectUri`, `MaxAttempts` (5), `PollSeconds`, `StateLifetimeMinutes`.
  The defaults are development values: set your own.
- Gateway: Strava is a fake with no network (`src/gateways/FakeStravaApi.cs`; the code `athlete-<id>` grants
  that athlete). The real HTTP adapter is not part of this build.
- Tests: `tests/unit` (router, plugins, helpers, Strava service), `tests/integration` (real Postgres; habit,
  check, account and plan are in-memory fakes in `tests/Fakes.cs` until those contexts' real versions are on
  main), `tests/sit/*.bru`.

## Changed from LazyTax

- **Webhook is signed.** LazyTax trusted unsigned Strava events and relied on fetching the activity with the
  owner's own token. Now `POST Strava/webhook` needs `X-Lazytax-Timestamp` (unix seconds) and
  `X-Lazytax-Signature` (hex HMAC-SHA256 of `<timestamp>.<raw body>` with `WebhookSecret`). The compare is
  constant time, the timestamp must be within `WebhookToleranceSec` of now (a replay or a future stamp is
  refused), and a bad or missing signature is 403. Strava itself does not sign, so a signing proxy in front of
  the endpoint is expected. A signed but unreadable body is still answered 200 and dropped, as LazyTax did.
  The challenge's verify token is compared in constant time.
- **No HTTP inside a transaction, so the refresh lock is a lease.** LazyTax held a `FOR UPDATE` row lock across
  the token-refresh HTTP call. Now a worker takes a short `RefreshLeaseUntilUtc` claim in its own statement,
  calls Strava outside any unit, then saves the tokens and clears the lease. A second worker that finds the
  lease held uses the saved tokens if they are fresh, else fails the event (the inbox retries it after its
  lease). Two workers never spend the same single-use refresh token. The activity fetch is also outside; only
  routing runs in a unit of work.
- **Ownership on the first read.** Every repository read takes an `Owner`. LazyTax's router compared
  `binding.UserId != e.UserId` after loading the binding by habit id; now the binding is read scoped to the
  event's user. The NFC claim is one `INSERT ... ON CONFLICT ... WHERE user_id = me` statement, so two users
  racing for a tag cannot both win and a foreign tag never loads.
- **Cross-context reads are contracts, not joins.** `ProofBindings` keeps a copy of the habit's `UserId`
  instead of joining `Habits`. The "non-deleted habit" filters moved to `habit.deleted`: proof deletes the
  binding and releases the NFC tags of a deleted habit (LazyTax joined `Habits.DeletedAt` on every read, and
  deleted tags inside `HabitRepository.Delete`).
- **NFC "today's execution" comes from check.** LazyTax's repository read `HabitExecutions`; now
  `check.ExecutionForDate`. "Today" is `account.Today`.
- **Strava evidence goes through an event.** LazyTax deleted `HabitEvidence` rows directly (disconnect,
  deauthorize, activity deleted in Strava). Now proof emits `proof.evidence_revoked` in the same unit as its own
  deletes and check removes the rows after commit. The day the evidence proved stays done. An
  activity-deleted event is scoped to the athlete's user (LazyTax matched by activity id across all users).
- **Tokens are encrypted by proof** (AES-256-GCM, key from `EncryptionKey`) because the kernel has no
  `IEncryptor`.
- **New `nfc` plugin and `POST NfcTag/{userId}/{tagId}/tap`.** LazyTax resolved the tag in the app and then
  pressed Complete (manual). A tap now routes through `proof.v1.Route` with evidence key `nfc`. The old
  resolve-then-complete path still works. The plugin is always-bound, so it shows in `Proof/plugins` with
  `alwaysBound: true` (the app must not offer it as a bindable proof).
- **A disabled habit does not stop other habits.** On an untargeted event (a Strava activity) a bound habit
  that check answers 404 (switched off) is skipped; LazyTax's router returned the first failure and dropped the
  rest. A targeted event still gets the 404.
- **Bind runs under the user lock** so two concurrent binds of one habit leave one row (LazyTax relied on the
  unique index and returned a 500 to the loser).
- **Strava endpoints return 404 when `Enabled` is false**, as LazyTax; the strava plugin is also left out of
  `Proof/plugins`.
- **Errors are the kernel's problem-details JSON**, with the same statuses; validation messages are written by
  hand instead of FluentValidation (same rules).
- **Event name** is `proof.evidence_revoked` (snake case: the kernel's event-name rule).
