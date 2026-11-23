using Ctx.Habit.Contract.V1;
using Ctx.Notification.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ctx.Friend.Tests.Integration;

/// <summary>Test seams: users seeded into account's table, and a fake habit contract (habit is another lane).</summary>
public static class Support
{
  /// <summary>A user that exists in account (friend reads usernames through account.v1). Returns the user id.</summary>
  public static async Task<string> NewUser(this KernelApp app, string? prefix = null)
  {
    var id = TestTokens.NewUser();
    var name = (prefix ?? "user") + "_" + id[2..];
    await using var conn = new NpgsqlConnection(app.ConnectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(
      """INSERT INTO account."Users" ("Id", "Username", "Email", "EmailVerified", "Scopes", "Active") VALUES (@id, @n, @e, true, '{}', true)""", conn);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("n", name);
    cmd.Parameters.AddWithValue("e", name + "@test.local");
    await cmd.ExecuteNonQueryAsync();
    return id;
  }

  public static async Task<string> UsernameOf(this KernelApp app, string userId)
  {
    await using var conn = new NpgsqlConnection(app.ConnectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand("""SELECT "Username" FROM account."Users" WHERE "Id" = @id""", conn);
    cmd.Parameters.AddWithValue("id", userId);
    return (string)(await cmd.ExecuteScalarAsync())!;
  }

  /// <summary>The same app with habit.v1 replaced by <see cref="FakeHabit"/>.</summary>
  public static WebApplicationFactory<Program> WithFakeHabit(this KernelApp app) =>
    Hosts.GetValue(app, a => a.WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
      foreach (var d in s.Where(d => d.ServiceType == typeof(IHabitV1) || d.ServiceType == typeof(INotificationV1)).ToList()) s.Remove(d);
      s.AddScoped<IHabitV1, FakeHabit>();
      s.AddScoped<INotificationV1, FakeNotification>();
    })));

  /// <summary>Delivers due outbox events from the fake-habit host, so events of parallel test classes meet the same habit contract.</summary>
  public static Task Dispatch(this KernelApp app) =>
    app.WithFakeHabit().Services.GetRequiredService<LazyTax.Kernel.Outbox.IOutboxDispatcher>().DispatchOnce();

  private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<KernelApp, WebApplicationFactory<Program>> Hosts = [];
}

/// <summary>An in-memory habit contract: habits exist only when a test registers them.</summary>
public sealed class FakeHabit : IHabitV1
{
  private static readonly Dictionary<Guid, (string UserId, string Task)> Habits = [];

  public static Guid Add(string userId, string task = "Run 5k")
  {
    var id = Guid.NewGuid();
    lock (Habits) Habits[id] = (userId, task);
    return id;
  }

  public Task<bool> Owns(string userId, Guid habitId, CancellationToken ct = default)
  {
    lock (Habits) return Task.FromResult(Habits.TryGetValue(habitId, out var h) && h.UserId == userId);
  }

  public Task<HabitVersionView?> GetCurrent(Owner owner, Guid habitId, CancellationToken ct = default)
  {
    lock (Habits)
    {
      if (!Habits.TryGetValue(habitId, out var h) || (!owner.IsAnyone && owner.UserId != h.UserId)) return Task.FromResult<HabitVersionView?>(null);
      return Task.FromResult<HabitVersionView?>(new HabitVersionView(
        Guid.NewGuid(), habitId, h.UserId, 1, h.Task, [], new TimeOnly(8, 0), new TimeOnly(20, 0), null, 0, "USD", 0,
        "UTC", true, false, true, 0, null));
    }
  }

  public Task<IReadOnlyList<SchedulableHabit>> ListSchedulable(IReadOnlyCollection<string>? userIds = null, IReadOnlyCollection<Guid>? habitIds = null, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<HabitVersionView?> GetVersion(Owner owner, Guid versionId, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<IReadOnlyList<HabitVersionView>> GetCurrentMany(Owner owner, IReadOnlyCollection<Guid> habitIds, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<IReadOnlyList<HabitVersionView>> ListCurrent(Owner owner, int limit, int skip, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<IReadOnlyList<HabitVersionView>> ListVersions(Owner owner, Guid habitId, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<int> CountHabits(string userId, CancellationToken ct = default) => throw new NotImplementedException();
}

/// <summary>An in-memory inbox that records what friend posts (notification is another lane). Same dedupe rule as the contract.</summary>
public sealed class FakeNotification : INotificationV1
{
  private static readonly List<InboxNotification> Rows = [];
  private static readonly HashSet<(string, string)> Keys = [];

  public static IReadOnlyList<InboxNotification> Of(string userId)
  {
    lock (Rows) return [.. Rows.Where(r => r.UserId == userId)];
  }

  public Task<InboxNotification> Post(string userId, string kind, string title, string body, string? actorUserId = null,
    string? data = null, string? dedupeKey = null, CancellationToken ct = default)
  {
    lock (Rows)
    {
      if (dedupeKey is not null && !Keys.Add((userId, dedupeKey)))
        return Task.FromResult(Rows.First(r => r.UserId == userId && r.Kind == kind));
      var row = new InboxNotification(Guid.NewGuid(), userId, kind, title, body, actorUserId, data, DateTime.UtcNow, null);
      Rows.Add(row);
      return Task.FromResult(row);
    }
  }

  public Task Email(string userId, string template, IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default) => Task.CompletedTask;

  public Task<int> UnreadCount(string userId, CancellationToken ct = default) => Task.FromResult(Of(userId).Count);
}
