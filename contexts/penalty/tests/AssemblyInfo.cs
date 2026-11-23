global using static Ctx.Penalty.Tests.TestCt;

// One app per test assembly, with penalty's charge gateway and charity names replaced by fakes.
// The drain claims EVERY due penalty in the database, so tests run one at a time.
[assembly: AssemblyFixture(typeof(Ctx.Penalty.Tests.Integration.PenaltyApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Ctx.Penalty.Tests
{
  /// <summary>The running test's cancellation token (xUnit1051).</summary>
  internal static class TestCt
  {
    public static CancellationToken Ct => TestContext.Current.CancellationToken;
  }
}
