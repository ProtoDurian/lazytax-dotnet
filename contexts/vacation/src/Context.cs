using Ctx.Vacation.Contract.V1;
using Ctx.Vacation.Data;
using Ctx.Vacation.Domain;
using Ctx.Vacation.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Vacation;

/// <summary>The one registration of the vacation context: vacation periods.</summary>
public sealed class VacationContext : IContext
{
  public void Register(ContextBuilder c)
  {
    c.Services.AddScoped<IVacationRepository, VacationRepository>();
    c.Services.AddScoped<VacationService>();
    c.Services.AddScoped<VacationQuotaGuard>();

    c.Provide<IVacationV1, VacationV1>("vacation.v1");
    c.Db<VacationDb>();
    c.AddControllers();
    c.Tool(VacationTools.Create);
    c.Tool(VacationTools.List);
    c.PurgeUser<VacationPurge>();
  }
}
