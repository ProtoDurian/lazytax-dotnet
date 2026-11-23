using Ctx.Check.Contract.V1;
using Ctx.Check.Data;
using Ctx.Check.Domain;
using Ctx.Check.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Check;

/// <summary>The one registration of the check context.</summary>
public sealed class CheckContext : IContext
{
  public void Register(ContextBuilder c)
  {
    c.Services.AddScoped<ICheckStore, CheckStore>();
    c.Services.AddScoped<IHabitSchedule, HabitScheduleAdapter>();
    c.Services.AddScoped<IFreezes, FreezesAdapter>();
    c.Services.AddScoped<CheckService>();
    c.Services.AddScoped<OverviewService>();
    c.Services.AddScoped<SkipQuotaGuard>();

    c.Provide<ICheckV1, CheckV1>("check.v1");
    c.Db<CheckDb>();
    c.AddControllers();
    c.Tool(CheckTools.Complete);
    c.Tool(CheckTools.Skip);
    c.Tool(CheckTools.Overview);
    c.Tool(CheckTools.Executions);
    c.Worker<CheckTickWorker>();
    c.Subscribe<HabitIdsChanged, HabitChangedHandler>("habit.changed");
    c.Subscribe<HabitIdsChanged, HabitDeletedHandler>("habit.deleted");
    c.Subscribe<TimezoneMoved, TimezoneChangedHandler>("account.timezone_changed");
    c.PurgeUser<CheckPurge>();
  }
}
