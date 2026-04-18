using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SeaBattleMultiplayer.Backend.Data;
using SeaBattleMultiplayer.Backend.DTOs;
using SeaBattleMultiplayer.Backend.Models;
using SeaBattleMultiplayer.Backend.Services;

namespace SeaBattleMultiplayer.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FriendsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly OnlineUsersService _onlineUsers;

    public FriendsController(AppDbContext db, OnlineUsersService onlineUsers)
    {
        _db = db;
        _onlineUsers = onlineUsers;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("players")]
    public async Task<IActionResult> GetAllPlayers()
    {
        var currentUserId = GetUserId();

        var friendIds = await _db.Friendships
            .Where(f => f.RequesterId == currentUserId || f.AddresseeId == currentUserId)
            .Select(f => f.RequesterId == currentUserId ? f.AddresseeId : f.RequesterId)
            .ToListAsync();

        var players = await _db.Users
            .Where(u => u.Id != currentUserId)
            .Select(u => new PlayerDto
            {
                Id = u.Id,
                Username = u.Username,
                IsOnline = _onlineUsers.IsOnline(u.Id),
                IsFriend = friendIds.Contains(u.Id)
            })
            .ToListAsync();

        return Ok(players);
    }

    [HttpPost("{targetUserId}/add")]
    public async Task<IActionResult> AddFriend(int targetUserId)
    {
        var currentUserId = GetUserId();

        if (currentUserId == targetUserId)
            return BadRequest(new { message = "Cannot add yourself." });

        var exists = await _db.Friendships.AnyAsync(f =>
            (f.RequesterId == currentUserId && f.AddresseeId == targetUserId) ||
            (f.RequesterId == targetUserId && f.AddresseeId == currentUserId));

        if (exists) return Conflict(new { message = "Already friends." });

        _db.Friendships.Add(new Friendship { RequesterId = currentUserId, AddresseeId = targetUserId });
        await _db.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{targetUserId}/remove")]
    public async Task<IActionResult> RemoveFriend(int targetUserId)
    {
        var currentUserId = GetUserId();

        var friendship = await _db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == currentUserId && f.AddresseeId == targetUserId) ||
            (f.RequesterId == targetUserId && f.AddresseeId == currentUserId));

        if (friendship is null) return NotFound();

        _db.Friendships.Remove(friendship);
        await _db.SaveChangesAsync();

        return Ok();
    }
}
