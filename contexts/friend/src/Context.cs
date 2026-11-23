using Ctx.Habit.Contract.V1;
using Microsoft.Extensions.DependencyInjection;
using Ctx.Account.Contract.V1;
using Ctx.Friend.Contract.V1;
using Ctx.Friend.Data;
using Ctx.Friend.Domain;
using Ctx.Friend.Mcp;
using LazyTax.Platform.Context;

namespace Ctx.Friend;

/// <summary>The one registration of the friend context.</summary>
public sealed class FriendContext : IContext
{
  public void Register(ContextBuilder c)
  {
    c.Services.AddScoped<IFriendRepository, FriendRepository>();
    c.Services.AddScoped<IFriendStakeRepository, FriendStakeRepository>();
    c.Services.AddScoped<IUserDirectory, AccountUserDirectory>();
    c.Services.AddScoped<FriendService>();
    c.Services.AddScoped<FriendStakeService>();

    c.Provide<IFriendV1, FriendV1>("friend.v1");
    c.Db<FriendDb>();
    c.AddControllers();
    c.Tool(FriendTools.List);
    c.Tool(FriendTools.Search);
    c.Tool(FriendTools.Request);
    c.Tool(FriendTools.Accept);
    c.Tool(FriendTools.WatchersGet);
    c.Tool(FriendTools.WatchersSet);
    c.Subscribe<FriendRequestSent, FriendRequestSentHandler>(FriendEvents.RequestSent);
    c.Subscribe<FriendAccepted, FriendAcceptedHandler>(FriendEvents.Accepted);
    c.Subscribe<CheckSettled, CheckSettledHandler>("check.settled");
    c.Subscribe<HabitDeleted, HabitDeletedHandler>("habit.deleted");
    c.PurgeUser<FriendPurge>();
  }
}
