using Ctx.Account.Api.V1;
using Ctx.Account.Contract.V1;
using Ctx.Account.Data;
using Ctx.Account.Domain;
using Ctx.Account.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Account;

/// <summary>The one registration of the account context: users, configurations, account deletion.</summary>
public sealed class AccountContext : IContext
{
  public void Register(ContextBuilder c)
  {
    c.Services.AddScoped<IUserRepository, UserRepository>();
    c.Services.AddScoped<IConfigurationRepository, ConfigurationRepository>();
    c.Services.AddScoped<UserService>();
    c.Services.AddScoped<ConfigurationService>();
    c.Services.AddScoped<TokenDataExtractor>();

    c.Provide<IAccountV1, AccountV1>("account.v1");
    c.Db<AccountDb>();
    c.AddControllers();
    c.Tool(AccountTools.GetUser);
    c.Tool(AccountTools.GetConfiguration);
    c.PurgeUser<AccountPurge>();
  }
}
