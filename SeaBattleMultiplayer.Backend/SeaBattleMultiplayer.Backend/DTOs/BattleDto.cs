namespace SeaBattleMultiplayer.Backend.DTOs;

public record ShipDto(int Size, int Row, int Col, bool Horizontal);

public record CellDto(int Row, int Col);

public record ShotResultDto(
    int ShooterId,
    int TargetId,
    int Row,
    int Col,
    string Result,           // "miss" | "hit" | "sunk"
    List<CellDto>? SunkCells
);

public record TurnDto(int PlayerId, string Username);
