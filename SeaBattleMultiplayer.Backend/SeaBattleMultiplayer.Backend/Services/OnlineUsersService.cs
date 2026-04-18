using System.Collections.Concurrent;

namespace SeaBattleMultiplayer.Backend.Services;

public class OnlineUsersService
{
    private readonly ConcurrentDictionary<int, string> _connections = new();

    public void AddUser(int userId, string connectionId) =>
        _connections[userId] = connectionId;

    public void RemoveUser(int userId) =>
        _connections.TryRemove(userId, out _);

    public bool IsOnline(int userId) => _connections.ContainsKey(userId);

    public IEnumerable<int> GetOnlineUserIds() => _connections.Keys;

    public string? GetConnectionId(int userId) =>
        _connections.TryGetValue(userId, out var cid) ? cid : null;
}
