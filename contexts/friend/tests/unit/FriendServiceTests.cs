using Ctx.Friend.Contract.V1;
using Ctx.Friend.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;

namespace Ctx.Friend.Tests.Unit;

public sealed class FriendServiceTests
{
  private sealed class Uow : IUnitOfWork
  {
    public Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
    public Task Start(Func<Task> work, CancellationToken ct = default) => work();
    public Task LockUser(string userId, CancellationToken ct = default) => Task.CompletedTask;
  }

  private sealed class Events : IOutbox
  {
    public List<(string Name, object Payload)> Added { get; } = [];
    public Task Add(string eventName, object payload, CancellationToken ct = default)
    {
      this.Added.Add((eventName, payload));
      return Task.CompletedTask;
    }
  }

  private sealed class Dir : IUserDirectory
  {
    public Task<FriendUser?> Get(string userId, CancellationToken ct) =>
      Task.FromResult<FriendUser?>(userId.StartsWith("gone") ? null : new FriendUser(userId, "name-" + userId));
    public Task<IReadOnlyDictionary<string, string>> Usernames(IReadOnlyCollection<string> userIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyDictionary<string, string>>(userIds.ToDictionary(i => i, i => "name-" + i));
    public Task<IReadOnlyList<FriendUser>> Search(string excludeUserId, string query, int limit, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<FriendUser>>([]);
  }

  /// <summary>A repository holding only the relation the test sets.</summary>
  private sealed class Repo(FriendRelation relation) : IFriendRepository
  {
    public List<string> Calls { get; } = [];
    public Task<FriendRelation> Relation(Owner owner, string otherUserId, CancellationToken ct) => Task.FromResult(relation);
    public Task<IReadOnlyDictionary<string, FriendRelation>> Relations(Owner owner, IReadOnlyCollection<string> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlyDictionary<string, FriendRelation>>(new Dictionary<string, FriendRelation>());
    public Task CreateRequest(string fromUserId, string toUserId, CancellationToken ct) { this.Calls.Add("request"); return Task.CompletedTask; }
    public Task<IReadOnlyList<RequestRow>> ListRequests(Owner owner, CancellationToken ct) => Task.FromResult<IReadOnlyList<RequestRow>>([]);
    public Task<bool> DeleteRequest(Owner owner, string otherUserId, CancellationToken ct) => Task.FromResult(false);
    public Task<DateTime> CreateFriendship(string userId, string friendUserId, CancellationToken ct) { this.Calls.Add("friendship"); return Task.FromResult(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)); }
    public Task<IReadOnlyList<FriendshipRow>> ListFriends(Owner owner, CancellationToken ct) => Task.FromResult<IReadOnlyList<FriendshipRow>>([]);
    public Task<bool> DeleteFriendship(Owner owner, string friendUserId, CancellationToken ct) => Task.FromResult(false);
    public Task<FriendInvitePrincipal> GetOrCreateInvite(string userId, string newCode, CancellationToken ct) => Task.FromResult(new FriendInvitePrincipal(newCode, userId));
    public Task<FriendInvitePrincipal?> GetInvite(string code, CancellationToken ct) => Task.FromResult<FriendInvitePrincipal?>(new FriendInvitePrincipal(code, "owner"));
  }

  private static (FriendService Service, Repo Repo, Events Events) Make(FriendRelation relation)
  {
    var repo = new Repo(relation);
    var events = new Events();
    return (new FriendService(repo, new Dir(), new Uow(), events), repo, events);
  }

  [Fact]
  public async Task Asking_with_no_relation_creates_a_request_and_an_event()
  {
    var (s, repo, events) = Make(FriendRelation.None);
    (await s.SendRequest("a", "b", default)).Should().Be(FriendRelation.Outgoing);
    repo.Calls.Should().Equal("request");
    events.Added.Should().ContainSingle(e => e.Name == FriendEvents.RequestSent);
  }

  [Fact]
  public async Task Asking_back_accepts_instead()
  {
    var (s, repo, events) = Make(FriendRelation.Incoming);
    (await s.SendRequest("a", "b", default)).Should().Be(FriendRelation.Friend);
    repo.Calls.Should().Equal("friendship");
    events.Added.Should().ContainSingle(e => e.Name == FriendEvents.Accepted);
  }

  [Theory]
  [InlineData(FriendRelation.Friend)]
  [InlineData(FriendRelation.Outgoing)]
  public async Task Asking_again_is_a_no_op(FriendRelation existing)
  {
    var (s, repo, events) = Make(existing);
    (await s.SendRequest("a", "b", default)).Should().Be(existing);
    repo.Calls.Should().BeEmpty();
    events.Added.Should().BeEmpty();
  }

  [Fact]
  public async Task Refuses_yourself_and_missing_users()
  {
    var (s, _, _) = Make(FriendRelation.None);
    await s.Invoking(x => x.SendRequest("a", "a", default)).Should().ThrowAsync<ValidationFailed>();
    await s.Invoking(x => x.SendRequest("a", "gone1", default)).Should().ThrowAsync<NotFound>();
    await s.Invoking(x => x.SendRequest("gone1", "b", default)).Should().ThrowAsync<NotFound>();
  }

  [Fact]
  public async Task Accept_needs_an_incoming_request()
  {
    var (s, repo, _) = Make(FriendRelation.Outgoing);
    (await s.Accept("a", "b", default)).Should().BeNull();
    repo.Calls.Should().BeEmpty();
  }

  [Theory]
  [InlineData("", 5)]
  [InlineData("a", 5)]
  [InlineData("  a ", 5)]
  [InlineData("abc", 0)]
  [InlineData("abc", 21)]
  public async Task Search_checks_its_input(string q, int limit)
  {
    var (s, _, _) = Make(FriendRelation.None);
    await s.Invoking(x => x.Search("a", q, limit, default)).Should().ThrowAsync<ValidationFailed>();
  }

  [Fact]
  public async Task Your_own_invite_cannot_be_accepted()
  {
    var (s, _, _) = Make(FriendRelation.None);
    await s.Invoking(x => x.AcceptInvite("owner", "ABC", default)).Should().ThrowAsync<ValidationFailed>();
  }

  [Fact]
  public async Task Invite_codes_use_the_unambiguous_alphabet()
  {
    var (s, _, _) = Make(FriendRelation.None);
    var invite = await s.GetOrCreateInvite("a", default);
    invite.Code.Should().HaveLength(FriendService.InviteCodeLength);
    invite.Code.Should().MatchRegex("^[23456789abcdefghjkmnpqrstuvwxyz]+$");
  }
}
