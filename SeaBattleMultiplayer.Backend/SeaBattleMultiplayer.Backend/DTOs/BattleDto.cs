namespace SeaBattleMultiplayer.Backend.DTOs;

public record ShipDto(int Size, int Row, int Col, bool Horizontal);

public record CellDto(int Row, int Col);

/// <summary>
/// Row/Col are sea-absolute coordinates (0-49) on the shared 50×50 sea.
/// SunkCells are also sea-absolute.
/// </summary>
public record ShotResultDto(
    int ShooterId,
    int TargetId,
    int Row,
    int Col,
    string Result,           // "miss" | "hit" | "sunk"
    List<CellDto>? SunkCells
);

public record TurnDto(int PlayerId, string Username);

/// <summary>Sea-absolute top-left offset of a player's 10×10 fleet area.</summary>
public record FleetPositionDto(int PlayerId, int OffsetRow, int OffsetCol);
