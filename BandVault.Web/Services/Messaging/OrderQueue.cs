using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BandVault.Web.Services.Messaging
{
    public record OrderPlacedMessage(int OrderId, DateTime PlacedAt);

    // ============================================
    // B8: ЧЕРГА ПОВІДОМЛЕНЬ ДЛЯ АСИНХРОННОЇ ОБРОБКИ ЗАМОВЛЕНЬ (RabbitMQ)
    // Якщо брокер недоступний (локальний запуск без Docker) - працює in-memory Channel,
    // щоб оформлення замовлення не падало.
    // ============================================
    public class OrderQueue : IAsyncDisposable
    {
        public const string QueueName = "bandvault.order-placed";

        private readonly string? _connectionString;
        private readonly ILogger<OrderQueue> _logger;
        private readonly Channel<OrderPlacedMessage> _fallback = Channel.CreateUnbounded<OrderPlacedMessage>();
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        private IConnection? _connection;
        private IChannel? _publishChannel;
        private DateTime _nextRetry = DateTime.MinValue;

        public OrderQueue(IConfiguration configuration, ILogger<OrderQueue> logger)
        {
            _connectionString = configuration.GetConnectionString("RabbitMQ");
            _logger = logger;
        }

        public bool UsesRabbitMq => _connection is { IsOpen: true };

        public async Task PublishAsync(OrderPlacedMessage message)
        {
            var channel = await GetPublishChannelAsync();
            if (channel == null)
            {
                await _fallback.Writer.WriteAsync(message);
                return;
            }

            try
            {
                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
                var props = new BasicProperties { Persistent = true, ContentType = "application/json" };
                await channel.BasicPublishAsync(exchange: "", routingKey: QueueName, mandatory: false, basicProperties: props, body: body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Публікація в RabbitMQ не вдалася, замовлення #{OrderId} піде у локальну чергу", message.OrderId);
                await _fallback.Writer.WriteAsync(message);
            }
        }

        // Споживач: підписка на RabbitMQ (якщо доступний) + читання локальної черги
        public async Task ConsumeAsync(Func<OrderPlacedMessage, Task> handler, CancellationToken ct)
        {
            IChannel? consumerChannel = null;
            // У docker-compose брокер стартує повільніше за застосунок - даємо йому до ~30 с
            IConnection? connection = null;
            for (int attempt = 0; attempt < 6 && connection == null && !string.IsNullOrWhiteSpace(_connectionString); attempt++)
            {
                _nextRetry = DateTime.MinValue;
                connection = await GetConnectionAsync();
                if (connection == null) await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            if (connection != null)
            {
                consumerChannel = await connection.CreateChannelAsync(cancellationToken: ct);
                await consumerChannel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
                // По одному повідомленню на обробника - важка генерація PDF не блокує інші інстанси
                await consumerChannel.BasicQosAsync(0, 1, false, ct);

                var consumer = new AsyncEventingBasicConsumer(consumerChannel);
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<OrderPlacedMessage>(ea.Body.Span);
                        if (message != null) await handler(message);
                        await consumerChannel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Помилка обробки повідомлення з RabbitMQ");
                        // Не повертаємо в чергу назавжди: requeue лише при першій спробі
                        await consumerChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: !ea.Redelivered);
                    }
                };
                await consumerChannel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: ct);
                _logger.LogInformation("Споживач RabbitMQ підписаний на {Queue}", QueueName);
            }

            try
            {
                await foreach (var message in _fallback.Reader.ReadAllAsync(ct))
                {
                    try { await handler(message); }
                    catch (Exception ex) { _logger.LogError(ex, "Помилка обробки замовлення #{OrderId} з локальної черги", message.OrderId); }
                }
            }
            finally
            {
                if (consumerChannel != null) await consumerChannel.DisposeAsync();
            }
        }

        private async Task<IChannel?> GetPublishChannelAsync()
        {
            if (_publishChannel is { IsOpen: true }) return _publishChannel;
            var connection = await GetConnectionAsync();
            if (connection == null) return null;

            await _connectLock.WaitAsync();
            try
            {
                if (_publishChannel is { IsOpen: true }) return _publishChannel;
                _publishChannel = await connection.CreateChannelAsync();
                await _publishChannel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false);
                return _publishChannel;
            }
            finally
            {
                _connectLock.Release();
            }
        }

        private async Task<IConnection?> GetConnectionAsync()
        {
            if (_connection is { IsOpen: true }) return _connection;
            if (string.IsNullOrWhiteSpace(_connectionString) || DateTime.UtcNow < _nextRetry) return null;

            await _connectLock.WaitAsync();
            try
            {
                if (_connection is { IsOpen: true }) return _connection;
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(_connectionString),
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(3),
                    AutomaticRecoveryEnabled = true
                };
                _connection = await factory.CreateConnectionAsync("BandVault.Web");
                return _connection;
            }
            catch (Exception ex)
            {
                _nextRetry = DateTime.UtcNow.AddMinutes(1);
                _logger.LogWarning("RabbitMQ недоступний ({Message}). Використовується локальна черга.", ex.Message);
                return null;
            }
            finally
            {
                _connectLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_publishChannel != null) await _publishChannel.DisposeAsync();
            if (_connection != null) await _connection.DisposeAsync();
        }
    }
}
