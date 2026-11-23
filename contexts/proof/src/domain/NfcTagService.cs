using Ctx.Account.Contract.V1;
using Ctx.Check.Contract.V1;
using Ctx.Habit.Contract.V1;
using Ctx.Proof.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Proof.Domain;

public sealed record NfcResolution(NfcTag Tag, HabitVersionView HabitVersion, DateOnly Today, HabitExecution? TodayExecution);

/// <summary>NFC tags (LazyTax NfcTagService).</summary>
public sealed class NfcTagService(
  INfcTagRepository repo,
  IHabitV1 habits,
  IAccountV1 account,
  ICheckV1 checks,
  ProofRouter router,
  IUnitOfWork uow,
  TimeProvider clock)
{
  /// <summary>Create-or-replace the tag to habit mapping; 409 when another user owns the tag (first come, first served).</summary>
  public async Task<NfcTag> Link(Owner owner, string tagId, Guid habitId, CancellationToken ct)
  {
    var userId = UserOf(owner);
    if (await habits.GetCurrent(owner, habitId, ct) is null) throw new NotFound("Habit", habitId.ToString());
    return await repo.Claim(userId, tagId, habitId, ct)
      ?? throw new Conflict("NFC tag is already linked by another user");
  }

  /// <summary>Null (404) when the tag is unclaimed or owned by another user: ownership is never leaked.</summary>
  public async Task<NfcResolution?> Resolve(Owner owner, string tagId, CancellationToken ct)
  {
    var userId = UserOf(owner);
    var tag = await repo.Get(owner, tagId, ct);
    if (tag is null) return null;
    var version = await habits.GetCurrent(owner, tag.HabitId, ct);
    if (version is null) return null;

    // "Today" is the user's day (their timezone follows the phone), not the habit's.
    var today = (await account.Today(userId, null, ct)).Today;
    return new NfcResolution(tag, version, today, await checks.ExecutionForDate(owner, tag.HabitId, today, ct));
  }

  public Task<bool> Unlink(Owner owner, string tagId, CancellationToken ct) => repo.Delete(owner, tagId, ct);

  /// <summary>A tap on a linked tag: proof of the tag's habit through the router. 404 for a tag that is not the owner's.</summary>
  public async Task<IReadOnlyList<ProofOutcome>> Tap(Owner owner, string tagId, CancellationToken ct)
  {
    var userId = UserOf(owner);
    var tag = await repo.Get(owner, tagId, ct) ?? throw new NotFound("NFC Tag", tagId);
    var at = clock.GetUtcNow().UtcDateTime;
    return await uow.Start(() => router.Route(new ProofEvent(
      ProofPlugins.Nfc, userId, $"{tag.Id}:{at:O}", at, tag.HabitId, $"{{\"tagId\":\"{tag.Id}\"}}"), ct), ct);
  }


  private static string UserOf(Owner owner) =>
    owner.IsAnyone ? throw new InvalidOperationException("NFC tags need Owner.User") : owner.UserId!;
}
