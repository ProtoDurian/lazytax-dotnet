using Ctx.Notification.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Lifecycle;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Notification.Tests.Integration;

public sealed class PurgeTests(KernelApp app)
{
  [Fact]
  public async Task Purging_a_user_leaves_none_of_their_rows_and_keeps_everyone_elses()
  {
    var gone = TestTokens.NewUser();
    var kept = TestTokens.NewUser();
    await app.InScope(async sp =>
    {
      var n = sp.GetRequiredService<INotificationV1>();
      await n.Post(gone, InboxKinds.FriendRequest, "to gone", "b");
      await n.Post(kept, InboxKinds.FriendAccepted, "gone accepted", "b", actorUserId: gone);
      await n.Post(kept, InboxKinds.FriendRequest, "unrelated", "b");
    });

    await app.InScope(async sp =>
    {
      var uow = sp.GetRequiredService<IUnitOfWork>();
      await uow.Start(async () =>
      {
        foreach (var purge in sp.GetServices<IUserPurge>()) await purge.Purge(gone, default);
      });
    });

    const string sql = "SELECT count(*) FROM notification.\"InboxNotifications\" WHERE \"UserId\" = @u OR \"ActorUserId\" = @u";
    (await app.Count(sql, ("u", gone))).Should().Be(0, "the user's own rows and the ones they caused are both gone");
    (await app.Count("SELECT count(*) FROM notification.\"InboxNotifications\" WHERE \"UserId\" = @u", ("u", kept)))
      .Should().Be(1, "only the unrelated row of the other user remains");
  }
}
