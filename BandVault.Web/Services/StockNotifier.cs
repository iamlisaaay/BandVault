using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BandVault.Web.Data;
using BandVault.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web.Services
{
    public record StockItemDto(int Id, string Title, int Stock);

    // Version росте при кожній зміні залишків, ChangedAt (unix ms) - для виміру затримки доставки на клієнті
    public record StockSnapshot(long Version, long ChangedAt, IReadOnlyList<StockItemDto> Items);

    // ============================================
    // C10/C11: ЄДИНЕ ДЖЕРЕЛО ЗМІН ЗАЛИШКІВ МЕРЧУ
    // Роздає зміни через WebSocket, Long Poll, SignalR; для Polls віддає знімок
    // ============================================
    public class StockNotifier
    {
        public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<StockHub> _hub;
        private readonly ILogger<StockNotifier> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();

        private StockSnapshot? _current;
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StockNotifier(IServiceScopeFactory scopeFactory, IHubContext<StockHub> hub, ILogger<StockNotifier> logger)
        {
            _scopeFactory = scopeFactory;
            _hub = hub;
            _logger = logger;
        }

        public int WebSocketClients => _sockets.Count;

        public async Task<StockSnapshot> GetAsync()
        {
            if (_current == null) await RefreshAsync();
            return _current!;
        }

        // Викликається після будь-якої зміни мерчу (замовлення, CRUD). Якщо залишки не змінились - нічого не розсилаємо
        public async Task RefreshAsync()
        {
            StockSnapshot snapshot;
            TaskCompletionSource previousSignal;

            await _lock.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var items = await context.MerchItems
                    .OrderBy(m => m.Id)
                    .Select(m => new StockItemDto(m.Id, m.Title, m.StockQuantity ?? 0))
                    .ToListAsync();

                if (_current != null && _current.Items.SequenceEqual(items)) return;

                snapshot = new StockSnapshot((_current?.Version ?? 0) + 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), items);
                _current = snapshot;
                previousSignal = _changed;
                _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            finally
            {
                _lock.Release();
            }

            // Будимо всі Long Poll запити, що чекають
            previousSignal.TrySetResult();

            if (snapshot.Version == 1) return; // перше завантаження - не зміна

            await BroadcastWebSocketsAsync(snapshot);
            try
            {
                await _hub.Clients.All.SendAsync("StockUpdated", snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не вдалося розіслати оновлення залишків через SignalR");
            }
        }

        // LONG POLL: тримаємо запит, доки не з'явиться версія новіша за since або не мине timeout
        public async Task<StockSnapshot?> WaitForChangeAsync(long since, TimeSpan timeout, CancellationToken ct)
        {
            var snapshot = await GetAsync();
            if (snapshot.Version > since) return snapshot;

            var signal = _changed.Task;
            var completed = await Task.WhenAny(signal, Task.Delay(timeout, ct));
            if (completed != signal) return null;
            return _current;
        }

        // WEBSOCKET: реєструємо з'єднання, одразу надсилаємо поточний знімок і тримаємо до закриття клієнтом
        public async Task HandleWebSocketAsync(WebSocket socket, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            _sockets[id] = socket;
            try
            {
                await SendAsync(socket, await GetAsync(), ct);

                var buffer = new byte[256];
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            finally
            {
                _sockets.TryRemove(id, out _);
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
                }
            }
        }

        private async Task BroadcastWebSocketsAsync(StockSnapshot snapshot)
        {
            foreach (var (id, socket) in _sockets)
            {
                if (socket.State != WebSocketState.Open)
                {
                    _sockets.TryRemove(id, out _);
                    continue;
                }
                try
                {
                    await SendAsync(socket, snapshot, CancellationToken.None);
                }
                catch
                {
                    _sockets.TryRemove(id, out _);
                }
            }
        }

        private static Task SendAsync(WebSocket socket, StockSnapshot snapshot, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, JsonOptions));
            return socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
    }
}
