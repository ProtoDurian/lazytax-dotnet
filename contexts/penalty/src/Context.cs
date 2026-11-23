using Ctx.Penalty.Adapters;
using Ctx.Penalty.Contract.V1;
using Ctx.Penalty.Data;
using Ctx.Penalty.Domain;
using Ctx.Penalty.Hooks;
using Ctx.Penalty.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ctx.Penalty;

/// <summary>The one registration of the penalty context.</summary>
public sealed class PenaltyContext : IContext
{
  public void Register(ContextBuilder c)
  {
    var options = c.Settings.Get<PenaltyOptions>() ?? new PenaltyOptions();
    c.Services.AddSingleton(options);
    c.Services.TryAddSingleton(TimeProvider.System);
    c.Services.AddScoped<IPenaltyStore, PenaltyStore>();
    c.Services.AddScoped<IChargeGateway, PaymentChargeGateway>();
    c.Services.AddScoped<ICharityNames, CharityNames>();
    c.Services.AddScoped<PenaltyService>();

    c.Provide<IPenaltyV1, PenaltyV1>("penalty.v1");
    c.Db<PenaltyDb>();
    c.AddControllers();
    c.Tool(PenaltyTools.Search);
    c.Tool(PenaltyTools.Get);
    c.Tool(PenaltyTools.Outstanding);
    c.Tool(PenaltyTools.CharityBalances);
    c.Worker<PenaltyProcessorWorker>();
    c.Subscribe<SettledCheck, CheckSettledHandler>("check.settled");
    c.Subscribe<PaymentCompleted, PaymentCompletedHandler>("payment.completed");
    c.DeleteGuard<NoDebtGuard>();
    c.PurgeUser<PenaltyPurge>();
  }
}
