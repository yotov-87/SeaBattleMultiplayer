using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SeaBattleMultiplayer.Backend.Data;
using SeaBattleMultiplayer.Backend.Services;

namespace SeaBattleMultiplayer.Backend.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly OnlineUsersService _onlineUsers;
    private readonly AppDbContext _db;

    public GameHub(OnlineUsersService onlineUsers, AppDbContext db)
    {
        _onlineUsers = onlineUsers;
        _db = db;
    }

    private int GetUserId() =>
        int.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string GetUsername() =>
        Context.User!.FindFirstValue(ClaimTypes.Name)!;

    // ── Connection lifecycle ───────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var username = GetUsername();

        _onlineUsers.AddUser(userId, Context.ConnectionId);
        await Clients.Others.SendAsync("UserOnline", userId, username);
        await Clients.Caller.SendAsync("OnlineUsers", _onlineUsers.GetOnlineUserIds());

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        var username = GetUsername();

        // Leave room on disconnect
        var roomId = _onlineUsers.RemoveFromRoom(userId);
        if (roomId is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room-{roomId}");
            await Clients.Group($"room-{roomId}").SendAsync("UserLeftLobby", userId, username);
        }

        _onlineUsers.RemoveUser(userId);
        await Clients.Others.SendAsync("UserOffline", userId);

        await base.OnDisconnectedAsync(exception);
    }

    // ── Presence ───────────────────────────────────────────────────────────

    public async Task SendGameInvite(int targetUserId, string roomId)
    {
        var senderId = GetUserId();
        var senderUsername = GetUsername();

        var connectionId = _onlineUsers.GetConnectionId(targetUserId);
        if (connectionId is not null)
        {
            await Clients.Client(connectionId)
                .SendAsync("GameInviteReceived", senderId, senderUsername, roomId);
        }
    }

    // ── Lobby ──────────────────────────────────────────────────────────────

    public async Task CreateGame()
    {
        var userId = GetUserId();
        var username = GetUsername();

        // Leave any existing room first
        var existingRoom = _onlineUsers.RemoveFromRoom(userId);
        if (existingRoom is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room-{existingRoom}");
            await Clients.Group($"room-{existingRoom}").SendAsync("UserLeftLobby", userId, username);
        }

        var roomId = _onlineUsers.CreateRoom(userId, username);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"room-{roomId}");

        var members = _onlineUsers.GetRoomMembers(roomId)
            .Select(m => new { id = m.Id, username = m.Username })
            .ToList();

        await Clients.Caller.SendAsync("RoomCreated", roomId);
        await Clients.Caller.SendAsync("LobbyState", members);
    }

    public async Task AcceptInvite(string roomId)
    {
        var userId = GetUserId();
        var username = GetUsername();

        // Leave old room if any
        var existingRoom = _onlineUsers.RemoveFromRoom(userId);
        if (existingRoom is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room-{existingRoom}");
            await Clients.Group($"room-{existingRoom}").SendAsync("UserLeftLobby", userId, username);
        }

        if (!_onlineUsers.AddToRoom(roomId, userId, username))
        {
            await Clients.Caller.SendAsync("InviteError", "Room no longer exists.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"room-{roomId}");

        // Notify everyone already in the room
        await Clients.Group($"room-{roomId}").SendAsync("UserJoinedLobby", userId, username);

        // Send full current member list to the new joiner
        var members = _onlineUsers.GetRoomMembers(roomId)
            .Select(m => new { id = m.Id, username = m.Username })
            .ToList();
        await Clients.Caller.SendAsync("LobbyState", members);
        await Clients.Caller.SendAsync("RoomJoined", roomId);
    }

    public async Task DeclineInvite(int hostUserId)
    {
        var username = GetUsername();
        var connectionId = _onlineUsers.GetConnectionId(hostUserId);
        if (connectionId is not null)
        {
            await Clients.Client(connectionId).SendAsync("InviteDeclined", username);
        }
    }

    public async Task SendLobbyChat(string roomId, string message)
    {
        var userId = GetUserId();
        var username = GetUsername();

        if (_onlineUsers.GetUserRoom(userId) != roomId) return;

        await Clients.Group($"room-{roomId}")
            .SendAsync("LobbyChatMessage", userId, username, message, DateTime.UtcNow);
    }

    public async Task LeaveLobby(string roomId)
    {
        var userId = GetUserId();
        var username = GetUsername();

        _onlineUsers.RemoveFromRoom(userId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room-{roomId}");
        await Clients.Group($"room-{roomId}").SendAsync("UserLeftLobby", userId, username);
    }
}
