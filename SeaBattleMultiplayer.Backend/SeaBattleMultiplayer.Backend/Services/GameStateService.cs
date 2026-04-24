using System.Collections.Concurrent;
using SeaBattleMultiplayer.Backend.DTOs;
using SeaBattleMultiplayer.Backend.Models;

namespace SeaBattleMultiplayer.Backend.Services;

public class GameStateService
{
    private readonly ConcurrentDictionary<string, GameRoomState> _games = new();

    // -------------------------------------------------------------------------
    // Init
    // -------------------------------------------------------------------------

    public void InitGame(string roomId, Dictionary<int, string> playerNames)
    {
        var state = new GameRoomState();
        foreach (var (id, name) in playerNames)
            state.PlayerNames[id] = name;

        state.TurnOrder = playerNames
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .ToList();

        _games[roomId] = state;
        InitFleetPositions(state);
    }

    /// <summary>Assigns starting sea positions at spread-out corners.</summary>
    private static void InitFleetPositions(GameRoomState state)
    {
        // Corners / mid-edges for up to 8 players on a 50x50 sea
        (int Row, int Col)[] starts =
        [
            ( 0,  0), (40, 40), ( 0, 40), (40,  0),
            ( 0, 20), (40, 20), (20,  0), (20, 40)
        ];

        for (int i = 0; i < state.TurnOrder.Count; i++)
            state.FleetOffsets[state.TurnOrder[i]] = starts[i % starts.Length];
    }

    // -------------------------------------------------------------------------
    // Fleet placement
    // -------------------------------------------------------------------------

    public void SubmitFleet(string roomId, int userId, List<ShipDto> ships)
    {
        if (!_games.TryGetValue(roomId, out var state)) return;

        var fleet = new PlayerFleet();
        foreach (var dto in ships)
        {
            var cells = new List<Cell>();
            for (int i = 0; i < dto.Size; i++)
            {
                int r = dto.Horizontal ? dto.Row : dto.Row + i;
                int c = dto.Horizontal ? dto.Col + i : dto.Col;
                cells.Add(new Cell(r, c));
            }
            fleet.Ships.Add(new BattleShip { Cells = cells });
        }
        state.Fleets[userId] = fleet;
    }

    public bool AllFleetsSubmitted(string roomId)
    {
        if (!_games.TryGetValue(roomId, out var state)) return false;
        return state.PlayerNames.Keys.All(id => state.Fleets.ContainsKey(id));
    }

    // -------------------------------------------------------------------------
    // Fleet movement
    // -------------------------------------------------------------------------

    /// <summary>
    /// Moves the player's fleet by one cell in the given direction.
    /// Returns (true, newPosition) on success, (false, null) otherwise.
    /// </summary>
    public (bool Moved, FleetPositionDto? NewPosition)
        MoveFleet(string roomId, int playerId, string direction)
    {
        if (!_games.TryGetValue(roomId, out var state)) return (false, null);
        if (!state.FleetOffsets.ContainsKey(playerId))    return (false, null);

        var (r, c) = state.FleetOffsets[playerId];
        var (nr, nc) = direction.ToLowerInvariant() switch
        {
            "up"    => (r - 1, c),
            "down"  => (r + 1, c),
            "left"  => (r, c - 1),
            "right" => (r, c + 1),
            _       => (r, c)
        };

        nr = Math.Clamp(nr, 0, GameRoomState.MaxOffset);
        nc = Math.Clamp(nc, 0, GameRoomState.MaxOffset);

        if (nr == r && nc == c) return (false, null);

        state.FleetOffsets[playerId] = (nr, nc);
        return (true, new FleetPositionDto(playerId, nr, nc));
    }

    // -------------------------------------------------------------------------
    // Range check
    // -------------------------------------------------------------------------

    /// <summary>
    /// Two fleets are in range when their 10x10 bounding boxes are
    /// within SHOOT_RANGE cells of each other (Chebyshev gap).
    /// </summary>
    private const int ShootRange = 2;

