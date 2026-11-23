using Ctx.Plan.Contract.V1;
using Ctx.Plan.Data;
using Ctx.Plan.Domain;
using Ctx.Plan.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ctx.Plan;

/// <summary>The one registration of the plan context: tier, status, period, and the entitlements.</summary>
public sealed class PlanContext : IContext
{
  public void Register(ContextBuilder c)
  {
    c.Services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection("Contexts:plan").Get<PlanOptions>() ?? new PlanOptions());
    c.Services.TryAddSingleton(TimeProvider.System);
    c.Services.AddScoped<Catalog>();
    c.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
    c.Services.AddScoped<PlanService>();

    c.Provide<IPlanV1, PlanV1>("plan.v1");
    c.Db<PlanDb>();
    c.AddControllers();
    c.Tool(PlanTools.ListPlans);
    c.Tool(PlanTools.GetSubscription);
    c.PurgeUser<PlanPurge>();
  }
}
