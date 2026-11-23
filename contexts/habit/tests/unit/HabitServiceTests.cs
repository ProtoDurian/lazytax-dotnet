#pragma warning disable xUnit1051 // tests run to completion; no cancellation token needed
using Ctx.Charity.Contract.V1;
using Ctx.Habit.Contract.V1;
using Ctx.Habit.Domain;
using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;

namespace Ctx.Habit.Tests.Unit;

public sealed class HabitServiceTests
{
  private sealed class FakeUow : IUnitOfWork
  {
    public int Locks;
    public Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
    public Task Start(Func<Task> work, CancellationToken ct = default) => work();
    public Task LockUser(string userId, CancellationToken ct = default) { this.Locks++; return Task.CompletedTask; }
  }

  private sealed class FakeOutbox : IOutbox
  {
    public List<(string Name, object Payload)> Added { get; } = [];
    public Task Add(string eventName, object payload, CancellationToken ct = default) { this.Added.Add((eventName, payload)); return Task.CompletedTask; }
  }

  private sealed class FakePlan(int habits) : IPlanV1
  {
    public Task<string> EffectiveTier(string userId, CancellationToken ct = default) => Task.FromResult("free");
    public Task<Quota> Quota(string userId, QuotaKind kind, CancellationToken ct = default) => Task.FromResult(new Quota("free", "ent.habits.max", habits));
    public Task<int> FreezeCap(string userId, int maxStreak, CancellationToken ct = default) => Task.FromResult(0);
    public Task EnsureTier(string userId, string minTier, string feature, CancellationToken ct = default) => Task.CompletedTask;
  }

  private sealed class FakeCharity(bool exists) : ICharityV1
  {
    public Task<CharityView?> Get(Guid id, CancellationToken ct = default) => Task.FromResult<CharityView?>(null);
    public Task<bool> Exists(Guid id, CancellationToken ct = default) => Task.FromResult(exists);
    public Task<IReadOnlyList<CharityView>> Search(CharitySearchQuery q, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CharityView>>([]);
    public Task<CharityView?> GetByExternalId(string source, string externalKey, CancellationToken ct = default) => Task.FromResult<CharityView?>(null);
    public Task<string?> GetExternalKey(Guid charityId, string source, CancellationToken ct = default) => Task.FromResult<string?>(null);
  }

  private sealed class FakeRepo : IHabitRepository, IRoutineRepository
  {
    public List<HabitVersionView> Habits { get; } = [];
    public List<RoutineView> Routines { get; } = [];

