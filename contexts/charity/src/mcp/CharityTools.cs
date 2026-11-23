using Ctx.Charity.Api.V1;
using Ctx.Charity.Contract.V1;
using Ctx.Charity.Domain;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Charity.Mcp;

public static class CharityTools
{
  public sealed record SearchArgs(string? Name, string? Country, string? CauseKey, int? Limit, int? Skip);

  public sealed record GetArgs(string Id);

  public sealed record CausesArgs(string? Name, int? Limit, int? Skip);

  public static readonly McpToolDefinition Search = McpTool.Define<SearchArgs>(
    "charity.search", "Search charities by name, country or cause key", Policies.Registered,
    async call => (await call.Services.GetRequiredService<CharityService>().Search(
      new CharitySearchQuery { Name = call.Args.Name, Country = call.Args.Country, CauseKey = call.Args.CauseKey, Limit = call.Args.Limit ?? 20, Skip = call.Args.Skip ?? 0 },
      call.Ct)).Select(c => c.ToRes()).ToList());

  public static readonly McpToolDefinition Get = McpTool.Define<GetArgs>(
    "charity.get", "Get one charity by id", Policies.Registered,
    async call => new CharityRes((await call.Services.GetRequiredService<CharityService>().Get(Guid.Parse(call.Args.Id), call.Ct)).ToRes()));

  public static readonly McpToolDefinition Causes = McpTool.Define<CausesArgs>(
    "charity.causes", "Search causes by name", Policies.Registered,
    async call => (await call.Services.GetRequiredService<CauseService>().Search(
      new CauseSearch(null, call.Args.Name, call.Args.Limit ?? 20, call.Args.Skip ?? 0), call.Ct)).Select(c => c.ToRes()).ToList());
}
