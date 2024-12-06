using Unity.Services.Lobbies.Models;
using System.Collections.Generic;

public class LobbySettings
{
    public string LobbyName { get; set; }
    public int MaxPlayers { get; set; }
    public int MinPlayersToStart { get; set; }
    public bool IsPrivate { get; set; }
    public string GameMode { get; set; }
    public string MapName { get; set; }
    public Dictionary<string, string> CustomSettings { get; set; }

    public Dictionary<string, DataObject> ToLobbyData()
    {
        return new Dictionary<string, DataObject>
        {
            {"LobbyName", new DataObject(DataObject.VisibilityOptions.Public, LobbyName)},
            {"GameMode", new DataObject(DataObject.VisibilityOptions.Public, GameMode)},
            {"MapName", new DataObject(DataObject.VisibilityOptions.Public, MapName)},
            {"MinPlayers", new DataObject(DataObject.VisibilityOptions.Public, MinPlayersToStart.ToString())},
            {"Started", new DataObject(DataObject.VisibilityOptions.Public, "false")}
        };
    }
}