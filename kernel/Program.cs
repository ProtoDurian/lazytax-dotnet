using LazyTax.Kernel;
using LazyTax.Kernel.Boot;

// `dotnet run --project kernel -- policies` prints every route and MCP tool and exits non-zero if any lacks a policy.
var policiesOnly = args.Contains("policies");
WebApplication app;
try
{
  app = KernelHost.Build([.. args.Where(a => a != "policies")], ContextRegistry.All());
  if (policiesOnly)
  {
    Console.WriteLine(PolicyTable.Render(PolicyTable.Endpoints(app.Services, ((IEndpointRouteBuilder)app).DataSources)));
  }
  KernelHost.Verify(app.Services, ((IEndpointRouteBuilder)app).DataSources);
}
catch (ContextBootException e)
{
  Console.Error.WriteLine(e.Message);
  return 2;
}

if (policiesOnly) return 0;
await KernelHost.Migrate(app.Services);
await app.RunAsync();
return 0;
