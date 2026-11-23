# friend

Friends and "tell my friends if I miss" (LazyTax `Friend` and `FriendStake`, ported 1:1 at the same paths).

## What it owns

Tables, in schema `friend`:

| Table                | Holds                                                                 |
| -------------------- | --------------------------------------------------------------------- |
| `FriendRequests`     | one pending request per ordered pair (unique `FromUserId`, `ToUserId`) |
| `Friendships`        | one row per direction (unique `UserId`, `FriendUserId`)               |
| `FriendInvites`      | a user's one permanent invite code (unique `UserId`)                  |
| `HabitFriendWatchers`| a friend told when a habit is missed; `OwnerUserId` is a copy         |

No foreign key leaves the schema: users live in account, habits in habit.

## Routes (prefix `/api/v1`)

`Friend/{userId}` list, `search`, `requests` (get, post), `requests/{from}/accept`, `requests/{other}` (delete), `invites`, `invites/{code}/accept`, `{friendUserId}` (delete); `FriendStake/{userId}/{habitId}` get and put. Every route is `registered` with `subject: userId`. MCP tools: `friend.list`, `friend.search`, `friend.request`, `friend.accept`, `friend.watchers_get`, `friend.watchers_set`.

## Contract `friend.v1`

`AreFriends`, `ListFriendIds`, `ListWatcherIds`. Events: `friend.request_sent`, `friend.accepted` (friend's own subscribers post the inbox notices).

## Calls and events

- needs `account.v1` (usernames: `GetProfile`, `Usernames`, `SearchUsernames`), `habit.v1` (`Owns`, `GetCurrent`), `notification.v1` (`Post`).
- subscribes to `check.settled` (a `missed` status posts `friend_missed` to each watcher), `habit.deleted` (drops the habit's watchers), and its own two events.
- registers a `purgeUser` hook: every row where the user is either side.

## Ownership (auth layer 4)

Every repository read takes an `Owner`. A friendship is read by its `UserId` side, a request by either side of the caller. A route that names another user's row (a friend you do not have, a request nobody sent you, a habit you do not own) gives 404, never 403. Invite codes are the one public lookup on purpose: the code is the capability, and accepting it only sends a request the owner still decides on.

## Changed from LazyTax

- **Usernames come from `account.v1`.** LazyTax joined `Users`. Friend lists, requests and search now call `Usernames` and `SearchUsernames`, two methods added to `account.v1` for this.
- **Notices are outbox events, not calls.** LazyTax posted to the inbox best effort after the write. Now the write emits `friend.request_sent` / `friend.accepted` in the same transaction and friend's own subscriber posts after commit. The post cannot undo the friendship, same as before, and a failed post is retried by the outbox.
- **`friend_missed` comes from the `check.settled` event** (LazyTax: `FriendStakeBinding` through `IStakeBinding`). The habit task name is read through `habit.v1` `GetCurrent`, so a habit deleted before delivery tells nobody (LazyTax read the task by execution id). The post is one unit of work: if any watcher's post fails, none is kept and the outbox retries the event; the dedupe key `friend_missed:{executionId}` keeps a retry from doubling (LazyTax: `friend_missed:{checkId}`).
- **Watchers of a deleted habit** are dropped on `habit.deleted` (LazyTax filtered `DeletedAt == null` in a join).
- **Watchers carry `OwnerUserId`**, a copy of the habit's owner, in place of the join to `Habits`. Removing a friend deletes both sides' watchers by that column.
- **Ownership first, 404 not 403.** The habit check for `FriendStake` is `habit.Owns(userId, habitId)` before anything else.
- **No concurrent-create retry.** Requests, friendships and invites are `INSERT ... ON CONFLICT DO NOTHING`, so a lost race never aborts the transaction (LazyTax caught `UniqueConstraintException` and retried).
- **Habit row lock** (`SELECT ... FOR UPDATE` on `Habits` when replacing watchers) became the per-user lock `uow.LockUser`.
- **Validation** is hand-written `ValidationFailed` (400), not FluentValidation; the messages are the same.
