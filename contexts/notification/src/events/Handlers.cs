using Ctx.Notification.Contract.V1;
using Ctx.Notification.Domain;
using LazyTax.Platform.Outbox;

namespace Ctx.Notification.Events;

/// <summary>notification's own event: the send, after the requesting transaction committed.</summary>
public sealed class EmailRequestedHandler(EmailSender sender) : IOutboxSubscriber<EmailRequested>
{
  public Task Handle(OutboxEvent meta, EmailRequested payload, CancellationToken ct) => sender.Send(meta.Id, payload, ct);
}

public sealed class PenaltyChargedHandler(PenaltyNotifier notifier) : IOutboxSubscriber<PenaltyChargedEvent>
{
  public Task Handle(OutboxEvent meta, PenaltyChargedEvent payload, CancellationToken ct) => notifier.Charged(meta, payload, ct);
}

public sealed class PenaltyFailedHandler(PenaltyNotifier notifier) : IOutboxSubscriber<PenaltyFailedEvent>
{
  public Task Handle(OutboxEvent meta, PenaltyFailedEvent payload, CancellationToken ct) => notifier.Failed(meta, payload, ct);
}
