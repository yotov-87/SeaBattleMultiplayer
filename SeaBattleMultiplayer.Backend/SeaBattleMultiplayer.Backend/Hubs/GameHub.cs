using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SeaBattleMultiplayer.Backend.Data;
using SeaBattleMultiplayer.Backend.DTOs;
using SeaBattleMultiplayer.Backend.Models;
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

    // ── Connection lifecycle ───────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var username = GetUsername();

        _onlineUsers.CancelDisconnectGrace(userId);
        _onlineUsers.AddUser(userId, Context.ConnectionId);
        await Clients.Others.SendAsync("UserOnline", userId, username);
        await Clients.Caller.SendAsync("OnlineUsers", _onlineUsers.GetOnlineUserIds());

        // If the player is in an active game (e.g. after browser refresh), rejoin the group
        // and send them the full game state to restore the board
        var activeRoomId = _gameState.GetActiveGameRoomForUser(userId);
        if (activeRoomId is not null)
        {
            // Restore room membership in SignalR group
            _onlineUsers.AddToRoom(activeRoomId, userId, username);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"room-{activeRoomId}");

            var state = _gameState.GetState(activeRoomId)!;

            // Rebuild the lobby member list from the game state
            var members = state.PlayerNames.Select(kv => new { id = kv.Key, username = kv.Value }).ToList();
            await Clients.Caller.SendAsync("RoomJoined", activeRoomId);
            await Clients.Caller.SendAsync("LobbyState", members);

            // Replay all moves so the frontend can reconstruct both boards
            // Row/Col are sea-absolute; sunkCells are also sea-absolute.
            var myMoves = state.AllMoves
                .Where(m => m.ShooterId == userId)
                .Select(m =>
                {
                    List<CellDto>? sunkCells = null;
                    if (m.Result == "sunk")
                    {
                        var fleet  = state.Fleets.GetValueOrDefault(m.TargetId);
                        var offset = state.FleetOffsets.GetValueOrDefault(m.TargetId);
                        if (fleet is not null)
                        {
                            int lRow = m.Row - offset.Row;
                            int lCol = m.Col - offset.Col;
                            var ship = fleet.Ships.FirstOrDefault(s => s.HitCells.Contains(new Cell(lRow, lCol)));
                            sunkCells = ship?.Cells
                                .Select(c => new CellDto(c.Row + offset.Row, c.Col + offset.Col))
                                .ToList();
                        }
                    }
                    return new ShotResultDto(m.ShooterId, m.TargetId, m.Row, m.Col, m.Result, sunkCells);
                }).ToList();

            // Shots received by me (so my fleet board shows damage) — sea-absolute coords
            var shotsOnMe = state.AllMoves
                .Where(m => m.TargetId == userId)
                .Select(m =>
                {
                    List<CellDto>? sunkCells = null;
                    if (m.Result == "sunk")
                    {
                        var fleet  = state.Fleets.GetValueOrDefault(userId);
                        var offset = state.FleetOffsets.GetValueOrDefault(userId);
                        if (fleet is not null)
                        {
                            int lRow = m.Row - offset.Row;
                            int lCol = m.Col - offset.Col;
                            var ship = fleet.Ships.FirstOrDefault(s => s.HitCells.Contains(new Cell(lRow, lCol)));
                            sunkCells = ship?.Cells
                                .Select(c => new CellDto(c.Row + offset.Row, c.Col + offset.Col))
                                .ToList();
                        }
                    }
                    return new ShotResultDto(m.ShooterId, m.TargetId, m.Row, m.Col, m.Result, sunkCells);
                }).ToList();

            var allRelevantMoves = myMoves.Concat(shotsOnMe).ToList();

            // Eliminated players
            var eliminated = state.TurnOrder
                .Where(id => state.Fleets.TryGetValue(id, out var f) && f.IsEliminated)
                .ToList();

            var turn          = _gameState.GetCurrentTurn(activeRoomId);
            var fleetPositions = _gameState.GetAllFleetPositions(activeRoomId);

            await Clients.Caller.SendAsync("RejoinGame", new
            {
                roomId = activeRoomId,
                phase  = state.Phase.ToString().ToLower(),
                moves  = allRelevantMoves,
                eliminatedPlayerIds  = eliminated,
                currentTurnPlayerId  = turn?.PlayerId,
                currentTurnUsername  = turn?.Username,
                fleetPositions       = fleetPositions,
                myFleet = state.Fleets.TryGetValue(userId, out var myFleet)
                    ? myFleet.Ships.Select(ship => new
                      {
                          size = ship.Cells.Count,
                          row  = ship.Cells.First().Row,
                          col  = ship.Cells.First().Col,
                          horizontal = ship.Cells.Count < 2 || ship.Cells[0].Row == ship.Cells[1].Row
                      }).ToList()
                    : null
            });
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        var username = GetUsername();

        // Only remove from the SignalR group if the player is NOT in an active battle.
        // During a battle, we keep their room membership so they can rejoin on reconnect.
        var activeRoomId = _gameState.GetActiveGameRoomForUser(userId);
        if (activeRoomId is null)
        {
            // Not in an active game — clean up room membership normally
            var roomId = _onlineUsers.RemoveFromRoom(userId);
            if (roomId is not null)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room-{roomId}");
                await Clients.Group($"room-{roomId}").SendAsync("UserLeftLobby", userId, username);
            }
        }
        else
        {
            // Still in an active game — just remove the connection ID but keep room membership
            // so on reconnect we can restore their game state
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

    // ── Presence ───────────────────────────────────────────────────────────

    public async Task SendGameInvite(int targetUserId, string roomId)
    {
        var senderId = GetUserId();
        var senderUsername = GetUsername();
        var connectionId = _onlineUsers.GetConnectionId(targetUserId);
        if (connectionId is not null)
            await Clients.Client(connectionId).SendAsync("GameInviteReceived", senderId, senderUsername, roomId);
    }

    // ── Lobby ──────────────────────────────────────────────────────────────

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

        // If leaving during an active battle, treat as voluntary exit → remove from game state too
        var state = _gameState.GetState(roomId);
        if (state is not null && state.Phase == GamePhase.Battle)
        {
            // Mark player as eliminated (voluntary leave)
            if (state.Fleets.TryGetValue(userId, out var fleet))
            {
                // Force all ships as sunk by hitting every cell
                foreach (var ship in fleet.Ships)
                    foreach (var cell in ship.Cells)
                        ship.TryHit(cell.Row, cell.Col);

                await _hubContext.Clients.Group($"room-{roomId}").SendAsync("PlayerEliminated", userId);

                var alive = state.AlivePlayers;
                if (alive.Count == 1)
                {
                    state.Phase = GamePhase.Finished;
                    var winnerId = alive[0];
                    var winnerName = state.PlayerNames.GetValueOrDefault(winnerId, "?");
                    await _hubContext.Clients.Group($"room-{roomId}").SendAsync("BattleOver", new { winnerId, winnerUsername = winnerName });
                    await PersistGameOver(roomId, winnerId);
                }
            }
        }

        _onlineUsers.RemoveFromRoom(userId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room-{roomId}");
        await Clients.Group($"room-{roomId}").SendAsync("UserLeftLobby", userId, username);
    }

    // ── Placement phase ────────────────────────────────────────────────────

    public async Task StartGame()
    {
        var userId = GetUserId();
        var roomId = _onlineUsers.GetUserRoom(userId);
        if (roomId is null || _onlineUsers.GetRoomHost(roomId) != userId) return;

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

    private async Task ForceBattleStart(string roomId)
    {
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
        var state = _gameState.GetState(roomId);
        if (state is null) return;

        state.Phase = GamePhase.Battle;

        // ── Persist game start to DB ──
        var record = new GameRecord
        {
            RoomId = roomId,
            StartedAt = DateTime.UtcNow
        };
        foreach (var (uid, uname) in state.PlayerNames)
        {
            var fleet = state.Fleets.GetValueOrDefault(uid);
            var fleetDtos = fleet?.Ships.Select(ship => new ShipDto(
                ship.Cells.Count,
                ship.Cells.First().Row,
                ship.Cells.First().Col,
                ship.Cells.Count < 2 || ship.Cells[0].Row == ship.Cells[1].Row
            )).ToList() ?? new List<ShipDto>();

            record.Participants.Add(new GameParticipant
            {
                UserId = uid,
                FleetJson = JsonSerializer.Serialize(fleetDtos)
            });
        }
        _db.GameRecords.Add(record);
        await _db.SaveChangesAsync();
        state.DbGameId = record.Id;

        await _hubContext.Clients.Group($"room-{roomId}").SendAsync("AllReady");

        // Send initial fleet positions to all players
        var positions = _gameState.GetAllFleetPositions(roomId);
        await _hubContext.Clients.Group($"room-{roomId}").SendAsync("FleetPositions", positions);

        var turn = _gameState.GetCurrentTurn(roomId);
        if (turn is null) return;

        await _hubContext.Clients.Group($"room-{roomId}").SendAsync("TurnStarted", turn.PlayerId, turn.Username);
        StartTurnTimer(roomId, turn.PlayerId, turn.Username);
    }

    // ── Battle phase ───────────────────────────────────────────────────────

    /// <summary>
    /// Move the current player's fleet by one cell.
    /// row/col are sea-absolute (0-49).
    /// </summary>
    public async Task MoveFleet(string direction)
    {
        var userId = GetUserId();
        var roomId = _onlineUsers.GetUserRoom(userId);
        if (roomId is null) return;

        var turn = _gameState.GetCurrentTurn(roomId);
        if (turn?.PlayerId != userId) return;

        _gameState.CancelTurnTimer(roomId);

        var (moved, newPos) = _gameState.MoveFleet(roomId, userId, direction);
        if (!moved || newPos is null) return;

        await Clients.Group($"room-{roomId}").SendAsync("FleetMoved", newPos);

        var nextTurn = _gameState.AdvanceTurn(roomId);
        if (nextTurn is null) return;

        await Clients.Group($"room-{roomId}")
            .SendAsync("TurnStarted", new { playerId = nextTurn.PlayerId, username = nextTurn.Username });
        StartTurnTimer(roomId, nextTurn.PlayerId, nextTurn.Username);
    }

    /// <summary>Fire a shot at sea-absolute coordinates.</summary>
    public async Task FireShot(int targetId, int seaRow, int seaCol)
    {
        var userId = GetUserId();
        var roomId = _onlineUsers.GetUserRoom(userId);
        if (roomId is null) return;

        var turn = _gameState.GetCurrentTurn(roomId);
        if (turn?.PlayerId != userId) return;

        _gameState.CancelTurnTimer(roomId);
        await ProcessAndBroadcastShot(roomId, userId, targetId, seaRow, seaCol, useHubContext: false);
    }

    public async Task SendBattleChat(string roomId, string message)
    {
        var userId = GetUserId();
        var username = GetUsername();
        if (_onlineUsers.GetUserRoom(userId) != roomId) return;
        await Clients.Group($"room-{roomId}").SendAsync("BattleChatMessage", userId, username, message, DateTime.UtcNow);
    }

    // ── Shared helpers ─────────────────────────────────────────────────────

    private async Task ProcessAndBroadcastShot(string roomId, int shooterId, int targetId, int row, int col, bool useHubContext)
    {
        var (result, eliminated, gameOver, winnerId) = _gameState.ProcessShot(roomId, shooterId, targetId, row, col);
        if (result is null) return;

        // ── Persist move to DB ──
        var state = _gameState.GetState(roomId);
        if (state?.DbGameId > 0)
        {
            _db.GameMoves.Add(new GameMove
            {
                GameRecordId = state.DbGameId,
                ShooterId = shooterId,
                TargetId = targetId,
                Row = row,
                Col = col,
                Result = result.Result,
                FiredAt = DateTime.UtcNow
            });

            if (eliminated)
            {
                var participant = await _db.GameParticipants
                    .FirstOrDefaultAsync(p => p.GameRecordId == state.DbGameId && p.UserId == targetId);
                if (participant is not null) participant.IsEliminated = true;
            }

            await _db.SaveChangesAsync();
        }

        await Send($"room-{roomId}", "ShotFired", result, useHubContext);

        if (eliminated)
            await Send($"room-{roomId}", "PlayerEliminated", targetId, useHubContext);

        if (gameOver && winnerId.HasValue)
        {
            var winnerName = state?.PlayerNames.GetValueOrDefault(winnerId.Value, "?") ?? "?";
            await Send($"room-{roomId}", "BattleOver", new { winnerId = winnerId.Value, winnerUsername = winnerName }, useHubContext);
            await PersistGameOver(roomId, winnerId.Value);
            return;
        }

        var nextTurn = _gameState.AdvanceTurn(roomId);
        if (nextTurn is null) return;

        await Send($"room-{roomId}", "TurnStarted", new { playerId = nextTurn.PlayerId, username = nextTurn.Username }, useHubContext);
        StartTurnTimer(roomId, nextTurn.PlayerId, nextTurn.Username);
    }

    private async Task PersistGameOver(string roomId, int winnerId)
    {
        var state = _gameState.GetState(roomId);
        if (state?.DbGameId > 0)
        {
            var record = await _db.GameRecords.FindAsync(state.DbGameId);
            if (record is not null)
            {
                record.WinnerId = winnerId;
                record.EndedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
        _gameState.RemoveGame(roomId);
    }

    private void StartTurnTimer(string roomId, int currentPlayerId, string currentPlayerUsername)
    {
        var cts = _gameState.SetTurnTimer(roomId);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(15_000, cts.Token);

                // Try auto-shot against in-range enemies first
                var randomShot = _gameState.GetRandomShot(roomId, currentPlayerId);
                if (randomShot is not null)
                {
                    var (targetId, r, c) = randomShot.Value;
                    await ProcessAndBroadcastShot(roomId, currentPlayerId, targetId, r, c, useHubContext: true);
                    return;
                }

                // No in-range enemies — auto-move toward nearest opponent
                var dir      = _gameState.GetAutoMoveDirection(roomId, currentPlayerId);
                var (moved, newPos) = _gameState.MoveFleet(roomId, currentPlayerId, dir);
                if (moved && newPos is not null)
                    await _hubContext.Clients.Group($"room-{roomId}").SendAsync("FleetMoved", newPos);

                var nextTurn = _gameState.AdvanceTurn(roomId);
                if (nextTurn is null) return;

                await _hubContext.Clients.Group($"room-{roomId}")
                    .SendAsync("TurnStarted", new { playerId = nextTurn.PlayerId, username = nextTurn.Username });
                StartTurnTimer(roomId, nextTurn.PlayerId, nextTurn.Username);
            }
            catch (OperationCanceledException) { }
        });
    }

    private Task Send(string group, string method, object? arg, bool useHubContext) =>
        useHubContext
            ? _hubContext.Clients.Group(group).SendAsync(method, arg)
            : Clients.Group(group).SendAsync(method, arg);
}