    public bool IsInRange(string roomId, int playerId1, int playerId2)
    {
        if (!_games.TryGetValue(roomId, out var state)) return false;
        return IsInRange(state, playerId1, playerId2);
    }

    private static bool IsInRange(GameRoomState state, int a, int b)
    {
        if (!state.FleetOffsets.TryGetValue(a, out var pa)) return false;
        if (!state.FleetOffsets.TryGetValue(b, out var pb)) return false;

        int rGap = Math.Max(0, Math.Max(pa.Row - (pb.Row + 9), pb.Row - (pa.Row + 9)));
        int cGap = Math.Max(0, Math.Max(pa.Col - (pb.Col + 9), pb.Col - (pa.Col + 9)));
        return Math.Max(rGap, cGap) <= ShootRange;
    }

    // -------------------------------------------------------------------------
    // Shot processing (sea-absolute coordinates)
    // -------------------------------------------------------------------------

    public (ShotResultDto? Result, bool Eliminated, bool GameOver, int? WinnerId)
        ProcessShot(string roomId, int shooterId, int targetId, int seaRow, int seaCol)
    {
        if (!_games.TryGetValue(roomId, out var state))
            return (null, false, false, null);

        if (!state.Fleets.TryGetValue(targetId, out var fleet))
            return (null, false, false, null);

        // Range check
        if (!IsInRange(state, shooterId, targetId))
            return (null, false, false, null);

        if (!state.FleetOffsets.TryGetValue(targetId, out var offset))
            return (null, false, false, null);

        // Translate sea coords -> fleet-local
        int localRow = seaRow - offset.Row;
        int localCol = seaCol - offset.Col;

        if (localRow < 0 || localRow >= GameRoomState.FleetArea ||
            localCol < 0 || localCol >= GameRoomState.FleetArea)
            return (null, false, false, null);

        // Prevent duplicate shots (tracked by sea coords)
        var fired = state.GetOrCreateFiredCells(shooterId, targetId);
        if (!fired.Add(new Cell(seaRow, seaCol)))
            return (null, false, false, null);

        var outcome   = fleet.ReceiveShot(localRow, localCol);
        var resultStr = outcome switch
        {
            ShotOutcome.Sunk => "sunk",
            ShotOutcome.Hit  => "hit",
            _                => "miss"
        };

        List<CellDto>? sunkCells = null;
        if (outcome == ShotOutcome.Sunk)
        {
            var sunkShip = fleet.Ships
                .First(s => s.HitCells.Contains(new Cell(localRow, localCol)));
            // Return sunk cells in sea-absolute coords
            sunkCells = sunkShip.Cells
                .Select(cell => new CellDto(cell.Row + offset.Row, cell.Col + offset.Col))
                .ToList();
        }

        state.AllMoves.Add((shooterId, targetId, seaRow, seaCol, resultStr));

        var dto       = new ShotResultDto(shooterId, targetId, seaRow, seaCol, resultStr, sunkCells);
        bool elim     = fleet.IsEliminated;
        bool gameOver = false;
        int? winnerId = null;

        if (elim)
        {
            var alive = state.AlivePlayers;
            if (alive.Count == 1)
            {
                gameOver  = true;
                winnerId  = alive[0];
                state.Phase = GamePhase.Finished;
            }
        }

        return (dto, elim, gameOver, winnerId);
    }

    // -------------------------------------------------------------------------
    // Turn management
    // -------------------------------------------------------------------------

    public TurnDto? GetCurrentTurn(string roomId)
    {
        if (!_games.TryGetValue(roomId, out var state)) return null;
        var id = state.CurrentPlayerId;
        if (id == -1) return null;
        return new TurnDto(id, state.PlayerNames.GetValueOrDefault(id, "?"));
    }

    public TurnDto? AdvanceTurn(string roomId)
    {
        if (!_games.TryGetValue(roomId, out var state)) return null;

        var alive = state.AlivePlayers;
        if (alive.Count <= 1) return null;

        for (int i = 1; i <= state.TurnOrder.Count; i++)
        {
            int next = (state.TurnIndex + i) % state.TurnOrder.Count;
            if (alive.Contains(state.TurnOrder[next]))
            {
                state.TurnIndex = next;
                var id = state.CurrentPlayerId;
                return new TurnDto(id, state.PlayerNames[id]);
            }
        }
        return null;
    }

