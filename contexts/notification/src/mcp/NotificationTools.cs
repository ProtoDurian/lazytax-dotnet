using Ctx.Notification.Api.V1;
using Ctx.Notification.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Notification.Mcp;

/// <summary>The inbox as MCP tools: the same service methods as the routes, the same policy and subject rule.</summary>
public static class NotificationTools
{
  public sealed record ListArgs(string UserId, bool? UnreadOnly, int? Limit, int? Skip);

  public sealed record UserArgs(string UserId);

  public sealed record MarkReadArgs(string UserId, Guid Id);

  public static readonly McpToolDefinition List = McpTool.Define<ListArgs>(
    "notification.list", "List a user's inbox notifications, newest first (limit 1..50, default 20)", Policies.Registered,
    async call => (await call.Services.GetRequiredService<InboxService>()
      .Search(Owner.User(call.Args.UserId), call.Args.UnreadOnly, call.Args.Limit, call.Args.Skip, call.Ct))
      .Select(InboxNotificationRes.From).ToList(),
    subject: "userId");

  public static readonly McpToolDefinition UnreadCount = McpTool.Define<UserArgs>(
    "notification.unreadCount", "Count a user's unread inbox notifications", Policies.Registered,
    async call => new InboxCountRes(await call.Services.GetRequiredService<InboxService>()
      .UnreadCount(Owner.User(call.Args.UserId), call.Ct)),
    subject: "userId");

  public static readonly McpToolDefinition MarkRead = McpTool.Define<MarkReadArgs>(
    "notification.markRead", "Mark one inbox notification as read (404 when it is not the user's)", Policies.Registered,
    async call =>
    {
      await call.Services.GetRequiredService<InboxService>().MarkRead(Owner.User(call.Args.UserId), call.Args.Id, call.Ct);
      return new { read = true };
    },
    subject: "userId");

  public static readonly McpToolDefinition MarkAllRead = McpTool.Define<UserArgs>(
    "notification.markAllRead", "Mark every unread inbox notification of a user as read", Policies.Registered,
    async call => new InboxCountRes(await call.Services.GetRequiredService<InboxService>()
      .MarkAllRead(Owner.User(call.Args.UserId), call.Ct)),
    subject: "userId");
}
