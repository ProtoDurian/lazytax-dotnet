using Ctx.Habit.Data;
using Ctx.Habit.Domain;
using Ctx.Habit.Contract.V1;
using Ctx.Habit.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Habit;

/// <summary>The one registration of the habit context.</summary>
public sealed class HabitContext : IContext
{
  public void Register(ContextBuilder c)
  {
    c.Services.AddSingleton(new HabitSettings(c.Settings.GetValue("MaxStakeCents", 1_000_000)));
    c.Services.AddScoped<IHabitRepository, HabitRepository>();
    c.Services.AddScoped<IRoutineRepository, RoutineRepository>();
    c.Services.AddScoped<HabitService>();
    c.Services.AddScoped<RoutineService>();
    c.Services.AddScoped<HabitQuotaGuard>();

    c.Provide<IHabitV1, HabitV1>("habit.v1");
    c.Db<HabitDb>();
    c.AddControllers();

    c.Tool(HabitTools.List);
    c.Tool(HabitTools.Get);
    c.Tool(HabitTools.Create);
    c.Tool(HabitTools.Update);
    c.Tool(HabitTools.Delete);
    c.Tool(HabitTools.Reorder);
    c.Tool(HabitTools.RoutineList);
    c.Tool(HabitTools.RoutineCreate);
    c.Tool(HabitTools.RoutineDelete);

    c.PurgeUser<HabitPurge>();
  }
}
