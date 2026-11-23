# notification

The user's in-app inbox and the transactional emails. Port of LazyTax `App/Modules/Notification` and `Domain/Notification`.

## What it provides

`notification.v1` (`contract/v1/INotificationV1.cs`):

| Method                                                             | What it does                                                    |
| ------------------------------------------------------------------ | --------------------------------------------------------------- |
| `Post(userId, kind, title, body, actorUserId?, data?, dedupeKey?)` | Puts a row in the user's inbox. Joins the caller's transaction. |
| `Email(userId, template, data?)`                                   | Queues an email through the outbox. Never throws on delivery.   |
| `UnreadCount(userId)`                                              | Unread inbox rows.                                              |

## Routes (`/api/v1/Inbox`, policy `registered`, subject `userId`)

| Route                                 | Result                                             |
| ------------------------------------- | -------------------------------------------------- |
| `GET /{userId}?unreadOnly&limit&skip` | Newest first. Limit 1..50 (default 20), skip >= 0. |
| `GET /{userId}/unread-count`          | `{ count }`                                        |
| `POST /{userId}/{id}/read`            | 204 (also when already read), 404 when not theirs. |
| `POST /{userId}/read-all`             | `{ count }` = how many were unread, now read.      |

MCP tools (prefix `notification`): `list`, `unreadCount`, `markRead`, `markAllRead`. Same service methods as the routes.

## Events

| Event                                | Direction | What happens                                          |
| ------------------------------------ | --------- | ----------------------------------------------------- |
| `penalty.charged` / `penalty.failed` | in        | An inbox row and an email, once per event.            |
| `notification.email_requested`       | own       | The send: render, then write to the fake SMTP folder. |

The penalty payloads are read with notification's own records (`PenaltyChargedEvent`, `PenaltyFailedEvent`), matching penalty's contract: `userId`, `habitId`, `habitExecutionId`, `charityName`, `amountCents`, `currency`, `chargedAtUtc` / `attemptedAtUtc`.

## Data

Schema `notification`, one table `InboxNotifications`. Posting the same `dedupeKey` to the same user twice records it once (unique partial index on `(UserId, DedupeKey)`; the insert uses `ON CONFLICT DO NOTHING`, so a duplicate never aborts the caller's transaction). Purge deletes rows where the user is the recipient **or the actor**.

## Email

Templates are small HTML bodies in `src/domain/EmailTemplates.cs` (LazyTax subjects; `{{Var}}` is replaced by the HTML-escaped value). A missing variable fails the render: the email is skipped and logged, so no `{{` ever reaches a user. The only SMTP adapter is a fake: one JSON file per message in `Contexts:notification:MailDir` (default: `lazytax-mail` under the OS temp folder), named by the outbox event id, so a redelivery overwrites its own file.

Settings (all optional): `Contexts:notification:MailDir`, `BaseUrl` (default `https://lazytax.club`), `SupportEmail` (default `support@lazytax.club`).

## Changed from LazyTax

- **Email goes through the outbox.** LazyTax's `IEmailNotifier` is awaited by the caller after its money write. Here `Email(...)` writes an outbox event in the caller's unit of work and the send happens after commit (nothing leaves inside a transaction; a rolled-back unit sends nothing). Delivery is at least once; the fake transport is idempotent by event id.
- **One generic `Email(userId, template, data)`** replaces the nine `Notify*` methods. The caller passes the template's variables as strings. `UserName`, `BaseUrl` and `SupportEmail` are always filled in by notification; the recipient's name and address come from `account.v1`.
- **No charity or habit lookups.** `EmailNotifier` read the charity and habit repositories for names. notification needs only account, so a penalty email uses the event's `charityName` when penalty sends one, else LazyTax's fallbacks "your chosen charity" / "your habit".
- **Penalty inbox rows are new.** LazyTax only emailed penalties; the `penalty_charged` and `penalty_failed` inbox kinds are added (the lane brief asks for email + inbox).
- **Welcome email is not wired yet.** LazyTax passed the address at sign-up; account's contract has no user-created event yet. The `welcome` template exists and can be requested with `Email(...)`.
- **Errors** are the kernel's problem-details JSON (`/problems/v1/...`) instead of LazyTax's `App.Error.V1` bodies; the status codes are the same.
- **Bad ids.** A non-guid `{id}` is a 404 (LazyTax's route constraint also gave a 404, from routing).
- **The actor foreign key is gone** (no cross-context FK). Purge deletes by `ActorUserId` instead of a cascade.
- **Email templates** are plain HTML strings, not the React Email build.
- **Every read takes an `Owner`.** Inbox reads and writes filter by owner in SQL (auth layer 4); another user's id is a 404.
