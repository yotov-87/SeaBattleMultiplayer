using System.Text.Json;

namespace SeaBattleMultiplayer.Backend.Models;

/// <summary>Persisted record of a single battle (one per room session).</summary>
public class GameRecord
{
    public int Id { get; set; }

    /// <summary>The short room ID used for SignalR group routing.</summary>
    public string RoomId { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    /// <summary>Winner's userId, null while game is in progress.</summary>
    public int? WinnerId { get; set; }

    /// <summary>JSON snapshot of the full in-memory GameRoomState for reconnect restore.</summary>
    public string StateJson { get; set; } = "{}";

    public List<GameParticipant> Participants { get; set; } = new();
    public List<GameMove> Moves { get; set; } = new();
}

/// <summary>Each player in a GameRecord.</summary>
public class GameParticipant
{
    public int Id { get; set; }
    public int GameRecordId { get; set; }
    public GameRecord GameRecord { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>JSON array of ShipDto — the fleet the player placed.</summary>
    public string FleetJson { get; set; } = "[]";

    public bool IsEliminated { get; set; }
}

/// <summary>Every shot fired during the game.</summary>
public class GameMove
{
    public int Id { get; set; }
    public int GameRecordId { get; set; }
    public GameRecord GameRecord { get; set; } = null!;

    public int ShooterId { get; set; }
    public int TargetId { get; set; }
    public int Row { get; set; }
    public int Col { get; set; }

    /// <summary>"miss" | "hit" | "sunk"</summary>
    public string Result { get; set; } = "miss";

    public DateTime FiredAt { get; set; } = DateTime.UtcNow;
}
