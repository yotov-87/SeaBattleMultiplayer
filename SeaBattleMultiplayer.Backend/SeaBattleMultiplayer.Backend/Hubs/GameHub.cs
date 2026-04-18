using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SeaBattleMultiplayer.Backend.Data;
using SeaBattleMultiplayer.Backend.DTOs;
using SeaBattleMultiplayer.Backend.Services;

namespace SeaBattleMultiplayer.Backend.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly OnlineUsersService _onlineUsers;
    private readonly AppDbContext _db;

    public GameHub(OnlineUsersService onlineUsers, AppDbContext db)
    {
        _onlineUsers = onlineUsers;
        _db = db;
    }

    private int GetUserId() =>
        int.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string GetUsername() =>
        Context.User!.FindFirstValue(ClaimTypes.Name)!;

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var username = GetUsername();

        _onlineUsers.AddUser(userId, Context.ConnectionId);

        // Notify everyone else that this user came online
        await Clients.Others.SendAsync("UserOnline", userId, username);

        // Send the caller the current list of online user IDs
        await Clients.Caller.SendAsync("OnlineUsers", _onlineUsers.GetOnlineUserIds());

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();

        _onlineUsers.RemoveUser(userId);

        // Notify everyone that this user went offline
        await Clients.Others.SendAsync("UserOffline", userId);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendGameInvite(int targetUserId)
    {
        var senderId = GetUserId();
        var senderUsername = GetUsername();

        var connectionId = _onlineUsers.GetConnectionId(targetUserId);
        if (connectionId is not null)
        {
            await Clients.Client(connectionId)
                .SendAsync("GameInviteReceived", senderId, senderUsername);
        }
    }
}
