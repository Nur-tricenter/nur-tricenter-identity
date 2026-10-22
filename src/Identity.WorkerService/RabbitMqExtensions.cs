using Joseco.Communication.External.RabbitMQ;
using Joseco.Communication.External.RabbitMQ.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.WorkerService;

public static class RabbitMqExtensions
{
    /// <summary>
    /// Binds <see cref="RabbitMqSettings"/> from the <c>RabbitMqSettings</c> configuration section
    /// and registers the RabbitMQ publisher infrastructure.
    /// </summary>
    /// <remarks>
    /// This service is a PURE PRODUCER: no consumer is registered, deliberately.
    /// <c>AddRabbitMqConsumer&lt;TMessage, THandler&gt;</c> spins up a hosted service that connects
    /// at startup and subscribes to a queue. Identity has no inbound contract, so registering one
    /// would make RabbitMQ a hard boot dependency for a queue that does not exist and would break
    /// host startup whenever the broker is unreachable.
    /// <para>
    /// The settings instance MUST be registered before <c>AddRabbitMQ</c>: the library uses
    /// <c>TryAddSingleton</c>, so a later registration is silently ignored.
    /// </para>
    /// <para>
    /// NOTE: <see cref="RabbitMqSettings.Port"/> and <see cref="RabbitMqSettings.UseSsl"/> are
    /// silently ignored by Joseco.Communication.External.RabbitMQ 1.1.0 — both the publisher and
    /// the consumer build their own <c>ConnectionFactory</c> from HostName/UserName/Password only.
    /// The broker MUST therefore be reachable on 5672.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddIdentityRabbitMq(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration
            .GetSection("RabbitMqSettings")
            .Get<RabbitMqSettings>();

        if (settings is null)
        {
            throw new InvalidOperationException(
                "Configuration section 'RabbitMqSettings' is missing or empty.");
        }

        // Must precede AddRabbitMQ (TryAddSingleton).
        services.AddSingleton(settings);

        return services.AddRabbitMQ(settings);
    }
}