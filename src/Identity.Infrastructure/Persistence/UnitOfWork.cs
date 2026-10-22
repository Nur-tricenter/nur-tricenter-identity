using Identity.Infrastructure.Persistence;
using Joseco.DDD.Core.Abstractions;
using Joseco.Outbox.Contracts.Model;
using Joseco.Outbox.EFCore.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// The transaction boundary: drains domain events to the outbox, then saves everything at once.
/// </summary>
/// <remarks>
/// Implements <see cref="IOutboxDatabase{TEvent}"/> as well as <see cref="IUnitOfWork"/>, so both
/// abstractions hand back the SAME <see cref="IdentityDbContext"/>. That shared context is the
/// whole point: the aggregate change and its outbox row go into one transaction, so an account can
/// never be committed without its event, nor its event without the account.
/// </remarks>
public class UnitOfWork : IUnitOfWork, IOutboxDatabase<DomainEvent>
{
    /// <summary>
    /// Ceiling on drain rounds. A handler that raises a new domain event is legal and handled by
    /// looping; one that raises events forever is a cycle, and this turns that into a loud failure
    /// instead of a hang.
    /// </summary>
    private const int MaxDrainRounds = 10;

    private readonly IdentityDbContext _context;
    private readonly IMediator _mediator;

    private bool _isCommitting;

    public UnitOfWork(IdentityDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public DbSet<OutboxMessage<DomainEvent>> GetOutboxMessages() => _context.OutboxMessages;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        // Re-entrancy guard: an outbox publisher that commits would otherwise recurse. Silently
        // returning is correct because the outer call is already doing this exact save.
        if (_isCommitting)
        {
            return;
        }

        _isCommitting = true;

        try
        {
            await DrainDomainEventsAsync(cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Detach everything rather than leaving half-built entities tracked. A caller that
            // retries on the same scoped context would otherwise re-save entities the failed
            // attempt left in a half-inserted state.
            _context.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            _isCommitting = false;
        }
    }

    private async Task DrainDomainEventsAsync(CancellationToken cancellationToken)
    {
        for (var round = 0; round < MaxDrainRounds; round++)
        {
            var domainEvents = CollectDomainEvents();

            if (domainEvents.Length == 0)
            {
                return;
            }

            foreach (var domainEvent in domainEvents)
            {
                await _mediator.Publish(domainEvent, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Domain events kept being raised after {MaxDrainRounds} drain rounds, " +
            "which indicates an event cycle.");
    }

    /// <summary>
    /// Takes every entity's pending events and clears them, so a retry does not re-raise an event
    /// that a previous attempt already staged.
    /// </summary>
    private DomainEvent[] CollectDomainEvents()
    {
        var collected = new List<DomainEvent>();

        foreach (var entity in _context.ChangeTracker
                     .Entries<Entity>()
                     .Select(entry => entry.Entity))
        {
            if (!entity.DomainEvents.Any())
            {
                continue;
            }

            collected.AddRange(entity.DomainEvents);
            entity.ClearDomainEvents();
        }

        return collected.ToArray();
    }
}