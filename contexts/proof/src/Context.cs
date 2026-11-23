using Ctx.Habit.Contract.V1;
using Ctx.Proof.Contract.V1;
using Ctx.Proof.Data;
using Ctx.Proof.Domain;
using Ctx.Proof.Gateways;
using Ctx.Proof.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ctx.Proof;

/// <summary>The one registration of the proof context.</summary>
public sealed class ProofContext : IContext
{
  public void Register(ContextBuilder c)
  {
    var options = new ProofOptions();
    c.Settings.Bind(options);
    c.Services.AddSingleton(options);
    c.Services.TryAddSingleton(TimeProvider.System);
    c.Services.AddSingleton<TokenBox>();

    // Plugins: adding one is a subclass plus one registration here.
    c.Services.AddScoped<IProofPlugin, ManualProofPlugin>();
    c.Services.AddScoped<IProofPlugin, NfcProofPlugin>();
    c.Services.AddScoped<IProofPlugin, StravaProofPlugin>();
    c.Services.AddScoped<NfcTagLookup>();
    c.Services.AddScoped<PluginRegistry>();

    c.Services.AddScoped<IBindingRepository, BindingRepository>();
    c.Services.AddScoped<INfcTagRepository, NfcTagRepository>();
    c.Services.AddScoped<IStravaConnections, StravaConnectionRepository>();
    c.Services.AddScoped<IStravaInbox, StravaInboxRepository>();

    // Strava is a fake with no network (the real HTTP adapter is not part of this build).
    c.Services.AddSingleton<FakeStravaApi>();
    c.Services.AddSingleton<IStravaApi>(sp => sp.GetRequiredService<FakeStravaApi>());

    c.Services.AddScoped<ProofRouter>();
    c.Services.AddScoped<ProofService>();
    c.Services.AddScoped<NfcTagService>();
    c.Services.AddScoped<StravaService>();

    c.Provide<IProofV1, ProofV1>("proof.v1");
    c.Db<ProofDb>();
    c.AddControllers();
    c.Tool(ProofTools.Plugins);
    c.Tool(ProofTools.Binding);
    c.Tool(ProofTools.Bind);
    c.Tool(ProofTools.Unbind);
    c.Tool(ProofTools.NfcLink);
    c.Tool(ProofTools.NfcResolve);
    c.Tool(ProofTools.NfcTap);
    c.Tool(ProofTools.StravaStatus);
    c.Tool(ProofTools.StravaDisconnect);
    c.Worker<StravaInboxWorker>();
    c.Subscribe<HabitDeleted, HabitDeletedHandler>(HabitEvents.Deleted);
    c.BeforePurge<StravaBeforePurge>();
    c.PurgeUser<ProofPurge>();
  }
}
