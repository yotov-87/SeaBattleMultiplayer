using System.Collections.Concurrent;

namespace SeaBattleMultiplayer.Backend.Services;

public class OnlineUsersService
{
    private readonly ConcurrentDictionary<int, string> _connections = new();

    // userId → roomId
    private readonly ConcurrentDictionary<int, string> _userRooms = new();

    // roomId → (userId → username)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, string>> _rooms = new();

    // ── Connection management ──────────────────────────────────────────────

    public void AddUser(int userId, string connectionId) =>
        _connections[userId] = connectionId;

    public void RemoveUser(int userId) =>
        _connections.TryRemove(userId, out _);

    public bool IsOnline(int userId) => _connections.ContainsKey(userId);

    public IEnumerable<int> GetOnlineUserIds() => _connections.Keys;

    public string? GetConnectionId(int userId) =>
        _connections.TryGetValue(userId, out var cid) ? cid : null;

    // ── Room management ────────────────────────────────────────────────────

    public string CreateRoom(int hostId, string hostUsername)
    {
        var roomId = Guid.NewGuid().ToString("N")[..8];
        var members = new ConcurrentDictionary<int, string>();
        members[hostId] = hostUsername;
        _rooms[roomId] = members;
        _userRooms[hostId] = roomId;
        return roomId;
    }

    /// <summary>Returns false if the room does not exist.</summary>
    public bool AddToRoom(string roomId, int userId, string username)
    {
        if (!_rooms.TryGetValue(roomId, out var members)) return false;
        members[userId] = username;
        _userRooms[userId] = roomId;
        return true;
    }

    /// <summary>Removes the user from whichever room they are in.
    /// Returns the roomId they were removed from, or null.</summary>
    public string? RemoveFromRoom(int userId)
    {
        if (!_userRooms.TryRemove(userId, out var roomId)) return null;
        if (_rooms.TryGetValue(roomId, out var members))
        {
            members.TryRemove(userId, out _);
            if (members.IsEmpty) _rooms.TryRemove(roomId, out _);
        }
        return roomId;
    }

    public string? GetUserRoom(int userId) =>
        _userRooms.TryGetValue(userId, out var roomId) ? roomId : null;

    public IEnumerable<(int Id, string Username)> GetRoomMembers(string roomId)
    {
        if (_rooms.TryGetValue(roomId, out var members))
            return members.Select(kv => (kv.Key, kv.Value)).ToList();
        return [];
    }
}
