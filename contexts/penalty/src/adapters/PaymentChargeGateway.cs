using Ctx.Payment.Contract.V1;
using Ctx.Penalty.Contract.V1;
using Ctx.Penalty.Domain;

namespace Ctx.Penalty.Adapters;

/// <summary>
/// payment.v1 -> the penalty charge port. payment owns the 3 phases and the idempotency key
/// <c>{requestId}-{attempt}</c>; penalty reuses its own id as the requestId, so a retry reconciles the same
/// gateway intent (LazyTax passed <c>idempotencyKey: p.Id</c> and <c>existingIntentId</c> itself).
/// </summary>
internal sealed class PaymentChargeGateway(IPaymentV1 payment) : IChargeGateway
{
  public async Task<ChargeOutcome> Charge(PenaltyInfo p, CancellationToken ct)
  {
    var r = await payment.Charge(p.Id, p.UserId, p.AmountCents, p.Currency, ConsentPurpose.Penalty,
      $"Habit penalty {p.HabitExecutionId}", ct);
    return r.Status switch
    {
      ChargeStatus.Succeeded => new ChargeOutcome.Succeeded(r.IntentId ?? p.Id.ToString()),
      ChargeStatus.Pending => new ChargeOutcome.Pending(r.IntentId, r.GatewayStatus ?? "PENDING"),
      ChargeStatus.NoConsent => new ChargeOutcome.NoConsent(),
      _ => new ChargeOutcome.Error(r.Error ?? r.GatewayStatus ?? "charge error"),
    };
  }
}
