#pragma warning disable xUnit1051 // tests run to completion; no cancellation token needed
using Ctx.Habit.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ctx.Habit.Tests.Integration;

/// <summary>The contract other contexts call (check, proof, friend).</summary>
public sealed class HabitV1Tests(KernelApp app)
{
  private async Task<(string Habit, string Version)> Create(string user)
  {
    var res = await app.ClientAs(user).PostAsJsonAsync($"/api/v1/Habit/{user}", new
    {
      task = "Run",
      daysOfWeek = new[] { "Monday" },
      notificationTime = "07:00:00",
      stake = "0",
      timezone = "Asia/Singapore",
    });
    var j = await res.Content.ReadFromJsonAsync<JsonElement>();
    return (j.GetProperty("habitId").GetString()!, j.GetProperty("id").GetString()!);
  }

  [Fact]
  public async Task GetVersion_is_owner_scoped()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var (_, version) = await Create(a);
    await app.InScope(async sp =>
    {
      var habit = sp.GetRequiredService<IHabitV1>();
      (await habit.GetVersion(Owner.User(a), Guid.Parse(version))).Should().NotBeNull();
      (await habit.GetVersion(Owner.User(b), Guid.Parse(version))).Should().BeNull();
      (await habit.GetVersion(Owner.Anyone(), Guid.Parse(version))).Should().NotBeNull();
    });
  }

  [Fact]
  public async Task Owns_and_count()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var (habit, _) = await Create(a);
    await app.InScope(async sp =>
    {
      var v1 = sp.GetRequiredService<IHabitV1>();
      (await v1.Owns(a, Guid.Parse(habit))).Should().BeTrue();
      (await v1.Owns(b, Guid.Parse(habit))).Should().BeFalse();
      (await v1.CountHabits(a)).Should().Be(1);
    });
  }

  [Fact]
  public async Task ListSchedulable_returns_enabled_habits_with_a_timezone()
  {
    var a = TestTokens.NewUser();
    var (habit, version) = await Create(a);
    await app.InScope(async sp =>
    {
      var rows = await sp.GetRequiredService<IHabitV1>().ListSchedulable([a], [Guid.Parse(habit)]);
      var row = rows.Should().ContainSingle().Subject;
      row.VersionId.Should().Be(Guid.Parse(version));
      row.Timezone.Should().NotBeNullOrEmpty();
      row.DeadlineTime.Should().Be(new TimeOnly(23, 59));
    });
  }
}
