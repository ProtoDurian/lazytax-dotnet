using Ctx.Proof.Contract.V1;
using Ctx.Proof.Domain;
using LazyTax.Platform.Data;

namespace Ctx.Proof;

/// <summary>The proof.v1 contract: thin over the router, the binding service and the repositories.</summary>
internal sealed class ProofV1(ProofRouter router, ProofService proofs, INfcTagRepository tags) : IProofV1
{
  public Task<IReadOnlyList<ProofOutcome>> Route(ProofEvent e, CancellationToken ct = default) => router.Route(e, ct);

  public IReadOnlyList<PluginManifest> ListPlugins() => proofs.ListPlugins();

  public async Task<ProofBindingInfo?> GetBinding(Owner owner, Guid habitId, CancellationToken ct = default) =>
    await proofs.GetBinding(owner, habitId, ct) is { } b ? new ProofBindingInfo(b.HabitId, b.PluginKey, b.PluginVersion, b.Settings) : null;

  public Task<IReadOnlyDictionary<Guid, string>> ListPluginKeys(Owner owner, CancellationToken ct = default) => proofs.ListPluginKeys(owner, ct);

  public Task<IReadOnlyList<Guid>> ListTaggedHabits(Owner owner, CancellationToken ct = default) => tags.ListTaggedHabits(owner, ct);
}
