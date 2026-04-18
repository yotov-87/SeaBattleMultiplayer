using System.Collections.Concurrent;
using SeaBattleMultiplayer.Backend.DTOs;
using SeaBattleMultiplayer.Backend.Models;

namespace SeaBattleMultiplayer.Backend.Services;

public class GameStateService
{
    private readonly ConcurrentDictionary<string, GameRoomState> _games = new();

    public void InitGame(string roomId, Dictionary<int, string> playerNames)
    {
        var state = new GameRoomState();
        foreach (var (id, name) in playerNames)
            state.PlayerNames[id] = name;

        // Turn order: alphabetical by username
        state.TurnOrder = playerNames
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .ToList();

        _games[roomId] = state;
    }

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

    public TurnDto? GetCurrentTurn(string roomId)
    {
        if (!_games.TryGetValue(roomId, out var state)) return null;
        var id = state.CurrentPlayerId;
        if (id == -1) return null;
        return new TurnDto(id, state.PlayerNames.GetValueOrDefault(id, "?"));
    }

    public (ShotResultDto? Result, bool Eliminated, bool GameOver, int? WinnerId)
        ProcessShot(string roomId, int shooterId, int targetId, int row, int col)
    {
        if (!_games.TryGetValue(roomId, out var state))
            return (null, false, false, null);

        if (!state.Fleets.TryGetValue(targetId, out var fleet))
            return (null, false, false, null);

        // Prevent duplicate shots
        var fired = state.GetOrCreateFiredCells(shooterId, targetId);
        if (!fired.Add(new Cell(row, col)))
            return (null, false, false, null);

        var outcome = fleet.ReceiveShot(row, col);
        var resultStr = outcome switch
        {
            ShotOutcome.Sunk => "sunk",
            ShotOutcome.Hit  => "hit",
            _                => "miss"
        };

        List<CellDto>? sunkCells = null;
        if (outcome == ShotOutcome.Sunk)
        {
            var sunkShip = fleet.Ships.First(s => s.HitCells.Contains(new Cell(row, col)));
            sunkCells = sunkShip.Cells.Select(c => new CellDto(c.Row, c.Col)).ToList();
        }

        var dto = new ShotResultDto(shooterId, targetId, row, col, resultStr, sunkCells);
        bool eliminated = fleet.IsEliminated;

        bool gameOver = false;
        int? winnerId = null;
        if (eliminated)
        {
            var alive = state.AlivePlayers;
            if (alive.Count == 1)
            {
                gameOver = true;
                winnerId = alive[0];
            }
        }

        return (dto, eliminated, gameOver, winnerId);
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

    public (int TargetId, int Row, int Col)? GetRandomShot(string roomId, int shooterId)
    {
        if (!_games.TryGetValue(roomId, out var state)) return null;

        var opponents = state.AlivePlayers.Where(id => id != shooterId).ToList();
        if (opponents.Count == 0) return null;

        var rng = Random.Shared;
        var targetId = opponents[rng.Next(opponents.Count)];
        var fired = state.GetOrCreateFiredCells(shooterId, targetId);

        for (int attempt = 0; attempt < 200; attempt++)
        {
            int r = rng.Next(10);
            int c = rng.Next(10);
            if (!fired.Contains(new Cell(r, c)))
                return (targetId, r, c);
        }
        return null;
    }

    public GameRoomState? GetState(string roomId) =>
        _games.TryGetValue(roomId, out var s) ? s : null;

    public void RemoveGame(string roomId) =>
        _games.TryRemove(roomId, out _);
}
