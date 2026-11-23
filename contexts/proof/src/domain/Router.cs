using System.Text.Json;
using Ctx.Check.Contract.V1;
using Ctx.Proof.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Proof.Domain;

/// <summary>
/// The one way in for every proof source: evaluates the event for each habit it can prove and reports each
/// verdict to check as evidence. Runs inside the caller's unit of work.
/// </summary>
public sealed class ProofRouter(PluginRegistry registry, IBindingRepository bindings, ICheckV1 checks)
{
  private static readonly JsonSerializerOptions DataJson = new(JsonSerializerDefaults.Web);

  public async Task<IReadOnlyList<ProofOutcome>> Route(ProofEvent e, CancellationToken ct)
  {
    var plugin = registry.Get(e.PluginKey) ?? throw new NotFound("Proof plugin", e.PluginKey);
    var owner = Owner.User(e.UserId);

    List<(Guid HabitId, BoundProof? Binding)> targets;
    if (e.HabitId is { } target)
    {
      if (plugin.Manifest.AlwaysBound) targets = [(target, null)]; // check refuses a habit that is not the user's
      else
      {
        // Owner-scoped first read: another user's habit has no binding for this user.
        var binding = await bindings.Get(owner, target, ct);
        if (binding is null || binding.PluginKey != e.PluginKey) return [];
        targets = [(target, binding)];
      }
    }
    else
    {
      targets = [.. (await bindings.ListForUser(owner, e.PluginKey, ct)).Select(b => (b.HabitId, (BoundProof?)b))];
    }

    var outcomes = new List<ProofOutcome>();
    foreach (var (habitId, binding) in targets)
    {
      try
      {
        outcomes.Add(await this.RouteOne(plugin, e, habitId, binding, ct));
      }
      catch (Problem p) when (p.Status == 404 && e.HabitId is null)
      {
        // An untargeted event (a Strava activity): a habit check refuses (switched off) must not stop the others.
        outcomes.Add(new ProofOutcome(habitId, null));
      }
    }

    return outcomes;
  }

  private async Task<ProofOutcome> RouteOne(IProofPlugin plugin, ProofEvent e, Guid habitId, BoundProof? binding, CancellationToken ct)
  {
    var pending = await checks.PendingCheckAt(e.UserId, habitId, e.OccurredAtUtc, ct);
    // That moment's day is settled, unscheduled or over: a replay or a late event.
    if (pending is null) return new ProofOutcome(habitId, null);

    var verdict = await plugin.Evaluate(binding, e, ProofWindow.Of(pending), ct);
    if (verdict is null) return new ProofOutcome(habitId, null);

    var execution = await checks.Report(e.UserId, habitId, new Evidence(
      e.PluginKey,
      e.ExternalId,
      verdict.Verdict,
      e.OccurredAtUtc,
      verdict.Data is null ? null : JsonSerializer.Serialize(verdict.Data, DataJson),
      e.Notes), ct);
    return new ProofOutcome(habitId, execution?.Id);
  }
}
