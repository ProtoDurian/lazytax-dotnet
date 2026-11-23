using Ctx.Proof.Api.V1;
using Ctx.Proof.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using LazyTax.Platform.Problems;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Proof.Mcp;

/// <summary>MCP tools: the same service methods as the routes, with the same subject rule.</summary>
public static class ProofTools
{
  public sealed record NoArgs;

  public sealed record HabitArgs(string UserId, string HabitId);

  public sealed record BindArgs(string UserId, string HabitId, string PluginKey, string Settings);

  public sealed record UserArgs(string UserId);

  public sealed record TagArgs(string UserId, string TagId);

  public sealed record LinkArgs(string UserId, string TagId, string HabitId);

  private static Guid HabitIdOf(string s) => Guid.TryParse(s, out var g) ? g : throw new NotFound("Habit", s);

  public static readonly McpToolDefinition Plugins = McpTool.Define<NoArgs>(
    "proof.plugins", "List the proof plugins this server offers", Policies.Registered,
    call => Task.FromResult<object?>(call.Services.GetRequiredService<ProofService>().ListPlugins().Select(m => m.ToRes()).ToList()));

  public static readonly McpToolDefinition Binding = McpTool.Define<HabitArgs>(
    "proof.binding", "Get a habit's proof binding (manual when none)", Policies.Registered,
    async call => (await call.Services.GetRequiredService<ProofService>().GetBinding(Owner.User(call.Args.UserId), HabitIdOf(call.Args.HabitId), call.Ct))?.ToRes()
                  ?? new ProofBindingRes(HabitIdOf(call.Args.HabitId), Contract.V1.ProofPlugins.Manual, 1, "{}"),
    subject: "userId");

  public static readonly McpToolDefinition Bind = McpTool.Define<BindArgs>(
    "proof.bind", "Bind a habit to a proof plugin", Policies.Registered,
    async call => (await call.Services.GetRequiredService<ProofService>()
      .Bind(call.Args.UserId, HabitIdOf(call.Args.HabitId), call.Args.PluginKey, call.Args.Settings, call.Ct)).ToRes(),
    subject: "userId");

  public static readonly McpToolDefinition Unbind = McpTool.Define<HabitArgs>(
    "proof.unbind", "Back to manual proof only", Policies.Registered,
    async call => await call.Services.GetRequiredService<ProofService>().Unbind(call.Args.UserId, HabitIdOf(call.Args.HabitId), call.Ct)
      ? new { unbound = true }
      : throw new NotFound("Proof Binding", call.Args.HabitId),
    subject: "userId");

  public static readonly McpToolDefinition NfcLink = McpTool.Define<LinkArgs>(
    "proof.nfcLink", "Link an NFC tag to a habit", Policies.Registered,
    async call => (await call.Services.GetRequiredService<NfcTagService>()
      .Link(Owner.User(call.Args.UserId), call.Args.TagId, HabitIdOf(call.Args.HabitId), call.Ct)).ToRes(),
    subject: "userId");

  public static readonly McpToolDefinition NfcResolve = McpTool.Define<TagArgs>(
    "proof.nfcResolve", "Resolve an NFC tag to its habit and today's execution", Policies.Registered,
    async call => (await call.Services.GetRequiredService<NfcTagService>().Resolve(Owner.User(call.Args.UserId), call.Args.TagId, call.Ct))?.ToRes()
                  ?? throw new NotFound("NFC Tag", call.Args.TagId),
    subject: "userId");

  public static readonly McpToolDefinition NfcTap = McpTool.Define<TagArgs>(
    "proof.nfcTap", "A tap on a linked NFC tag proves its habit", Policies.Registered,
    async call => (await call.Services.GetRequiredService<NfcTagService>().Tap(Owner.User(call.Args.UserId), call.Args.TagId, call.Ct))
      .Select(o => new TapRes(o.HabitId, o.ExecutionId)).ToList(),
    subject: "userId");

  public static readonly McpToolDefinition StravaStatus = McpTool.Define<UserArgs>(
    "proof.stravaStatus", "Whether the user's Strava is connected", Policies.Registered,
    async call =>
    {
      if (!call.Services.GetRequiredService<ProofOptions>().Strava.Enabled) throw new NotFound("Strava", "disabled");
      var c = await call.Services.GetRequiredService<StravaService>().Get(Owner.User(call.Args.UserId), call.Ct);
      return new StravaConnectionRes(c is not null, c?.ConnectedAtUtc);
    },
    subject: "userId");

  public static readonly McpToolDefinition StravaDisconnect = McpTool.Define<UserArgs>(
    "proof.stravaDisconnect", "Disconnect Strava and delete the user's Strava data", Policies.Registered,
    async call =>
    {
      if (!call.Services.GetRequiredService<ProofOptions>().Strava.Enabled) throw new NotFound("Strava", "disabled");
      return await call.Services.GetRequiredService<StravaService>().Disconnect(Owner.User(call.Args.UserId), call.Ct)
        ? new { disconnected = true }
        : throw new NotFound("Strava Connection", call.Args.UserId);
    },
    subject: "userId");
}
