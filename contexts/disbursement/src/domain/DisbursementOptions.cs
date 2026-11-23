namespace Ctx.Disbursement.Domain;

/// <summary>Settings under <c>Contexts:disbursement</c> (LazyTax <c>DisbursementOption</c>).</summary>
public sealed class DisbursementOptions
{
  /// <summary>Master switch of the daily worker. Off by default; the admin route works regardless.</summary>
  public bool Enabled { get; set; }

  /// <summary>Skip a (charity, currency) group below this many cents. 0 = no threshold.</summary>
  public long MinPayoutCents { get; set; }

  /// <summary>Cap on groups paid out per pass.</summary>
  public int MaxGroupsPerRun { get; set; } = 100;

  public string DonorFirstName { get; set; } = "LazyTax";
  public string DonorLastName { get; set; } = "Donations";
  public string DonorEmail { get; set; } = "donations@lazytax.club";

  /// <summary>Hours between worker ticks (LazyTax: 24).</summary>
  public int IntervalHours { get; set; } = 24;
}