    public CancellationTokenSource SetTurnTimer(string roomId)
    {
        if (!_games.TryGetValue(roomId, out var state))
            return new CancellationTokenSource();

        state.TurnTimerCts?.Cancel();
        var cts = new CancellationTokenSource();
        state.TurnTimerCts = cts;
        return cts;
    }

    public void CancelTurnTimer(string roomId)
    {
        if (_games.TryGetValue(roomId, out var state))
            state.TurnTimerCts?.Cancel();
    }

    // -------------------------------------------------------------------------
    // Auto-action helpers (for turn timer expiry)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a random unfired sea-absolute cell in any in-range enemy fleet.
    /// Returns null if no in-range enemies exist (player must auto-move instead).
    /// </summary>
    public (int TargetId, int Row, int Col)? GetRandomShot(string roomId, int shooterId)
    {
        if (!_games.TryGetValue(roomId, out var state)) return null;

        var inRange = state.AlivePlayers
            .Where(id => id != shooterId && IsInRange(state, shooterId, id))
            .ToList();

        if (inRange.Count == 0) return null;

        var rng      = Random.Shared;
        var targetId = inRange[rng.Next(inRange.Count)];
        var fired    = state.GetOrCreateFiredCells(shooterId, targetId);

        if (!state.FleetOffsets.TryGetValue(targetId, out var offset)) return null;

        for (int attempt = 0; attempt < 200; attempt++)
        {
            int r = offset.Row + rng.Next(GameRoomState.FleetArea);
            int c = offset.Col + rng.Next(GameRoomState.FleetArea);
            if (!fired.Contains(new Cell(r, c)))
                return (targetId, r, c);
        }
        return null;
    }

    /// <summary>
    /// Returns the direction that moves this player's fleet closest to any alive
    /// opponent.  Used for auto-move when no enemy is in range.
    /// </summary>
    public string GetAutoMoveDirection(string roomId, int playerId)
    {
        if (!_games.TryGetValue(roomId, out var state))
            return "down";

        if (!state.FleetOffsets.TryGetValue(playerId, out var myPos))
            return "down";

        var opponents = state.AlivePlayers
            .Where(id => id != playerId && state.FleetOffsets.ContainsKey(id))
            .ToList();

        if (opponents.Count == 0)
            return new[] { "up", "down", "left", "right" }[Random.Shared.Next(4)];

        var closest = opponents
            .OrderBy(id =>
            {
                var p = state.FleetOffsets[id];
                return Math.Abs(myPos.Row - p.Row) + Math.Abs(myPos.Col - p.Col);
            })
            .First();

        var target = state.FleetOffsets[closest];
        int dRow   = target.Row - myPos.Row;
        int dCol   = target.Col - myPos.Col;

        return Math.Abs(dRow) >= Math.Abs(dCol)
            ? (dRow > 0 ? "down" : "up")
            : (dCol > 0 ? "right" : "left");
    }

    // -------------------------------------------------------------------------
    // Queries
    // -------------------------------------------------------------------------

    public List<FleetPositionDto> GetAllFleetPositions(string roomId)
    {
        if (!_games.TryGetValue(roomId, out var state)) return [];
        return state.FleetOffsets
            .Select(kv => new FleetPositionDto(kv.Key, kv.Value.Row, kv.Value.Col))
            .ToList();
    }

    public GameRoomState? GetState(string roomId) =>
        _games.TryGetValue(roomId, out var s) ? s : null;

    public string? GetActiveGameRoomForUser(int userId)
    {
        foreach (var (roomId, state) in _games)
        {
            if (state.Phase != GamePhase.Finished && state.PlayerNames.ContainsKey(userId))
                return roomId;
        }
        return null;
    }

    public void RemoveGame(string roomId) =>
        _games.TryRemove(roomId, out _);
}
