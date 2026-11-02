using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BandVault.Web.Hubs
{
    // ============================================
    // C13/C14: СИНХРОНІЗАЦІЯ РЕДАКТОРА ПОСТІВ МІЖ ПРИСТРОЯМИ
    // Кожен документ - окрема група ("post-new" або "post-{id}").
    // Сервер лише ретранслює стан документа та присутність (хто який блок редагує).
    // ============================================
    [Authorize]
    public class EditorHub : Hub
    {
        private static readonly ConcurrentDictionary<string, string> ConnectionDocs = new();

        public async Task Join(string docId)
        {
            if (!IsAdmin || !IsValidDocId(docId)) throw new HubException("Немає доступу до редактора");

            ConnectionDocs[Context.ConnectionId] = docId;
            await Groups.AddToGroupAsync(Context.ConnectionId, docId);
            // Інші учасники надішлють новачку актуальний стан документа
            await Clients.OthersInGroup(docId).SendAsync("PeerJoined", Context.ConnectionId, DisplayName);
        }

        public async Task Update(string docId, JsonElement state)
        {
            if (!IsMember(docId)) return;
            await Clients.OthersInGroup(docId).SendAsync("State", Context.ConnectionId, DisplayName, state);
        }

        public async Task SendStateTo(string connectionId, JsonElement state)
        {
            if (!ConnectionDocs.TryGetValue(Context.ConnectionId, out var docId)) return;
            if (!ConnectionDocs.TryGetValue(connectionId, out var targetDoc) || targetDoc != docId) return;
            await Clients.Client(connectionId).SendAsync("InitialState", Context.ConnectionId, DisplayName, state);
        }

        public async Task Focus(string docId, string? blockId)
        {
            if (!IsMember(docId)) return;
            await Clients.OthersInGroup(docId).SendAsync("PeerFocus", Context.ConnectionId, DisplayName, blockId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (ConnectionDocs.TryRemove(Context.ConnectionId, out var docId))
            {
                await Clients.OthersInGroup(docId).SendAsync("PeerLeft", Context.ConnectionId);
            }
            await base.OnDisconnectedAsync(exception);
        }

        private bool IsAdmin => Context.User?.HasClaim("IsAdmin", "true") == true;

        private bool IsMember(string docId) =>
            ConnectionDocs.TryGetValue(Context.ConnectionId, out var joined) && joined == docId;

        private static bool IsValidDocId(string docId) =>
            docId == "post-new" || (docId.StartsWith("post-") && int.TryParse(docId[5..], out _));

        private string DisplayName => Context.User?.FindFirst("DisplayName")?.Value ?? Context.User?.Identity?.Name ?? "Редактор";
    }
}
