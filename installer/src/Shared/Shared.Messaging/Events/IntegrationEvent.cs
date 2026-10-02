using MediatR;

namespace Shared.Messaging.Events;

public abstract record IntegrationEvent : INotification
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
    public string EventType => GetType().AssemblyQualifiedName!;
}