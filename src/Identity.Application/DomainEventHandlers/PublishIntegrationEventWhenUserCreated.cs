using Identity.Application.Contracts;
using Identity.Application.Contracts.Integration;
using Identity.Application.Events;
using Joseco.Communication.External.Contracts.Services;
using Joseco.Outbox.Contracts.Model;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Identity.Application.DomainEventHandlers;

/// <summary>
/// Translates the outbox message carrying <see cref="UserCreated"/> into the external
/// <see cref="UserPublished"/> contract and publishes it to RabbitMQ.
/// </summary>
/// <remarks>
/// Typed against <c>OutboxMessage&lt;UserCreated&gt;</c> rather than <c>DomainEvent</c>: the outbox
/// processor dispatches through MediatR with the RUNTIME event type, so a handler registered
/// against the base class would never match a specific delivery. Per-event handlers are what
/// MediatR can resolve.
/// </remarks>
public class PublishIntegrationEventWhenUserCreated : INotificationHandler<OutboxMessage<UserCreated>>
{
    /// <summary>
    /// MUST be passed explicitly: the library derives its default destination from
    /// <c>typeof(T)</c>, which for a runtime type that is not the compile-time generic argument
    /// silently routes to the wrong name.
    /// </summary>
    public const string Destination = IdentityExchanges.UserPublished;

    /// <summary>Logical producer name stamped into every published contract.</summary>
    public const string Source = "identity";

    private readonly ILogger<PublishIntegrationEventWhenUserCreated> _logger;
    private readonly IExternalPublisher _publisher;

    public PublishIntegrationEventWhenUserCreated(
        ILogger<PublishIntegrationEventWhenUserCreated> logger,
        IExternalPublisher publisher)
    {
        _logger = logger;
        _publisher = publisher;
    }

    public async Task Handle(
        OutboxMessage<UserCreated> notification,
        CancellationToken cancellationToken = default)
    {
        var created = notification.Content;

        var message = new UserPublished(notification.CorrelationId, Source)
        {
            UserId = created.UserId,
            UserName = created.UserName,
            Email = created.Email,
            IsAdministrator = created.IsAdministrator,
            Scopes = created.Scopes ?? Array.Empty<string>()
        };

        _logger.LogInformation(
            "Publishing {EventType} for user {UserId}: {UserName}",
            message.EventType, message.UserId, message.UserName);

        // declareDestination MUST be true: the publish is mandatory, so an undeclared exchange
        // makes the call fail loudly instead of silently dropping the event.
        await _publisher.PublishAsync(message, Destination, declareDestination: true);
    }
}