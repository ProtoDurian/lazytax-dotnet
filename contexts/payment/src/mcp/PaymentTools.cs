using Ctx.Payment.Api.V1;
using Ctx.Payment.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Payment.Mcp;

/// <summary>MCP tools: the same service methods and checks as the routes.</summary>
public static class PaymentTools
{
  public sealed record ConsentArgs(string UserId, string? Purpose);

  public static readonly McpToolDefinition Consent = McpTool.Define<ConsentArgs>(
    "payment.consent", "Get a user's payment consent status (purpose: penalty or subscription)", Policies.Registered,
    async call => PaymentConsentRes.From(await call.Services.GetRequiredService<PaymentService>()
      .GetConsent(Owner.User(call.Args.UserId), PurposeParam.Parse(call.Args.Purpose), call.Ct)),
    subject: "userId");

  public static readonly McpToolDefinition DisableConsent = McpTool.Define<ConsentArgs>(
    "payment.disable_consent", "Revoke a user's payment consent (purpose: penalty or subscription)", Policies.Registered,
    async call =>
    {
      await call.Services.GetRequiredService<PaymentService>()
        .DisableConsent(Owner.User(call.Args.UserId), PurposeParam.Parse(call.Args.Purpose), call.Ct);
      return new { disabled = true };
    },
    subject: "userId");
}
