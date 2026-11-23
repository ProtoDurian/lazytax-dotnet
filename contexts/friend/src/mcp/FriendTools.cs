using Ctx.Friend.Api.V1;
using Ctx.Friend.Domain;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using LazyTax.Platform.Problems;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Friend.Mcp;

public static class FriendTools
{
  public sealed record UserArgs(string UserId);

  public sealed record SearchArgs(string UserId, string Q, int? Limit);

  public sealed record RequestArgs(string UserId, string ToUserId);

  public sealed record AcceptArgs(string UserId, string FromUserId);

  public sealed record WatchersGetArgs(string UserId, string HabitId);

  public sealed record WatchersSetArgs(string UserId, string HabitId, string[] FriendUserIds);

  public static readonly McpToolDefinition List = McpTool.Define<UserArgs>(
    "friend.list", "List the user's friends", Policies.Registered,
    async call => (await call.Services.GetRequiredService<FriendService>().ListFriends(call.Args.UserId, call.Ct))
      .Select(x => x.ToRes()).ToList(),
    subject: "userId");

  public static readonly McpToolDefinition Search = McpTool.Define<SearchArgs>(
    "friend.search", "Search users by username prefix", Policies.Registered,
    async call => (await call.Services.GetRequiredService<FriendService>()
      .Search(call.Args.UserId, call.Args.Q, call.Args.Limit ?? FriendService.MaxSearchLimit, call.Ct))
      .Select(x => x.ToRes()).ToList(),
    subject: "userId");

  public static readonly McpToolDefinition Request = McpTool.Define<RequestArgs>(
    "friend.request", "Ask a user to be friends (accepts if they already asked)", Policies.Registered,
    async call => new FriendRelationRes((await call.Services.GetRequiredService<FriendService>()
      .SendRequest(call.Args.UserId, call.Args.ToUserId, call.Ct)).ToRes()),
    subject: "userId");

  public static readonly McpToolDefinition Accept = McpTool.Define<AcceptArgs>(
    "friend.accept", "Accept a friend request", Policies.Registered,
    async call => (await call.Services.GetRequiredService<FriendService>()
      .Accept(call.Args.UserId, call.Args.FromUserId, call.Ct))?.ToRes()
      ?? throw new NotFound("Friend Request", call.Args.FromUserId),
    subject: "userId");

  public static readonly McpToolDefinition WatchersGet = McpTool.Define<WatchersGetArgs>(
    "friend.watchers_get", "Which friends hear when a habit is missed", Policies.Registered,
    async call => (await call.Services.GetRequiredService<FriendStakeService>()
      .GetWatchers(call.Args.UserId, ParseHabit(call.Args.HabitId), call.Ct)).ToRes(ParseHabit(call.Args.HabitId)),
    subject: "userId");

  public static readonly McpToolDefinition WatchersSet = McpTool.Define<WatchersSetArgs>(
    "friend.watchers_set", "Replace the friends told when a habit is missed", Policies.Registered,
    async call => (await call.Services.GetRequiredService<FriendStakeService>()
      .SetWatchers(call.Args.UserId, ParseHabit(call.Args.HabitId), call.Args.FriendUserIds, call.Ct))
      .ToRes(ParseHabit(call.Args.HabitId)),
    subject: "userId");

  private static Guid ParseHabit(string id) =>
    Guid.TryParse(id, out var g) ? g : throw new ValidationFailed("Invalid request", "habitId", "habitId must be a guid");
}
