using Ctx.Disbursement.Contract.V1;
using Ctx.Penalty.Contract.V1;
using LazyTax.Platform.Data;
using Microsoft.Extensions.Logging;

namespace Ctx.Disbursement.Domain;

/// <summary>
/// The payout pass (LazyTax <c>DisbursementService</c>). Three steps, never mixed in one transaction:
/// claim (one unit of work, with penalty's row locks), donate (HTTP, no transaction), mark (one unit of work).
/// </summary>
public sealed class DisbursementService(
  IUnitOfWork uow,
  IPenaltyV1 penalty,
  IPayoutTargets targets,
  IDisbursementRepository repo,
  IDonationGateway gateway,
  ILogger<DisbursementService> logger)
{
  /// <summary>A Pending payout still in flight after this long is assumed to belong to a crashed pass.</summary>
  public static readonly TimeSpan ReconcileLease = TimeSpan.FromMinutes(10);

  private sealed record Claimed(Guid Id, string OrganizationId, long AmountCents, string Currency);

  public async Task<int> ProcessPending(DonorIdentity donor, long minPayoutCents, int maxGroups, CancellationToken ct)
  {
    // 1) Settle (or release) payouts a previous pass created at the vendor but crashed before recording.
    try
    {
      await this.ReconcileInflight(maxGroups, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogError(ex, "Disbursement reconcile pass failed; continuing to claim");
    }

    // 2) Claim, then donate each group outside any transaction.
    var claimed = await this.Claim(minPayoutCents, maxGroups, ct);
    var completed = 0;
    foreach (var c in claimed)
    {
      try
      {
        completed += await this.DonateOne(c, donor, ct);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // An unexpected throw is AMBIGUOUS (it can happen after the donation was accepted): never release here.
        logger.LogError(ex, "Donation threw for payout {Id}; leaving Pending for reconciliation", c.Id);
      }
    }
    return completed;
  }

  // ONE unit of work: penalty locks the charged, unclaimed rows (FOR UPDATE SKIP LOCKED) until it ends, so two
  // passes never claim the same penalty. The id is generated up front: it is both the stamp on the penalties and
  // the reference sent to the vendor. There is no foreign key between the two contexts, so order does not matter.
  private Task<IReadOnlyList<Claimed>> Claim(long minPayoutCents, int maxGroups, CancellationToken ct) =>
    uow.Start<IReadOnlyList<Claimed>>(async () =>
    {
      var claimed = new List<Claimed>();
      var groups = await penalty.PayoutCandidates(minPayoutCents, ct);
      foreach (var g in groups)
      {
        if (claimed.Count >= maxGroups) break;
        var org = await targets.PledgeOrganizationId(g.CharityId, ct);
        if (string.IsNullOrWhiteSpace(org))
        {
          // No Pledge org id: cannot donate. Leave the penalties unclaimed so they settle once the charity has one.
          logger.LogWarning("Skipping payout for charity {CharityId} ({Currency}): no Pledge org id", g.CharityId, g.Currency);
          continue;
        }

        var id = Guid.NewGuid();
        var stamped = await penalty.ClaimForPayout(id, g.CharityId, g.Currency, ct);
        if (stamped <= 0) continue;

        var now = DateTime.UtcNow;
        await repo.Insert(new DisbursementInfo(id, g.CharityId, g.Currency, stamped, DisbursementStatus.Pending, org,
          null, 0, null, now, now), ct);
        claimed.Add(new Claimed(id, org, stamped, g.Currency));
      }
      return claimed;
    }, ct);

  private async Task<int> DonateOne(Claimed c, DonorIdentity donor, CancellationToken ct)
  {
    try
    {
      var d = await gateway.CreateDonation(new DonationRequest(c.OrganizationId, c.AmountCents, c.Currency,
        c.Id.ToString(), donor.FirstName, donor.LastName, donor.Email), ct);
      await this.Complete(c.Id, d.DonationId, ct);
      return 1;
    }
    catch (DonationRejectedException ex)
    {
      // DEFINITIVE (4xx): the vendor created nothing, so releasing the penalties for a fresh retry is safe.
      await this.Fail(c.Id, ex.Message, ct);
      return 0;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // AMBIGUOUS (5xx, timeout, network): the donation MAY have landed. Pledge has no idempotency key, so releasing
      // would risk a double payout. Stay Pending; reconcile looks the donation up by our reference.
      logger.LogWarning(ex, "Donation for payout {Id} failed ambiguously; leaving Pending for reconciliation", c.Id);
      return 0;
    }
  }

  // For each Pending payout past the lease, ask the vendor whether the donation landed. Landed: mark Completed
  // WITHOUT re-donating. Conclusively absent: release the penalties. Inconclusive: leave Pending.
  private async Task ReconcileInflight(int batchSize, CancellationToken ct)
  {
    var stale = await repo.GetReconcilable(DateTime.UtcNow - ReconcileLease, batchSize, ct);
    foreach (var d in stale)
    {
      try
      {
        var found = await gateway.FindDonationByReference(d.Id.ToString(), ct);
        if (found is not null) await this.Complete(d.Id, found.DonationId, ct);
        else await this.Fail(d.Id, "reconcile: no donation found at provider; released for retry", ct);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        logger.LogWarning(ex, "Reconcile lookup for payout {Id} failed; leaving Pending", d.Id);
      }
    }
  }

  // Idempotent: a repeat (a reconcile racing the original) finds it Completed and does nothing.
  private Task Complete(Guid id, string donationId, CancellationToken ct) =>
    uow.Start(async () => { await repo.MarkCompleted(id, donationId, ct); }, ct);

  // One unit of work: the payout row and penalty's release commit together. A Completed payout is never undone.
  private Task Fail(Guid id, string error, CancellationToken ct) =>
    uow.Start(async () =>
    {
      if (await repo.MarkFailed(id, error, ct)) await penalty.ReleasePayout(id, ct);
    }, ct);
}
