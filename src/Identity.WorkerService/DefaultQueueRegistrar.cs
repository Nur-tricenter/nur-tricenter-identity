using Identity.Application.Contracts;
using Joseco.Communication.External.RabbitMQ.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Identity.WorkerService;

/// <summary>
/// Declares the single <c>identity.default</c> queue and binds it to every exchange in
/// <see cref="IdentityExchanges.All"/>. It exists to be the COMPLETE RECORD of everything identity
/// publishes, and for that reason it is deliberately NEVER CONSUMED.
/// </summary>
/// <remarks>
/// <para>
/// <b>Do NOT "improve" this into a consumer.</b> Registering one consumer per contract on the same
/// queue name would put COMPETING consumers on one queue: RabbitMQ delivers each message to exactly
/// one of them, so a <c>UserChanged</c> could be handed to the <c>UserPublished</c> consumer, fail
/// to deserialize, and be dead-lettered — destroying the record by the very feature meant to
/// preserve it. Per-consumer queues are created later by each consuming microservice.
/// </para>
/// <para>
/// The exchange flags are NOT a free choice: they MUST match what
/// <c>Joseco.Communication.External.RabbitMQ 1.1.0</c> declares on the publish side, because
/// RabbitMQ rejects a redeclaration with different arguments. Verified empirically against the
/// live broker: the library declares its exchanges as <c>fanout</c> + <c>durable: false</c>, so
/// declaring them durable here made every publish fail with
/// <c>406 PRECONDITION_FAILED - inequivalent arg 'durable'</c>. <b>Do not "harden" this to
/// durable without changing the publisher first.</b>
/// </para>
/// <para>
/// The QUEUE, on the other hand, is only ever declared here, so it is durable: the record must
/// outlive the worker process.
/// </para>
/// <para>
/// The connection is opened INSIDE <see cref="ExecuteAsync"/> and retried, not in the constructor:
/// a broker that is briefly down must not turn into a failed host start.
/// </para>
/// </remarks>
public sealed class DefaultQueueRegistrar : BackgroundService
{
    private const int DefaultPort = 5672;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    /// <summary>Non-durable, matching the publisher side exactly.</summary>
    private const bool ExchangesAreDurable = false;

    private const bool ExchangesAutoDelete = false;

    /// <summary>Durable: the record must outlive this worker.</summary>
    private const bool QueueIsDurable = true;

    /// <summary>
    /// Empty map rather than <c>null</c>: RabbitMQ.Client 7.x declares the parameter as
    /// non-nullable, and an empty map and <c>null</c> mean the same thing on the wire.
    /// </summary>
    private static readonly IDictionary<string, object?> NoArguments = new Dictionary<string, object?>();

    private readonly RabbitMqSettings _settings;
    private readonly ILogger<DefaultQueueRegistrar> _logger;

    public DefaultQueueRegistrar(RabbitMqSettings settings, ILogger<DefaultQueueRegistrar> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public const string QueueName = IdentityExchanges.DefaultQueue;

    public static IReadOnlyList<string> BoundExchanges => IdentityExchanges.All;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeclareTopologyAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Could not declare queue {Queue} bound to {ExchangeCount} exchange(s). Retrying in {RetryDelay}.",
                    QueueName,
                    BoundExchanges.Count,
                    RetryDelay);
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Opens one connection, declares the queue and every exchange, then binds. Idempotent: the
    /// broker treats a redeclaration with identical arguments as a no-op.
    /// </summary>
    private async Task DeclareTopologyAsync(CancellationToken cancellationToken)
    {
        // RabbitMqSettings models these as nullable, so a half-configured section must fail loudly
        // here instead of producing a connection to an empty host name.
        if (string.IsNullOrWhiteSpace(_settings.Host)
            || string.IsNullOrWhiteSpace(_settings.UserName)
            || string.IsNullOrWhiteSpace(_settings.Password)
            || string.IsNullOrWhiteSpace(_settings.VirtualHost))
        {
            throw new InvalidOperationException(
                "RabbitMqSettings must define Host, UserName, Password and VirtualHost before the " +
                $"'{QueueName}' queue can be declared.");
        }

        var factory = new ConnectionFactory
        {
            HostName = _settings.Host,
            Port = _settings.Port > 0 ? _settings.Port : DefaultPort,
            UserName = _settings.UserName,
            Password = _settings.Password,
            VirtualHost = _settings.VirtualHost
        };

        await using var connection = await factory.CreateConnectionAsync("identity-default-queue", cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            QueueName,
            durable: QueueIsDurable,
            exclusive: false,
            autoDelete: false,
            arguments: NoArguments,
            cancellationToken: cancellationToken);

        foreach (var exchange in BoundExchanges)
        {
            // fanout + non-durable mirror the publisher side exactly; any mismatch is a 406 on the
            // next publish.
            await channel.ExchangeDeclareAsync(
                exchange,
                ExchangeType.Fanout,
                durable: ExchangesAreDurable,
                autoDelete: ExchangesAutoDelete,
                arguments: NoArguments,
                cancellationToken: cancellationToken);

            // Empty routing key: a fanout ignores it, but RabbitMQ requires it non-null.
            await channel.QueueBindAsync(
                QueueName,
                exchange,
                routingKey: string.Empty,
                arguments: NoArguments,
                cancellationToken: cancellationToken);
        }

        // No BasicConsume/BasicGet here, and no consumer ever will be. Acking removes a message,
        // and this queue's whole purpose is to keep every published message.
        _logger.LogInformation(
            "Queue {Queue} is declared and bound to {ExchangeCount} exchange(s): {Exchanges}.",
            QueueName,
            BoundExchanges.Count,
            string.Join(", ", BoundExchanges));
    }
}