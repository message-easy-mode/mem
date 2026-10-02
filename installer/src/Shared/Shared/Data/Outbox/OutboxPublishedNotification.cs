using System;
using MediatR;

namespace Shared.Data.Outbox;

public sealed record OutboxPublishedNotification(
    Guid OutboxId,
    string ModuleName,
    DateTime OccurredOnUtc,
    string EventType,
    string PayloadJson,
    INotification Event
) : INotification;
