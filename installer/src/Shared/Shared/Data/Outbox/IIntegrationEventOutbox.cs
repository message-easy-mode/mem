using System;
using MediatR;

namespace Shared.Data.Outbox;


public interface IIntegrationEventOutbox
{
    Task EnqueueAsync(
        INotification integrationEvent,
        string moduleName,
        CancellationToken ct = default);
}