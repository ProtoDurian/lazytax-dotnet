using Ctx.Protection.Contract.V1;
using Ctx.Protection.Data;
using Ctx.Protection.Domain;
using Ctx.Protection.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Protection;

/// <summary>The one registration of the protection context.</summary>
public sealed class ProtectionContext : IContext
{
  public void Register(ContextBuilder c)
  {
    c.Services.AddScoped<IProtectionRepository, ProtectionRepository>();
    c.Services.AddScoped<ProtectionService>();

    c.Provide<IProtectionV1, ProtectionV1>("protection.v1");
    c.Db<ProtectionDb>();
    c.AddControllers();
    c.Tool(ProtectionTools.Balance);
    c.Tool(ProtectionTools.AwardWeekly);
    c.Worker<WeeklyAwardWorker>();
    c.Subscribe<CheckSettled, CheckSettledHandler>("check.settled");
    c.PurgeUser<ProtectionPurge>();
  }
}
