using Ctx.Disbursement.Contract.V1;
using Ctx.Disbursement.Data;
using Ctx.Disbursement.Domain;
using Ctx.Disbursement.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Disbursement;

/// <summary>The one registration of the disbursement context.</summary>
public sealed class DisbursementContext : IContext
{
  public void Register(ContextBuilder c)
  {
    c.Services.Configure<DisbursementOptions>(c.Settings);
    c.Services.AddScoped<IDisbursementRepository, DisbursementRepository>();
    c.Services.AddScoped<IPayoutTargets, CharityPayoutTargets>();
    c.Services.AddScoped<DisbursementService>();
    // The vendor is a fake with no network (tests script it); one instance, because it holds the "vendor's" donations.
    c.Services.AddSingleton<FakeDonationGateway>();
    c.Services.AddSingleton<IDonationGateway>(sp => sp.GetRequiredService<FakeDonationGateway>());

    c.Provide<IDisbursementV1, DisbursementV1>("disbursement.v1");
    c.Db<DisbursementDb>();
    c.AddControllers();
    c.Tool(DisbursementTools.Run);
    c.Tool(DisbursementTools.List);
    c.Worker<DisbursementWorker>();
    c.PurgeUser<DisbursementPurge>();
  }
}
