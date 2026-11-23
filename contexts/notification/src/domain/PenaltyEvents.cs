using System.Globalization;
using Ctx.Notification.Contract.V1;
using LazyTax.Platform.Outbox;

namespace Ctx.Notification.Domain;

// notification does not need penalty's contract to read its events: these are the fields it uses, from
// penalty.charged / penalty.failed (camelCase JSON). Unknown fields are ignored.
public sealed record PenaltyChargedEvent(string UserId, string? HabitId, string? HabitExecutionId, long AmountCents, string Currency, string? ChargedAtUtc, string? CharityName);

public sealed record PenaltyFailedEvent(string UserId, string? HabitId, string? HabitExecutionId, long AmountCents, string Currency, string? AttemptedAtUtc, string? CharityName);

/// <summary>
/// Reacts to penalty's events with an inbox row and an email, in the subscriber's own unit of work, so each
/// happens once per event. notification only needs account, so the emails use LazyTax's fallback names
/// ("your habit", "your chosen charity").
/// </summary>
public sealed class PenaltyNotifier(INotificationV1 notification)
{
  private const string Habit = "your habit";
  private const string Charity = "your chosen charity";

  public async Task Charged(OutboxEvent meta, PenaltyChargedEvent e, CancellationToken ct)
  {
    var amount = FormatMoney(e.AmountCents, e.Currency);
    await notification.Post(e.UserId, InboxKinds.PenaltyCharged, "Habit stake charged",
      $"You missed {Habit}: {amount} was charged.", null, TapData(e.HabitId, e.HabitExecutionId),
      $"penalty.charged:{meta.Id}", ct);
    await notification.Email(e.UserId, EmailTemplateIds.PenaltyCharged, new Dictionary<string, string>
    {
      ["Amount"] = amount,
      ["CharityName"] = string.IsNullOrWhiteSpace(e.CharityName) ? Charity : e.CharityName,
      ["HabitName"] = Habit,
      ["ChargeDate"] = FormatDate(e.ChargedAtUtc, meta.CreatedAtUtc),
    }, ct);
  }

  public async Task Failed(OutboxEvent meta, PenaltyFailedEvent e, CancellationToken ct)
  {
    var amount = FormatMoney(e.AmountCents, e.Currency);
    await notification.Post(e.UserId, InboxKinds.PenaltyFailed, "We couldn't process your stake",
      $"You missed {Habit}, but {amount} could not be charged.", null, TapData(e.HabitId, e.HabitExecutionId),
      $"penalty.failed:{meta.Id}", ct);
    await notification.Email(e.UserId, EmailTemplateIds.PenaltyPaymentFailed, new Dictionary<string, string>
    {
      ["Amount"] = amount,
      ["CharityName"] = string.IsNullOrWhiteSpace(e.CharityName) ? Charity : e.CharityName,
      ["HabitName"] = Habit,
      ["AttemptDate"] = FormatDate(e.AttemptedAtUtc, meta.CreatedAtUtc),
    }, ct);
  }

  /// <summary>"$5.00" for USD, else "5.00 EUR" (LazyTax <c>FormatMoney</c>).</summary>
  public static string FormatMoney(long cents, string currency)
  {
    var amount = (cents / 100m).ToString("F2", CultureInfo.InvariantCulture);
    return currency == "USD" ? $"${amount}" : $"{amount} {currency}";
  }

  /// <summary>"12 July 2026"; the fallback when the payload has no readable instant.</summary>
  public static string FormatDate(string? iso, DateTime fallbackUtc)
  {
    var d = DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
      ? parsed
      : fallbackUtc;
    return d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
  }

  private static string? TapData(string? habitId, string? executionId) =>
    habitId is null && executionId is null
      ? null
      : System.Text.Json.JsonSerializer.Serialize(new { habitId, habitExecutionId = executionId });
}
