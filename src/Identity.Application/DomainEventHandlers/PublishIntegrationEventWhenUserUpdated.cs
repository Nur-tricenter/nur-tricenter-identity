using Identity.Application.Contracts;
using Nur.Tricenter.Integration.Messages.Identity;
using Identity.Application.Events;
using Joseco.Communication.External.Contracts.Services;
using Joseco.Outbox.Contracts.Model;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Identity.Application.DomainEventHandlers;

/// <summary>
/// Translates the outbox message carrying <see cref="UserUpdated"/> into the external
/// <see cref="UserChanged"/> contract and publishes it to RabbitMQ.
/// </summary>
/// <inheritdoc cref="PublishIntegrationEventWhenUserCreated"/>
public class PublishIntegrationEventWhenUserUpdated : INotificationHandler<OutboxMessage<UserUpdated>>
{
    public const string Destination = IdentityExchanges.UserUpdated;

    public const string Source = "identity";

    private readonly ILogger<PublishIntegrationEventWhenUserUpdated> _logger;
    private readonly IExternalPublisher _publisher;

    public PublishIntegrationEventWhenUserUpdated(
        ILogger<PublishIntegrationEventWhenUserUpdated> logger,
        IExternalPublisher publisher)
    {
        _logger = logger;
        _publisher = publisher;
    }

    public async Task Handle(
        OutboxMessage<UserUpdated> notification,
        CancellationToken cancellationToken = default)
    {
        var updated = notification.Content;

        var message = new UserChanged(notification.CorrelationId, Source)
        {
            UserId = updated.UserId,
            UserName = updated.UserName,
            Email = updated.Email,
            Scopes = updated.Scopes ?? Array.Empty<string>(),
            IsActive = updated.IsActive,
            ChangeKind = updated.ChangeKind
        };

        _logger.LogInformation(
            "Publishing {EventType} ({ChangeKind}) for user {UserId}: {UserName}",
            message.EventType, message.ChangeKind, message.UserId, message.UserName);

        await _publisher.PublishAsync(message, Destination, declareDestination: true);
    }
}