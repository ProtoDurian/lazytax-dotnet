using Ctx.Notification.Contract.V1;
using Ctx.Notification.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Notification.Tests.Unit;

public sealed class InboxServiceTests
{
  private sealed class Uow : IUnitOfWork
  {
    public Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
    public Task Start(Func<Task> work, CancellationToken ct = default) => work();
    public Task LockUser(string userId, CancellationToken ct = default) => Task.CompletedTask;
  }

  private sealed class Repo : IInboxRepository
  {
    public int Created;
    public bool MarkResult = true;
    public Task<InboxNotification> Create(string userId, InboxRecord r, CancellationToken ct)
    {
      this.Created++;
      return Task.FromResult(new InboxNotification(Guid.NewGuid(), userId, r.Kind, r.Title, r.Body, r.ActorUserId, r.Data, DateTime.UtcNow, null));
    }
    public Task<IReadOnlyList<InboxNotification>> Search(InboxSearch s, CancellationToken ct) => Task.FromResult<IReadOnlyList<InboxNotification>>([]);
    public Task<int> UnreadCount(Owner o, CancellationToken ct) => Task.FromResult(0);
    public Task<bool> MarkRead(Owner o, Guid id, DateTime at, CancellationToken ct) => Task.FromResult(this.MarkResult);
    public Task<int> MarkAllRead(Owner o, DateTime at, CancellationToken ct) => Task.FromResult(0);
  }

  private readonly Repo repo = new();

  private InboxService Service() => new(this.repo, new Uow());

  [Theory]
  [InlineData("", "body")]
  [InlineData("  ", "body")]
  [InlineData("title", "")]
  public async Task Empty_title_or_body_is_400(string title, string body)
  {
    var act = () => this.Service().Post("u", new InboxRecord("k", title, body, null, null, null), default);
    (await act.Should().ThrowAsync<ValidationFailed>()).Which.Status.Should().Be(400);
    this.repo.Created.Should().Be(0);
  }

  [Fact]
  public async Task Too_long_text_is_400()
  {
    var act = () => this.Service().Post("u", new InboxRecord("k", new string('a', 257), "b", null, null, null), default);
    await act.Should().ThrowAsync<ValidationFailed>();
  }

  [Fact]
  public async Task A_valid_post_reaches_the_repository()
  {
    var n = await this.Service().Post("u", new InboxRecord("k", "t", "b", null, null, null), default);
    n.UserId.Should().Be("u");
    this.repo.Created.Should().Be(1);
  }

  [Theory]
  [InlineData(0, 0)]
  [InlineData(51, 0)]
  [InlineData(10, -1)]
  public async Task Bad_limit_or_skip_is_400(int limit, int skip)
  {
    var act = () => this.Service().Search(Owner.User("u"), null, limit, skip, default);
    await act.Should().ThrowAsync<ValidationFailed>();
  }

  [Fact]
  public async Task Mark_read_of_someone_elses_row_is_404()
  {
    this.repo.MarkResult = false;
    var act = () => this.Service().MarkRead(Owner.User("u"), Guid.NewGuid(), default);
    (await act.Should().ThrowAsync<NotFound>()).Which.Status.Should().Be(404);
  }
}
