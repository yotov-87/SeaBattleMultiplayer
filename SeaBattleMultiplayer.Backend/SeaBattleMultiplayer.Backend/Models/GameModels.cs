namespace SeaBattleMultiplayer.Backend.Models;

public record Cell(int Row, int Col);

public class BattleShip
{
    public required List<Cell> Cells { get; init; }
    private readonly HashSet<Cell> _hitCells = new();

    public IReadOnlyCollection<Cell> HitCells => _hitCells;
    public bool IsSunk => _hitCells.Count >= Cells.Count;

    public bool TryHit(int row, int col)
    {
        var cell = new Cell(row, col);
        if (!Cells.Contains(cell)) return false;
        _hitCells.Add(cell);
        return true;
    }
}

public enum ShotOutcome { Miss, Hit, Sunk }

public class PlayerFleet
{
    public List<BattleShip> Ships { get; } = new();
    public bool IsEliminated => Ships.All(s => s.IsSunk);

    public ShotOutcome ReceiveShot(int row, int col)
    {
        foreach (var ship in Ships)
        {
            if (ship.TryHit(row, col))
                return ship.IsSunk ? ShotOutcome.Sunk : ShotOutcome.Hit;
        }
        return ShotOutcome.Miss;
    }
}

public class GameRoomState
{
    public Dictionary<int, string> PlayerNames { get; } = new();
    public Dictionary<int, PlayerFleet> Fleets { get; } = new();
    public List<int> TurnOrder { get; set; } = new();
    public int TurnIndex { get; set; } = 0;
    public CancellationTokenSource? TurnTimerCts { get; set; }

    // (shooterId, targetId) → cells already fired
    private readonly Dictionary<(int, int), HashSet<Cell>> _firedCells = new();

    public int CurrentPlayerId =>
        TurnOrder.Count > 0 ? TurnOrder[TurnIndex] : -1;

    public List<int> AlivePlayers =>
        TurnOrder.Where(id => Fleets.TryGetValue(id, out var f) && !f.IsEliminated).ToList();

    public HashSet<Cell> GetOrCreateFiredCells(int shooterId, int targetId)
    {
        var key = (shooterId, targetId);
        if (!_firedCells.TryGetValue(key, out var set))
        {
            set = new HashSet<Cell>();
            _firedCells[key] = set;
        }
        return set;
    }
}
