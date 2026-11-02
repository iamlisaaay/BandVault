using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BandVault.Web.Hubs
{
    // Координати нормовані (0..1), щоб полотно однаково виглядало на будь-якому екрані
    public record BoardSegment(string StrokeId, string Color, double Size, bool Eraser, double[][] Points);

    // ============================================
    // C18: СПІЛЬНА ДОШКА ДЛЯ МАЛЮВАННЯ ТА АВТОГРАФІВ ФАНАТІВ
    // Історія штрихів тримається в пам'яті, щоб новий учасник побачив уже намальоване
    // ============================================
    [Authorize]
    public class BoardHub : Hub
    {
        private const int MaxSegments = 20000;
        private static readonly List<BoardSegment> History = new();
        private static readonly object HistoryLock = new();
        private static readonly Dictionary<string, string> Online = new();

        public override async Task OnConnectedAsync()
        {
            lock (HistoryLock) Online[Context.ConnectionId] = DisplayName;
            await Clients.All.SendAsync("Presence", OnlineNames());
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            lock (HistoryLock) Online.Remove(Context.ConnectionId);
            await Clients.All.SendAsync("Presence", OnlineNames());
            await Clients.Others.SendAsync("CursorLeft", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        public BoardSegment[] GetHistory()
        {
            lock (HistoryLock) return History.ToArray();
        }

        public async Task Draw(BoardSegment segment)
        {
            if (segment.Points == null || segment.Points.Length == 0 || segment.Points.Length > 500) return;
            var safe = segment with { Size = Math.Clamp(segment.Size, 1, 60), Color = SanitizeColor(segment.Color) };

            lock (HistoryLock)
            {
                History.Add(safe);
                if (History.Count > MaxSegments) History.RemoveRange(0, History.Count - MaxSegments);
            }
            await Clients.Others.SendAsync("Draw", safe);
        }

        public async Task MoveCursor(double x, double y, string color)
        {
            await Clients.Others.SendAsync("Cursor", Context.ConnectionId, DisplayName, x, y, SanitizeColor(color));
        }

        public async Task Clear()
        {
            // Очищати спільну дошку може лише адміністратор
            if (Context.User?.HasClaim("IsAdmin", "true") != true) return;
            lock (HistoryLock) History.Clear();
            await Clients.All.SendAsync("Cleared", DisplayName);
        }

        private string DisplayName => Context.User?.FindFirst("DisplayName")?.Value ?? Context.User?.Identity?.Name ?? "Фанат";

        private static string[] OnlineNames()
        {
            lock (HistoryLock) return Online.Values.Distinct().ToArray();
        }

        private static string SanitizeColor(string? color) =>
            color != null && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$") ? color : "#ffffff";
    }
}
