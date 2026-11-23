using Ctx.Payment.Contract.V1;
using Ctx.Payment.Data;
using Ctx.Payment.Domain;
using Ctx.Payment.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ctx.Payment;

/// <summary>The one registration of the payment context.</summary>
public sealed class PaymentContext : IContext
{
  public void Register(ContextBuilder c)
  {
    var settings = new PaymentSettings();
    c.Settings.Bind(settings);
    c.Services.AddSingleton(settings);
    c.Services.TryAddSingleton(TimeProvider.System);

    // External gateways are fakes with no network (Airwallex, Logto management).
    c.Services.AddSingleton<FakeAirwallex>();
    c.Services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<FakeAirwallex>());
    c.Services.AddSingleton<FakeIdentityClaims>();
    c.Services.AddSingleton<IIdentityClaims>(sp => sp.GetRequiredService<FakeIdentityClaims>());

    c.Services.AddScoped<IConsentNotifier, NotificationConsentNotifier>();
    c.Services.AddScoped<ICustomerRepository, CustomerRepository>();
    c.Services.AddScoped<IChargeRepository, ChargeRepository>();
    c.Services.AddScoped<PaymentService>();
    c.Services.AddScoped<ChargeService>();
    c.Services.AddScoped<WebhookService>();

    c.Provide<IPaymentV1, PaymentV1>("payment.v1");
    c.Db<PaymentDb>();
    c.AddControllers();
    c.Tool(PaymentTools.Consent);
    c.Tool(PaymentTools.DisableConsent);
    c.BeforePurge<RevokeConsentsBeforePurge>();
    c.PurgeUser<PaymentPurge>();
  }
}
