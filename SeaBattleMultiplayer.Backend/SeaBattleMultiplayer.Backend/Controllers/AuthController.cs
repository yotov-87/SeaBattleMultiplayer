using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SeaBattleMultiplayer.Backend.Data;
using SeaBattleMultiplayer.Backend.DTOs;
using SeaBattleMultiplayer.Backend.Models;
using SeaBattleMultiplayer.Backend.Services;

namespace SeaBattleMultiplayer.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;

    public AuthController(AppDbContext db, JwtService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] AuthRequestDto dto)
    {
        if (await _db.Users.AnyAsync(u => u.Username == dto.Username))
            return Conflict(new { message = "Username already exists." });

        var user = new User
        {
            Username = dto.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var token = _jwt.GenerateToken(user);
        return Ok(new AuthResponseDto { Token = token, Username = user.Username });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AuthRequestDto dto)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == dto.Username);
        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid username or password." });

        var token = _jwt.GenerateToken(user);
        return Ok(new AuthResponseDto { Token = token, Username = user.Username });
    }
}
