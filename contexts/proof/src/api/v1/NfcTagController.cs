using System.Text.RegularExpressions;
using Ctx.Proof.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Proof.Api.V1;

/// <summary>LazyTax's NfcTagController (/api/v1/NfcTag/...), plus the new tap.</summary>
[Route("NfcTag")]
public sealed partial class NfcTagController(NfcTagService service) : ContextController
{
  // Tag ids are client-generated (UUID-ish) strings embedded in the tag URL.
  [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
  private static partial Regex TagIdRegex();

  [Policy(Policies.Registered), Subject("userId"), HttpPut("{userId}/{tagId}")]
  public async Task<NfcTagRes> Link(string userId, string tagId, [FromBody] LinkNfcTagReq req, CancellationToken ct)
  {
    ValidTagId(tagId);
    if (req.HabitId == Guid.Empty) throw new ValidationFailed("Invalid LinkNfcTagReq", "HabitId", "HabitId must not be empty");
    return (await service.Link(Owner.User(userId), tagId, req.HabitId, ct)).ToRes();
  }

  [Policy(Policies.Registered), Subject("userId"), HttpGet("{userId}/{tagId}")]
  public async Task<NfcTagResolutionRes> Resolve(string userId, string tagId, CancellationToken ct)
  {
    ValidTagId(tagId);
    return (await service.Resolve(Owner.User(userId), tagId, ct))?.ToRes() ?? throw new NotFound("NFC Tag", tagId);
  }

  [Policy(Policies.Registered), Subject("userId"), HttpDelete("{userId}/{tagId}")]
  public async Task<IActionResult> Unlink(string userId, string tagId, CancellationToken ct)
  {
    ValidTagId(tagId);
    return await service.Unlink(Owner.User(userId), tagId, ct) ? this.NoContent() : throw new NotFound("NFC Tag", tagId);
  }

  /// <summary>New: a tap is proof of the tag's habit, through the proof router (plugin "nfc").</summary>
  [Policy(Policies.Registered), Subject("userId"), HttpPost("{userId}/{tagId}/tap")]
  public async Task<List<TapRes>> Tap(string userId, string tagId, CancellationToken ct)
  {
    ValidTagId(tagId);
    return [.. (await service.Tap(Owner.User(userId), tagId, ct)).Select(o => new TapRes(o.HabitId, o.ExecutionId))];
  }

  private static void ValidTagId(string tagId)
  {
    if (!TagIdRegex().IsMatch(tagId))
      throw new ValidationFailed("Invalid NFC tag id", "TagId", "Tag id must be 8-64 characters of letters, digits or hyphens");
  }
}
