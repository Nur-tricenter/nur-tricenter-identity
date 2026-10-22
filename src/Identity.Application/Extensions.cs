using Identity.Application.DomainEventHandlers;
using Identity.Application.Events;
using Joseco.Outbox.Contracts.Model;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Identity.Application;

/// <summary>
/// Composition root for the application layer.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Registers the application layer and the MediatR pipeline.
    /// </summary>
    /// <remarks>
    /// MediatR's assembly scan also picks up the outbox-publishing handlers, but they are REMOVED
    /// here on purpose. They depend on <c>IExternalPublisher</c>, which is bound only by the
    /// WorkerService — the single host that runs <c>OutboxProcessor&lt;DomainEvent&gt;</c> and
    /// therefore the only host that ever dispatches <c>OutboxMessage&lt;…&gt;</c>. Leaving them
    /// registered in the WebApi would make DI validation fail there for handlers that can never
    /// run. Hosts that own the outbox processor call <see cref="AddIntegrationMessaging"/> instead.
    /// <para>
    /// Each is removed against the CONCRETE event type, because the outbox processor dispatches
    /// messages with their runtime type (<c>OutboxMessage&lt;UserCreated&gt;</c>), not the base
    /// <c>OutboxMessage&lt;DomainEvent&gt;</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(Extensions).Assembly));

        // See the remarks above: the outbox publishers belong to the WorkerService host only.
        services.RemoveAll<INotificationHandler<OutboxMessage<UserCreated>>>();
        services.RemoveAll<INotificationHandler<OutboxMessage<UserUpdated>>>();

        return services;
    }

    /// <summary>
    /// Registers the outbox-to-broker publishing handlers. Call this ONLY from a host that runs
    /// the outbox processor and that has already registered an
    /// <c>IExternalPublisher</c>.
    /// </summary>
    public static IServiceCollection AddIntegrationMessaging(this IServiceCollection services)
    {
        services.AddTransient<
            INotificationHandler<OutboxMessage<UserCreated>>,
            PublishIntegrationEventWhenUserCreated>();

        services.AddTransient<
            INotificationHandler<OutboxMessage<UserUpdated>>,
            PublishIntegrationEventWhenUserUpdated>();

        return services;
    }
}