using Ctx.Account.Contract.V1;
using Ctx.Check.Contract.V1;
using Ctx.Habit.Contract.V1;
using Ctx.Plan.Contract.V1;
using LazyTax.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ctx.Proof.Tests.Integration;

/// <summary>
/// The kernel app with habit, check, account and plan replaced by in-memory fakes, so proof is tested on its own
/// real Postgres schema while those contexts are built elsewhere. Strava is switched on (the fake gateway).
/// </summary>
public sealed class ProofHost
{
  public FakeHabits Habits { get; } = new();
  public FakeCheck Check { get; }
  public FakePlan Plan { get; } = new();
  public WebApplicationFactory<Program> Factory { get; }
  public KernelApp App { get; }

  private ProofHost(KernelApp app, bool strava)
  {
    this.App = app;
    this.Check = new FakeCheck(this.Habits);
    this.Factory = app.WithWebHostBuilder(b =>
    {
      b.UseSetting("Contexts:proof:Strava:Enabled", strava ? "true" : "false");
      b.UseSetting("Contexts:proof:Strava:SubscriptionId", "0");
      b.ConfigureServices(s =>
      {
        s.RemoveAll<IHabitV1>();
        s.RemoveAll<ICheckV1>();
        s.RemoveAll<IPlanV1>();
        s.RemoveAll<IAccountV1>();
        s.AddSingleton<IHabitV1>(this.Habits);
        s.AddSingleton<ICheckV1>(this.Check);
        s.AddSingleton<IPlanV1>(this.Plan);
        s.AddSingleton<IAccountV1>(new FakeAccount());
      });
    });
  }

  public static ProofHost On(KernelApp app) => new(app, strava: true);

  public static ProofHost StravaOff(KernelApp app) => new(app, strava: false);

  public HttpClient As(string user, params string[] scopes) => this.Factory.CreateClient().As(user, scopes);

  public HttpClient Anonymous() => this.Factory.CreateClient();

  public HttpClient NoRedirect() => this.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

  public async Task InScope(Func<IServiceProvider, Task> work)
  {
    await using var scope = this.Factory.Services.CreateAsyncScope();
    await work(scope.ServiceProvider);
  }

  public async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
  {
    await using var scope = this.Factory.Services.CreateAsyncScope();
    return await work(scope.ServiceProvider);
  }
}
