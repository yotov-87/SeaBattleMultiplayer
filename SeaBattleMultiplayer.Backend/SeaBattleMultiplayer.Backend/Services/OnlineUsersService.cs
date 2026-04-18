using System.Collections.Concurrent;

namespace SeaBattleMultiplayer.Backend.Services;

public class OnlineUsersService
{
    private readonly ConcurrentDictionary<int, string> _connections = new();

    // userId → roomId
    private readonly ConcurrentDictionary<int, string> _userRooms = new();

    // roomId → (userId → username)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, string>> _rooms = new();

    // roomId → hostUserId
    private readonly ConcurrentDictionary<string, int> _roomHosts = new();

    // roomId → set of ready userIds
    private readonly ConcurrentDictionary<string, HashSet<int>> _readyPlayers = new();

    // roomId → placement timer cancellation token
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _placementTimers = new();

    // userId → disconnect grace timer (delays UserOffline broadcast on browser refresh)
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _disconnectTimers = new();

    // ── Connection management ──────────────────────────────────────────────

    public void AddUser(int userId, string connectionId) =>
        _connections[userId] = connectionId;

    public void RemoveUser(int userId) =>
        _connections.TryRemove(userId, out _);

    /// <summary>Starts a 3-second grace period. Returns the CTS for the caller to await.
    /// Call CancelDisconnectGrace on reconnect to abort.</summary>
    public CancellationTokenSource StartDisconnectGrace(int userId)
    {
        if (_disconnectTimers.TryRemove(userId, out var old)) old.Cancel();
        var cts = new CancellationTokenSource();
        _disconnectTimers[userId] = cts;
        return cts;
    }

    /// <summary>Cancels a pending disconnect grace period (user reconnected in time).</summary>
    public void CancelDisconnectGrace(int userId)
    {
        if (_disconnectTimers.TryRemove(userId, out var cts)) cts.Cancel();
    }

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
        _roomHosts[roomId] = hostId;
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
            if (members.IsEmpty)
            {
                _rooms.TryRemove(roomId, out _);
                _roomHosts.TryRemove(roomId, out _);
                _readyPlayers.TryRemove(roomId, out _);
                if (_placementTimers.TryRemove(roomId, out var cts)) cts.Cancel();
            }
        }
        return roomId;
    }

    public string? GetUserRoom(int userId) =>
        _userRooms.TryGetValue(userId, out var roomId) ? roomId : null;

    public int? GetRoomHost(string roomId) =>
        _roomHosts.TryGetValue(roomId, out var h) ? h : null;

    public IEnumerable<(int Id, string Username)> GetRoomMembers(string roomId)
    {
        if (_rooms.TryGetValue(roomId, out var members))
            return members.Select(kv => (kv.Key, kv.Value)).ToList();
        return [];
    }

    // ── Placement phase ────────────────────────────────────────────────────

    /// <summary>Initialises the ready-set and returns a new CancellationTokenSource for the 25s timer.</summary>
    public CancellationTokenSource InitPlacementTimer(string roomId)
    {
        if (_placementTimers.TryRemove(roomId, out var old)) old.Cancel();
        _readyPlayers[roomId] = new HashSet<int>();
        var cts = new CancellationTokenSource();
        _placementTimers[roomId] = cts;
        return cts;
    }

    /// <summary>Marks a player as ready. Returns true when ALL room members are ready.</summary>
    public bool MarkReady(string roomId, int userId)
    {
        if (!_readyPlayers.TryGetValue(roomId, out var ready)) return false;
        if (!_rooms.TryGetValue(roomId, out var members)) return false;
        lock (ready)
        {
            ready.Add(userId);
            return ready.Count >= members.Count;
        }
    }

    public void CancelPlacementTimer(string roomId)
    {
        if (_placementTimers.TryRemove(roomId, out var cts)) cts.Cancel();
    }
}
