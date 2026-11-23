using Ctx.Charity.Domain;
using LazyTax.Platform.Problems;

namespace Ctx.Charity.Tests.Unit;

public sealed class SyncAndValidationTests
{
  [Theory]
  [InlineData("Red Cross & Co.", "red-cross-co")]
  [InlineData("  --Hello   World--  ", "hello-world")]
  [InlineData("", "")]
  public void Slug_matches_lazytax(string name, string expected) => PledgeSyncService.Slug(name).Should().Be(expected);

  [Fact]
  public void Hierarchical_name_joins_parents_and_survives_cycles()
  {
    var a = new PledgeCause(1, "Health", 2);
    var b = new PledgeCause(2, "Cancer", 1);
    var lookup = new Dictionary<int, PledgeCause> { [1] = a, [2] = b };
    PledgeSyncService.HierarchicalName(a, lookup).Should().Be("Cancer > Health");
    PledgeSyncService.HierarchicalName(new PledgeCause(3, "Kids", null), lookup).Should().Be("Kids");
  }

  [Fact]
  public void Charity_validation_rejects_bad_country_and_url()
  {
    var ok = new CharityRecord("A", null, null, ["US"], null, "US", "https://a.example", null);
    Validation.Charity(ok).Should().Be(ok);
    var act1 = () => Validation.Charity(ok with { Countries = ["USA"] });
    act1.Should().Throw<ValidationFailed>();
    var act2 = () => Validation.Charity(ok with { WebsiteUrl = "not a url" });
    act2.Should().Throw<ValidationFailed>();
    var act3 = () => Validation.Charity(ok with { Name = "" });
    act3.Should().Throw<ValidationFailed>();
  }

  [Fact]
  public void Paging_limits()
  {
    var act = () => Validation.Paging(101, 0);
    act.Should().Throw<ValidationFailed>();
    Validation.Paging(100, 0);
  }

  [Fact]
  public async Task Fake_gateway_has_no_network_and_serves_orgs_per_country()
  {
    var g = new Ctx.Charity.Gateway.FakePledgeGateway();
    (await g.GetOrganizations("US", "2", 25, TestContext.Current.CancellationToken)).Should().HaveCount(1);
    (await g.GetOrganizations("US", "1", 25, TestContext.Current.CancellationToken)).Should().BeEmpty();
  }
}
