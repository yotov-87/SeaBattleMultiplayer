namespace SeaBattleMultiplayer.Backend.DTOs;

public class PlayerDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public bool IsFriend { get; set; }
}
