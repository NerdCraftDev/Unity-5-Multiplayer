using System; 
using System.Collections.Generic;

public class LobbyData
{
    public string LobbyId { get; set; }
    public string LobbyCode { get; set; }
    public string HostId { get; set; }
    public Dictionary<string, string> Settings { get; set; }
    public List<PlayerData> Players { get; set; }
    public bool IsLocked { get; set; }
    public DateTime LastHeartbeat { get; set; }

    public class PlayerData
    {
        private LobbyData _lobbyData;

        public PlayerData(string playerId, string playerName, int team, bool isReady, LobbyData lobbyData)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            Team = team;
            IsReady = isReady;
            _lobbyData = lobbyData;
        }

        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int Team { get; set; }
        public bool IsReady { get; set; }
        public bool IsHost => PlayerId == _lobbyData.HostId;
    }
}