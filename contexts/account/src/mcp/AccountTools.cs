using Ctx.Account.Api.V1;
using Ctx.Account.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Account.Mcp;

public static class AccountTools
{
  public sealed record GetUserArgs(string UserId);

  /// <summary>Same as GET /api/v1/User/{id}.</summary>
  public static readonly McpToolDefinition GetUser = McpTool.Define<GetUserArgs>(
    "account.get_user", "Get a user's profile (yourself, or anyone as admin)", Policies.Registered,
    async call => (object?)new UserRes((await call.Services.GetRequiredService<UserService>()
      .Get(Owner.User(call.Args.UserId), call.Args.UserId, call.Ct)).ToRes()),
    subject: "userId");

  public sealed record GetConfigurationArgs(string UserId);

  /// <summary>Same as GET /api/v1/Configuration/me, for a given user.</summary>
  public static readonly McpToolDefinition GetConfiguration = McpTool.Define<GetConfigurationArgs>(
    "account.get_configuration", "Get a user's configuration (timezone, default charity)", Policies.Registered,
    async call => (object?)(await call.Services.GetRequiredService<ConfigurationService>()
      .GetByUser(call.Args.UserId, call.Ct)).ToRes(),
    subject: "userId");
}
