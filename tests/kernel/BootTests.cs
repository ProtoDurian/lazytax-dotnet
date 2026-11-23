using LazyTax.Kernel.Boot;
using LazyTax.Platform.Context;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LazyTax.Kernel.Tests;

public sealed class BootTests(KernelApp app)
{
  private sealed class Fake : IContext
  {
    public void Register(ContextBuilder c)
    {
    }
  }

  private static (ContextManifest, IContext) M(string name, string[] needs, string[] provides, string[]? uses = null) =>
    (new ContextManifest { Id = "ctx_abcdef", Name = name, Needs = needs, Provides = provides, Uses = uses ?? ["postgres.main"], Folder = $"contexts/{name}" }, new Fake());

  [Fact]
  public void Needs_come_first()
  {
    var problems = new List<ContextProblem>();
    var ordered = ManifestLoader.Order([M("vacation", ["plan.v1"], ["vacation.v1"]), M("plan", [], ["plan.v1"])], new HashSet<string> { "postgres.main" }, problems);
    problems.Should().BeEmpty();
    ordered.Select(o => o.Manifest.Name).Should().Equal("plan", "vacation");
  }

  [Fact]
  public void A_missing_contract_names_the_context_and_the_fix()
  {
    var problems = new List<ContextProblem>();
    ManifestLoader.Order([M("vacation", ["plan.v1"], ["vacation.v1"])], new HashSet<string> { "postgres.main" }, problems);
    problems.Should().ContainSingle().Which.ToString().Should()
      .Be("context 'vacation' (contexts/vacation): needs plan.v1, but no context provides it. Fix: add the context that provides plan.v1, or remove it from needs");
  }

  [Fact]
  public void A_cycle_is_refused()
  {
    var problems = new List<ContextProblem>();
    ManifestLoader.Order([M("a", ["b.v1"], ["a.v1"]), M("b", ["a.v1"], ["b.v1"])], new HashSet<string> { "postgres.main" }, problems);
    problems.Should().Contain(p => p.Problem.Contains("cycle"));
  }

  [Fact]
  public void A_use_missing_from_dependency_yaml_is_refused()
  {
    var problems = new List<ContextProblem>();
    ManifestLoader.Order([M("a", [], ["a.v1"], ["redis.cache"])], new HashSet<string> { "postgres.main" }, problems);
    problems.Should().ContainSingle(p => p.Problem.Contains("redis.cache"));
  }

  [Fact]
  public void Every_endpoint_and_tool_has_a_policy()
  {
    var rows = PolicyTable.Endpoints(app.Services);
    rows.Should().NotBeEmpty();
    rows.Should().OnlyContain(r => r.Policy != null);
    rows.Should().Contain(r => r.Kind == "mcp" && r.Path == "vacation.create" && r.Subject == "userId");
  }
}
