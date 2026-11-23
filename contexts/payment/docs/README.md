# payment

LazyTax's product money: the user's Airwallex customer, their purpose-scoped consents (mandates) and the
merchant-initiated charges made against them. Ported from `App/Modules/Payment` and `Domain/Payment`.

## What it owns

| Table (schema `payment`) | Holds |
| --- | --- |
| `PaymentCustomers` | one Airwallex customer per user (`UserId` = JWT sub, unique) |
| `PaymentConsents` | one consent per (customer, purpose): `penalty` or `subscription` |
| `Charges` | one row per logical charge, keyed by the caller's request id, with its 3-phase state |

## Contract `payment.v1` (`contract/v1/IPaymentV1.cs`)

| Method | Use |
| --- | --- |
| `HasConsent(userId, purpose)` | is there a VERIFIED consent for the purpose |
| `GetConsent(owner, purpose)` | the owner's consent state; 404 without a customer |
| `Charge(requestId, userId, amountCents, currency, purpose, description?)` | exactly-once charge; returns `Succeeded`, `Pending`, `NoConsent` or `Failed` |
| `GetCharge(requestId)` | last known state of a charge |
| `RevokeAll(userId)` | revoke every consent at the gateway, then locally |

`Charge` and `RevokeAll` call the gateway, so they refuse to run inside a unit of work.

**The 3 phases of `Charge`** (nitroso `Withdrawal.Approve`):

1. **Claim**, in one transaction: insert or lock the `Charges` row; a succeeded row returns at once; no verified
   consent gives `NoConsent`; else take the next attempt (or re-drive the same attempt after an ambiguous first
   send) and hold a 2-minute in-flight lease.
2. **Gateway**, outside any transaction. With a stored intent: retrieve it, confirm only if not settled. Without
   one: create it with key `{requestId}-{attempt}`, store the intent id, then confirm.
3. **Write back**, in a second transaction, only if this attempt still holds the claim. Emits
   `payment.completed` (succeeded) or `payment.failed` (failed, ambiguous, no consent) through the outbox.

## Events

| Event | Payload | When |
| --- | --- | --- |
| `payment.completed` | `PaymentSettled` | a charge succeeded (from `Charge` or the `payment_intent.succeeded` webhook) |
| `payment.failed` | `PaymentSettled` | an attempt ended without money moving |

## Routes (`/api/v1/Payment`, same paths as LazyTax)

| Route | Policy | Notes |
| --- | --- | --- |
| `PUT {userId}/customers` | registered, subject | DB, then gateway lookup, then gateway create |
| `GET {userId}/client-secret` | registered, subject | 404 without a customer |
| `GET {userId}/consent?purpose=` | registered, subject | `penalty` (default) or `subscription` |
| `DELETE {userId}/consent?purpose=` | registered, subject | gateway revoke, local delete, email, Logto claim |
| `POST {userId}/intent` | registered, subject | interactive intent |
| `POST {userId}/intent/{intentId}` | registered, subject | 404 for another customer's intent |
| `POST webhook` | public | HMAC over the raw body, constant-time, fresh timestamp |

MCP tools: `payment.consent`, `payment.disable_consent`.

Hooks: `BeforePurge` revokes every consent (best effort, outside the transaction); `PurgeUser` deletes the
user's rows in all three tables.

## Fakes (no network)

- `FakeAirwallex` (`src/data/Fakes.cs`) is the gateway: in memory, the same request id gives the same intent,
  confirm settles it. Tests script failures with `FailNext(op, definitive)` and `NextConfirmStatus(status)`.
- `FakeIdentityClaims` stands in for Logto management (`has_payment_consent`).
- The consent email goes through `notification.v1` `Email`, which turns it into an outbox event.

## Changed from LazyTax

1. **New `Charges` table and `Charge` contract.** LazyTax's `ChargeStoredConsentAsync` took callbacks
   (`existingIntentId`, `onIntentCreated`) so the penalty row stored the intent id. Now payment stores the
   intent per request id itself; callers pass only their id. Idempotency key is `{requestId}-{attempt}`
   (LazyTax used the penalty id).
2. **No-consent is a result, not an exception:** `ChargeStatus.NoConsent` replaces the typed
   `NotFoundException(PaymentCustomer)` penalty matched on.
3. **Webhook signature:** compared in constant time (LazyTax used `==`), computed over the raw body (LazyTax
   re-serialized the parsed body first) and rejected when `x-timestamp` is more than 5 minutes off (LazyTax had
   no freshness check).
4. **`payment_intent.succeeded` is handled:** it settles a pending charge and emits `payment.completed`
   (LazyTax had this branch commented out).
5. **Consent row and email commit together:** the email request is an outbox event in the same unit of work as
   the consent change. The Logto claim is set after commit, best effort (LazyTax failed the webhook when the
   claim call failed).
6. **Confirming another customer's intent is a 404** before any gateway confirm (LazyTax relied on Airwallex
   rejecting the customer mismatch).
7. **No `account.v1` in `needs`:** lanes.md lists account, but payment never calls it. The JWT sub is the
   customer key and there is no foreign key to `Users`.
8. **Admin customer search** (`SearchCustomers`, no route in LazyTax) is not ported.
9. **Problem codes:** gateway failures on routes are `502 payment_gateway_error`; a bad signature is `401`.