    public Task<HabitVersionView?> GetCurrent(Owner o, Guid id, CancellationToken ct) =>
      Task.FromResult(this.Habits.FirstOrDefault(h => h.HabitId == id && (o.IsAnyone || h.UserId == o.UserId)));
    public Task<HabitVersionView?> GetVersion(Owner o, Guid id, CancellationToken ct) => Task.FromResult<HabitVersionView?>(null);
    public Task<IReadOnlyList<HabitVersionView>> GetCurrentMany(Owner o, IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<HabitVersionView>>([]);
    public Task<IReadOnlyList<HabitVersionView>> Search(Owner o, HabitSearch s, CancellationToken ct) => Task.FromResult<IReadOnlyList<HabitVersionView>>(this.Habits);
    public Task<IReadOnlyList<HabitVersionView>> ListVersions(Owner o, Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<HabitVersionView>>([]);
    public Task<IReadOnlyList<SchedulableRow>> ListSchedulable(IReadOnlyCollection<string>? u, IReadOnlyCollection<Guid>? h, CancellationToken ct) => Task.FromResult<IReadOnlyList<SchedulableRow>>([]);
    public Task<int> Count(string userId, CancellationToken ct) => Task.FromResult(this.Habits.Count(h => h.UserId == userId));

    public Task<HabitVersionView> Create(string userId, VersionRecord r, Guid routineId, CancellationToken ct)
    {
      var v = new HabitVersionView(Guid.NewGuid(), Guid.NewGuid(), userId, 1, r.Task, r.DaysOfWeek, r.NotificationTime, r.DeadlineTime,
        r.CharityId, r.StakeCents, "USD", 10_000, r.Timezone, true, false, true, 0, routineId);
      this.Habits.Add(v);
      return Task.FromResult(v);
    }

    public Task<HabitVersionView?> Update(Owner o, Guid id, VersionRecord r, bool enabled, CancellationToken ct) =>
      Task.FromResult(this.Habits.FirstOrDefault(h => h.HabitId == id && h.UserId == o.UserId) is { } h ? h with { Enabled = enabled, Task = r.Task } : null);

    public Task<bool> Delete(Owner o, Guid id, CancellationToken ct) => Task.FromResult(this.Habits.RemoveAll(h => h.HabitId == id && h.UserId == o.UserId) > 0);
    public Task<IReadOnlyList<Guid>> OrderedIds(Owner o, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>(this.Habits.Where(h => h.UserId == o.UserId).Select(h => h.HabitId).ToList());
    public Task SetOrder(string userId, IReadOnlyList<Guid> ids, CancellationToken ct) => Task.CompletedTask;

    Task<RoutineView?> IRoutineRepository.Get(Owner o, Guid id, CancellationToken ct) =>
      Task.FromResult(this.Routines.FirstOrDefault(r => r.Id == id && r.UserId == o.UserId));
    public Task<IReadOnlyList<RoutineView>> Search(Owner o, RoutineSearch s, CancellationToken ct) => Task.FromResult<IReadOnlyList<RoutineView>>(this.Routines);
    Task<RoutineView> IRoutineRepository.Create(string userId, string name, CancellationToken ct)
    {
      var r = new RoutineView(Guid.NewGuid(), userId, name);
      this.Routines.Add(r);
      return Task.FromResult(r);
    }
    public Task<RoutineView?> Update(Owner o, Guid id, string name, CancellationToken ct) => Task.FromResult<RoutineView?>(null);
    Task<IReadOnlyList<Guid>?> IRoutineRepository.Delete(Owner o, Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>?>(null);
  }

  private static (HabitService Svc, FakeRepo Repo, FakeOutbox Outbox, FakeUow Uow) Make(int quota = 2, bool charity = true)
  {
    var repo = new FakeRepo();
    var outbox = new FakeOutbox();
    var uow = new FakeUow();
    return (new HabitService(repo, repo, new FakePlan(quota), new FakeCharity(charity), outbox, uow, new HabitSettings(1_000_000)), repo, outbox, uow);
  }

  private static HabitInput Input(string task = "Run", Guid? charity = null, string stake = "0") =>
    new(task, ["Monday"], "07:00:00", stake, charity, "Asia/Singapore", null);

  [Fact]
  public async Task Create_locks_counts_and_tells_check()
  {
    var (svc, _, outbox, uow) = Make();
    var created = await svc.Create("u1", Input(), null, default);
    uow.Locks.Should().Be(1);
    outbox.Added.Should().ContainSingle(e => e.Name == HabitEvents.Changed);
    ((HabitChanged)outbox.Added[0].Payload).HabitIds.Should().Equal(created.HabitId);
  }

  [Fact]
  public async Task Create_over_quota_is_tier_insufficient_and_emits_nothing()
  {
    var (svc, _, outbox, _) = Make(quota: 1);
    await svc.Create("u1", Input("a"), null, default);
    outbox.Added.Clear();
    var act = () => svc.Create("u1", Input("b"), null, default);
    (await act.Should().ThrowAsync<TierInsufficient>()).Which.Status.Should().Be(400);
    outbox.Added.Should().BeEmpty();
  }

  [Fact]
  public async Task Create_with_unknown_charity_is_a_validation_error()
  {
    var (svc, _, _, _) = Make(charity: false);
    var act = () => svc.Create("u1", Input(charity: Guid.NewGuid(), stake: "5"), null, default);
    await act.Should().ThrowAsync<ValidationFailed>();
  }

  [Fact]
  public async Task Create_in_someone_elses_routine_is_404()
  {
    var (svc, repo, _, _) = Make();
    var theirs = await ((IRoutineRepository)repo).Create("u2", "r", default);
    var act = () => svc.Create("u1", Input(), theirs.Id, default);
    (await act.Should().ThrowAsync<NotFound>()).Which.Status.Should().Be(404);
  }

  [Fact]
  public async Task Update_of_another_users_habit_is_404_and_emits_nothing()
  {
    var (svc, _, outbox, _) = Make();
    var a = await svc.Create("userA", Input(), null, default);
    outbox.Added.Clear();
    var act = () => svc.Update("userB", a.HabitId, Input("hijack"), true, default);
    await act.Should().ThrowAsync<NotFound>();
    outbox.Added.Should().BeEmpty();
  }

  [Fact]
  public async Task Reenabling_at_the_limit_is_refused()
  {
    var (svc, repo, _, _) = Make(quota: 1);
    var a = await svc.Create("u1", Input(), null, default);
    repo.Habits[0] = repo.Habits[0] with { Enabled = false };
    var act = () => svc.Update("u1", a.HabitId, Input(), true, default);
    await act.Should().ThrowAsync<TierInsufficient>();
  }

  [Fact]
  public async Task Delete_emits_deleted_and_changed()
  {
    var (svc, _, outbox, _) = Make();
    var a = await svc.Create("u1", Input(), null, default);
    outbox.Added.Clear();
    await svc.Delete("u1", a.HabitId, default);
    outbox.Added.Select(e => e.Name).Should().Equal(HabitEvents.Deleted, HabitEvents.Changed);
  }
}
