using Ctx.Charity.Contract.V1;
using Ctx.Charity.Data;
using Ctx.Charity.Domain;
using Ctx.Charity.Gateway;
using Ctx.Charity.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Charity;

/// <summary>The one registration of the charity context.</summary>
public sealed class CharityContext : IContext
{
  public void Register(ContextBuilder c)
  {
    c.Services.AddScoped<ICharityRepository, CharityRepository>();
    c.Services.AddScoped<ICauseRepository, CauseRepository>();
    c.Services.AddScoped<CharityService>();
    c.Services.AddScoped<CauseService>();
    c.Services.AddScoped<PledgeSyncService>();
    c.Services.AddScoped<IPledgeGateway, FakePledgeGateway>();

    c.Provide<ICharityV1, CharityV1>("charity.v1");
    c.Db<CharityDb>();
    c.AddControllers();
    c.Tool(CharityTools.Search);
    c.Tool(CharityTools.Get);
    c.Tool(CharityTools.Causes);
    c.PurgeUser<CharityPurge>();
  }
}
