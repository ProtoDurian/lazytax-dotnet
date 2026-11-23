using Ctx.Proof.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Proof.Api.V1;

/// <summary>LazyTax's ProofController, at the same paths (/api/v1/Proof/...).</summary>
[Route("Proof")]
public sealed class ProofController(ProofService service) : ContextController
{
  // The proof plugins this server offers; the app renders a binding form from SettingsSchema.
  [Policy(Policies.Registered), HttpGet("plugins")]
  public List<PluginManifestRes> ListPlugins() => [.. service.ListPlugins().Select(m => m.ToRes())];

  [Policy(Policies.Registered), Subject("userId"), HttpGet("{userId}/{habitId:guid}")]
  public async Task<ProofBindingRes> Get(string userId, Guid habitId, CancellationToken ct) =>
    (await service.GetBinding(Owner.User(userId), habitId, ct))?.ToRes()
    ?? new ProofBindingRes(habitId, Contract.V1.ProofPlugins.Manual, 1, "{}");

  [Policy(Policies.Registered), Subject("userId"), HttpPut("{userId}/{habitId:guid}")]
  public async Task<ProofBindingRes> Bind(string userId, Guid habitId, [FromBody] BindProofReq req, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.PluginKey) || req.PluginKey.Length > 64)
      throw new ValidationFailed("Invalid BindProofReq", "PluginKey", "PluginKey must be 1-64 characters");
    if (string.IsNullOrWhiteSpace(req.Settings) || req.Settings.Length > 4096)
      throw new ValidationFailed("Invalid BindProofReq", "Settings", "Settings must be 1-4096 characters");
    return (await service.Bind(userId, habitId, req.PluginKey, req.Settings, ct)).ToRes();
  }

  // Back to manual only.
  [Policy(Policies.Registered), Subject("userId"), HttpDelete("{userId}/{habitId:guid}")]
  public async Task<IActionResult> Unbind(string userId, Guid habitId, CancellationToken ct) =>
    await service.Unbind(userId, habitId, ct) ? this.NoContent() : throw new NotFound("Proof Binding", habitId.ToString());
}
