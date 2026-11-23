using Ctx.Habit.Contract.V1;
using Ctx.Plan.Contract.V1;
using Ctx.Proof.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Proof.Domain;

/// <summary>Binding a habit to a proof plugin (LazyTax ProofService).</summary>
public sealed class ProofService(
  PluginRegistry registry,
  IBindingRepository repo,
  IHabitV1 habits,
  IPlanV1 plan,
  IUnitOfWork uow)
{
  public IReadOnlyList<PluginManifest> ListPlugins() => registry.List();

  public async Task<BoundProof?> GetBinding(Owner owner, Guid habitId, CancellationToken ct)
  {
    await this.EnsureOwned(owner, habitId, ct);
    return await repo.Get(owner, habitId, ct);
  }

  public async Task<IReadOnlyDictionary<Guid, string>> ListPluginKeys(Owner owner, CancellationToken ct) =>
    await repo.ListPluginKeys(owner, ct);

  /// <summary>Validates the settings, checks the user's tier, then binds, replacing the habit's binding. One binding per habit, under the user lock.</summary>
  public async Task<BoundProof> Bind(string userId, Guid habitId, string pluginKey, string settings, CancellationToken ct)
  {
    var owner = Owner.User(userId);
    await this.EnsureOwned(owner, habitId, ct);

    var plugin = registry.Get(pluginKey) ?? throw new NotFound("Proof plugin", pluginKey);
    if (plugin.Manifest.AlwaysBound)
      throw new ValidationFailed("Invalid proof binding", "pluginKey", $"'{pluginKey}' is always available and can't be bound");
    var normalized = plugin.ValidateSettings(settings);
    if (plugin.Manifest.MinTier is { } min) await plan.EnsureTier(userId, min, $"proof.{pluginKey}", ct);

    return await uow.Start(async () =>
    {
      await uow.LockUser(userId, ct);
      var existing = await repo.Get(owner, habitId, ct);
      if (existing is not null) await this.UnbindPlugin(existing, ct);

      var pending = new BoundProof(existing?.Id ?? Guid.Empty, habitId, userId, pluginKey, plugin.Manifest.Version, normalized);
      await plugin.OnBind(pending, ct);
      return await repo.Upsert(userId, habitId, pluginKey, plugin.Manifest.Version, normalized, ct);
    }, ct);
  }

  /// <summary>False when the habit has no binding.</summary>
  public async Task<bool> Unbind(string userId, Guid habitId, CancellationToken ct)
  {
    var owner = Owner.User(userId);
    await this.EnsureOwned(owner, habitId, ct);
    return await uow.Start(async () =>
    {
      var existing = await repo.Get(owner, habitId, ct);
      if (existing is null) return false;
      await this.UnbindPlugin(existing, ct);
      return await repo.Delete(owner, habitId, ct);
    }, ct);
  }

  // A binding whose plugin was removed from the app has nothing to clean up.
  private Task UnbindPlugin(BoundProof binding, CancellationToken ct) =>
    registry.Get(binding.PluginKey)?.OnUnbind(binding, ct) ?? Task.CompletedTask;

  private async Task EnsureOwned(Owner owner, Guid habitId, CancellationToken ct)
  {
    if (owner.IsAnyone || !await habits.Owns(owner.UserId!, habitId, ct))
      throw new NotFound("Habit", habitId.ToString());
  }
}
