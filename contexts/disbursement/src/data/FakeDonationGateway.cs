using System.Collections.Concurrent;
using Ctx.Disbursement.Domain;

namespace Ctx.Disbursement.Data;

/// <summary>How the fake vendor answers the next donation to one organisation.</summary>
public enum FakeOutcome
{
  /// <summary>The donation is created.</summary>
  Succeed,

  /// <summary>A 4xx: refused, nothing created (<see cref="DonationRejectedException"/>).</summary>
  Reject,

  /// <summary>A timeout AFTER the vendor created the donation (ambiguous, but it landed).</summary>
  TimeoutAfterCreate,

  /// <summary>A timeout BEFORE the vendor created anything (ambiguous, it never landed).</summary>
  TimeoutBeforeCreate,
}

/// <summary>
/// The Pledge gateway. There is no network: LazyTax's <c>PledgeDonationGateway</c> is replaced by this in-memory
/// vendor, for dev and for tests. Script it per organisation id with <see cref="Script"/>; the default is success.
/// Stateful singleton, so tests use their own organisation ids.
/// </summary>
public sealed class FakeDonationGateway : IDonationGateway
{
  private readonly ConcurrentDictionary<string, FakeOutcome> script = new();
  private readonly ConcurrentDictionary<string, DonationResult> byReference = new();
  private readonly ConcurrentDictionary<string, byte> inconclusive = new();
  private readonly ConcurrentQueue<DonationRequest> requests = new();

  /// <summary>Every request made, in order, including ones that failed.</summary>
  public IReadOnlyCollection<DonationRequest> Requests => this.requests.ToArray();

  /// <summary>Sets the outcome of every later donation to <paramref name="organizationId"/>.</summary>
  public void Script(string organizationId, FakeOutcome outcome) => this.script[organizationId] = outcome;

  /// <summary>Makes <see cref="FindDonationByReference"/> fail for this reference (an inconclusive lookup).</summary>
  public void MakeLookupInconclusive(string reference) => this.inconclusive[reference] = 0;

  public Task<DonationResult> CreateDonation(DonationRequest request, CancellationToken ct = default)
  {
    this.requests.Enqueue(request);
    var outcome = this.script.GetValueOrDefault(request.OrganizationId, FakeOutcome.Succeed);
    switch (outcome)
    {
      case FakeOutcome.Reject:
        throw new DonationRejectedException($"fake pledge: organization {request.OrganizationId} refused the donation");
      case FakeOutcome.TimeoutBeforeCreate:
        throw new TimeoutException("fake pledge: timed out before the donation was created");
    }

    var made = this.byReference.GetOrAdd(request.Reference,
      _ => new DonationResult("don_" + Guid.NewGuid().ToString("N")[..16], "processed"));
    if (outcome == FakeOutcome.TimeoutAfterCreate) throw new TimeoutException("fake pledge: timed out after the donation was created");
    return Task.FromResult(made);
  }

  public Task<DonationResult?> FindDonationByReference(string reference, CancellationToken ct = default)
  {
    if (this.inconclusive.ContainsKey(reference)) throw new InvalidOperationException($"fake pledge: lookup for {reference} was inconclusive");
    return Task.FromResult(this.byReference.GetValueOrDefault(reference));
  }
}
