namespace Ctx.Disbursement.Data;

/// <summary>One row per payout attempt to a single (charity, currency). The Id is also the reference stored at the vendor.</summary>
public sealed class DisbursementRow
{
  public Guid Id { get; set; }
  public Guid CharityId { get; set; }
  public string Currency { get; set; } = string.Empty;
  public long AmountCents { get; set; }
  public string Status { get; set; } = string.Empty;
  public string PledgeOrganizationId { get; set; } = string.Empty;
  public string? ProviderDonationId { get; set; }
  public int Attempts { get; set; }
  public string? LastError { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
}
