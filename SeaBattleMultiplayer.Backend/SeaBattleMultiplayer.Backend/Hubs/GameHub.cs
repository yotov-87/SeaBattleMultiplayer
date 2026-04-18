using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SeaBattleMultiplayer.Backend.Data;
using SeaBattleMultiplayer.Backend.DTOs;
using SeaBattleMultiplayer.Backend.Services;

namespace SeaBattleMultiplayer.Backend.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly OnlineUsersService _onlineUsers;
    private readonly GameStateService _gameState;
    private readonly AppDbContext _db;
    private readonly IHubContext<GameHub> _hubContext;

    public GameHub(OnlineUsersService onlineUsers, GameStateService gameState, AppDbContext db, IHubContext<GameHub> hubContext)
    {
        _onlineUsers = onlineUsers;
        _gameState = gameState;
        _db = db;
        _hubContext = hubContext;
    }

    private int GetUserId() =>
        int.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string GetUsername() =>
        Context.User!.FindFirstValue(ClaimTypes.Name)!;

    // в”Ђв”Ђ Connection lifecycle в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var username = GetUsername();

        _onlineUsers.CancelDisconnectGrace(userId);
        _onlineUsers.AddUser(userId, Context.ConnectionId);
        await Clients.Others.SendAsync("UserOnline", userId, username);
        await Clients.Caller.SendAsync("OnlineUsers", _onlineUsers.GetOnlineUserIds());

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        var username = GetUsername();

        var roomId = _onlineUsers.RemoveFromRoom(userId);
        if (roomId is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room-{roomId}");
            await Clients.Group($"room-{roomId}").SendAsync("UserLeftLobby", userId, username);
        }

        _onlineUsers.RemoveUser(userId);

        var cts = _onlineUsers.StartDisconnectGrace(userId);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000, cts.Token);
                await _hubContext.Clients.All.SendAsync("UserOffline", userId);
            }
            catch (OperationCanceledException) { }
        });

        await base.OnDisconnectedAsync(exception);
    }

    // в”Ђв”Ђ Presence в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    public async Task SendGameInvite(int targetUserId, string roomId)
    {
        var senderId = GetUserId();
        var senderUsername = GetUsername();
        var connectionId = _onlineUsers.GetConnectionId(targetUserId);
        if (connectionId is not null)
            await Clients.Client(connectionId).SendAsync("GameInviteReceived", senderId, senderUsername, roomId);
    }

    // в”Ђв”Ђ Lobby в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    public async Task CreateGame()
    {
        var userId = GetUserId();
        var username = GetUsername();

        var existingRoom = _onlineUsers.RemoveFromRoom(userId);
        if (existingRoom is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room-{existingRoom}");
            await Clients.Group($"room-{existingRoom}").SendAsync("UserLeftLobby", userId, username);
        }

        var roomId = _onlineUsers.CreateRoom(userId, username);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"room-{roomId}");

        var members = _onlineUsers.GetRoomMembers(roomId).Select(m => new { id = m.Id, username = m.Username }).ToList();
        await Clients.Caller.SendAsync("RoomCreated", roomId);
        await Clients.Caller.SendAsync("LobbyState", members);
    }

    public async Task AcceptInvite(string roomId)
    {
        var userId = GetUserId();
        var username = GetUsername();

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
        await Clients.Group($"room-{roomId}").SendAsync("UserJoinedLobby", userId, username);

        var members = _onlineUsers.GetRoomMembers(roomId).Select(m => new { id = m.Id, username = m.Username }).ToList();
        await Clients.Caller.SendAsync("LobbyState", members);
        await Clients.Caller.SendAsync("RoomJoined", roomId);
    }

    public async Task DeclineInvite(int hostUserId)
    {
        var username = GetUsername();
        var connectionId = _onlineUsers.GetConnectionId(hostUserId);
        if (connectionId is not null)
            await Clients.Client(connectionId).SendAsync("InviteDeclined", username);
    }

    public async Task SendLobbyChat(string roomId, string message)
    {
        var userId = GetUserId();
        var username = GetUsername();
        if (_onlineUsers.GetUserRoom(userId) != roomId) return;
        await Clients.Group($"room-{roomId}").SendAsync("LobbyChatMessage", userId, username, message, DateTime.UtcNow);
    }

    public async Task LeaveLobby(string roomId)
    {
        var userId = GetUserId();
        var username = GetUsername();
        _onlineUsers.RemoveFromRoom(userId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room-{roomId}");
        await Clients.Group($"room-{roomId}").SendAsync("UserLeftLobby", userId, username);
    }

    // в”Ђв”Ђ Placement phase в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    public async Task StartGame()
    {
        var userId = GetUserId();
        var roomId = _onlineUsers.GetUserRoom(userId);
        if (roomId is null || _onlineUsers.GetRoomHost(roomId) != userId) return;

        // Init game state now so fleets can be stored during placement
        var members = _onlineUsers.GetRoomMembers(roomId).ToDictionary(m => m.Id, m => m.Username);
        _gameState.InitGame(roomId, members);

        var cts = _onlineUsers.InitPlacementTimer(roomId);
        await Clients.Group($"room-{roomId}").SendAsync("GameStarted");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(30_000, cts.Token);
                await ForceBattleStart(roomId);
            }
            catch (OperationCanceledException) { }
        });
    }

    public async Task PlayerReady(List<ShipDto> ships)
    {
        var userId = GetUserId();
        var roomId = _onlineUsers.GetUserRoom(userId);
        if (roomId is null) return;

        _gameState.SubmitFleet(roomId, userId, ships);

        bool allReady = _onlineUsers.MarkReady(roomId, userId);
        if (allReady)
        {
            _onlineUsers.CancelPlacementTimer(roomId);
            await BeginBattle(roomId);
        }
    }

    // Called either when all submit fleets OR when the 30s timer fires
    private async Task ForceBattleStart(string roomId)
    {
        // Assign empty fleet to anyone who disconnected or didn't submit
        var state = _gameState.GetState(roomId);
        if (state is null) return;
        foreach (var id in state.PlayerNames.Keys)
        {
            if (!state.Fleets.ContainsKey(id))
                _gameState.SubmitFleet(roomId, id, []);
        }
        await BeginBattle(roomId);
    }

    private async Task BeginBattle(string roomId)
    {
        await _hubContext.Clients.Group($"room-{roomId}").SendAsync("AllReady");

        var turn = _gameState.GetCurrentTurn(roomId);
        if (turn is null) return;

        await _hubContext.Clients.Group($"room-{roomId}").SendAsync("TurnStarted", turn.PlayerId, turn.Username);
        StartTurnTimer(roomId, turn.PlayerId, turn.Username);
    }

    // в”Ђв”Ђ Battle phase в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    public async Task FireShot(int targetId, int row, int col)
    {
        var userId = GetUserId();
        var roomId = _onlineUsers.GetUserRoom(userId);
        if (roomId is null) return;

        // Only the current turn player can fire
        var turn = _gameState.GetCurrentTurn(roomId);
        if (turn?.PlayerId != userId) return;

        _gameState.CancelTurnTimer(roomId);
        await ProcessAndBroadcastShot(roomId, userId, targetId, row, col, useHubContext: false);
    }

    public async Task SendBattleChat(string roomId, string message)
    {
        var userId = GetUserId();
        var username = GetUsername();
        if (_onlineUsers.GetUserRoom(userId) != roomId) return;
        await Clients.Group($"room-{roomId}").SendAsync("BattleChatMessage", userId, username, message, DateTime.UtcNow);
    }

    // в”Ђв”Ђ Shared helpers в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    private async Task ProcessAndBroadcastShot(string roomId, int shooterId, int targetId, int row, int col, bool useHubContext)
    {
        var (result, eliminated, gameOver, winnerId) = _gameState.ProcessShot(roomId, shooterId, targetId, row, col);
        if (result is null) return;

        await Send($"room-{roomId}", "ShotFired", result, useHubContext);

        if (eliminated)
            await Send($"room-{roomId}", "PlayerEliminated", targetId, useHubContext);

        if (gameOver && winnerId.HasValue)
        {
            var winnerName = _gameState.GetState(roomId)?.PlayerNames.GetValueOrDefault(winnerId.Value, "?") ?? "?";
            await Send($"room-{roomId}", "BattleOver", new { winnerId = winnerId.Value, winnerUsername = winnerName }, useHubContext);
            return;
        }

        var nextTurn = _gameState.AdvanceTurn(roomId);
        if (nextTurn is null) return;

        await Send($"room-{roomId}", "TurnStarted", new { playerId = nextTurn.PlayerId, username = nextTurn.Username }, useHubContext);
        StartTurnTimer(roomId, nextTurn.PlayerId, nextTurn.Username);
    }

    private void StartTurnTimer(string roomId, int currentPlayerId, string currentPlayerUsername)
    {
        var cts = _gameState.SetTurnTimer(roomId);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(15_000, cts.Token);
                var randomShot = _gameState.GetRandomShot(roomId, currentPlayerId);
                if (randomShot is null) return;
                var (targetId, row, col) = randomShot.Value;
                await ProcessAndBroadcastShot(roomId, currentPlayerId, targetId, row, col, useHubContext: true);
            }
            catch (OperationCanceledException) { }
        });
    }

    /// <summary>Sends a hub event to a group using either the hub context (background) or the hub clients (request).</summary>
    private Task Send(string group, string method, object? arg, bool useHubContext) =>
        useHubContext
            ? _hubContext.Clients.Group(group).SendAsync(method, arg)
            : Clients.Group(group).SendAsync(method, arg);
}

