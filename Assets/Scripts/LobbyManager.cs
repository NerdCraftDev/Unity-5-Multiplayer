using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class LobbyManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private Transform uiRoot;
    [SerializeField] private GameObject lobbyListItemPrefab;
    [SerializeField] private float heartbeatInterval = 15f;
    // [SerializeField] private float reconnectDelay = 2f;
    [SerializeField] private int maxRetries = 3;
    [SerializeField] private const int MaxUserNameLength = 20;
    [SerializeField] private const int MaxLobbyNameLength = 30;

    
    private LobbyData currentLobby;
    private ILobbyEvents lobbyEvents;
    private bool isLobbyEventActive;
    private float heartbeatTimer;
    private LobbySettings pendingLobbySettings;
    private Queue<string> chatMessages = new Queue<string>(10); // Keep last 10 messages

    private async void Awake()
    {
        UIManager.Initialize(uiRoot);
        await InitializeUnityServices();
        SetupUICallbacks();
        UIManager.ShowScreen(UIManager.Screen.MainMenu);
    }

    private async Task InitializeUnityServices()
    {
        UIManager.ShowLoading("Initializing...");
        
        try
        {
            await UnityServices.InitializeAsync();
            
            // Clear any existing authentication data
            AuthenticationService.Instance.SignOut(true);
            
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log($"Player signed in with ID: {AuthenticationService.Instance.PlayerId}");
                    break;
                }
                catch (AuthenticationException e)
                {
                    if (i == maxRetries - 1) throw;
                    Debug.LogWarning($"Auth attempt {i + 1} failed: {e.Message}");
                    await Task.Delay(1000);
                }
            }
            (LobbyService.Instance as ILobbyServiceSDKConfiguration).EnableLocalPlayerLobbyEvents(true);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize: {e.Message}");
            ShowError("Failed to initialize Unity Services. Please check your internet connection and try again.");
        }
        finally
        {
            UIManager.HideLoading();
        }
    }

    private void SetupUICallbacks()
    {
        // Main Menu
        Debug.Log("Setting up UI callbacks");
        UIManager.GetElement("HostButton").Button.onClick.AddListener(() => 
            UIManager.ShowScreen(UIManager.Screen.HostSettings));
        UIManager.GetElement("JoinButton").Button.onClick.AddListener(() => {
            UIManager.ShowScreen(UIManager.Screen.LobbyBrowser);
            RefreshLobbyList();
        });
        UIManager.GetElement("QuitButton").Button.onClick.AddListener(Application.Quit);

        // Host Settings
        UIManager.GetElement("CreateButton").Button.onClick.AddListener(CreateLobby);
        UIManager.GetElement("BackButtonHostSettings").Button.onClick.AddListener(() => 
            UIManager.ShowScreen(UIManager.Screen.MainMenu));

        // Max Players Slider
        var maxPlayersSlider = UIManager.GetElement("MaxPlayersSlider").Slider;
        var maxPlayersLabel = UIManager.GetElement("MaxPlayersLabel").Text;
        maxPlayersSlider.onValueChanged.AddListener((value) => {
            maxPlayersLabel.text = $"Max Players ({value})";
        });

        // Lobby Browser
        UIManager.GetElement("JoinByCodeButton").Button.onClick.AddListener(JoinLobbyByCode);
        UIManager.GetElement("RefreshButton").Button.onClick.AddListener(RefreshLobbyList);
        UIManager.GetElement("BackButtonLobbyBrowser").Button.onClick.AddListener(() => 
            UIManager.ShowScreen(UIManager.Screen.MainMenu));

        // Lobby Room
        UIManager.GetElement("StartButton").Button.onClick.AddListener(StartGame);
        UIManager.GetElement("LeaveButton").Button.onClick.AddListener(LeaveLobby);
        UIManager.GetElement("ReadyButton").Button.onClick.AddListener(ToggleReady);
        UIManager.GetElement("TeamDropdown").Dropdown.onValueChanged.AddListener(ChangeTeam);
        UIManager.GetElement("ChatInput").InputField.onSubmit.AddListener(SendChatMessage);
    }

    private async void RefreshLobbyList()
    {
        try
        {
            UIManager.ShowLoading("Fetching lobbies...");
            
            UIManager.UIElement lobbyList = UIManager.GetElement("LobbyList");
            var lobbyListContent = lobbyList.GameObject.GetComponent<RectTransform>();
            foreach (Transform child in lobbyListContent)
            {
                Destroy(child.gameObject);
            }

            var options = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                    new QueryFilter(QueryFilter.FieldOptions.IsLocked, "false", QueryFilter.OpOptions.EQ)
                }
            };

            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
            
            foreach (var lobby in response.Results)
            {
                GameObject item = Instantiate(lobbyListItemPrefab, lobbyListContent);
                
                // Set lobby info
                string gameMode = lobby.Data.ContainsKey("GameMode") ? lobby.Data["GameMode"].Value : "Classic";
                string mapName = lobby.Data.ContainsKey("MapName") ? lobby.Data["MapName"].Value : "Default";
                
                item.GetComponentInChildren<TextMeshProUGUI>().text = 
                    $"{lobby.Name} | {lobby.Players.Count}/{lobby.MaxPlayers} Players | Gamemode: {gameMode} | Map: {mapName}";
                
                item.GetComponentInChildren<Button>().onClick.AddListener(() => JoinLobby(lobby.Id));
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError(e.Message);
            ShowError("Failed to fetch lobby list. Please try again.");
        }
        finally
        {
            UIManager.HideLoading();
        }
    }

    private async void JoinLobbyByCode()
    {
        string code = UIManager.GetElement("JoinCodeInput").InputField.text;
        if (string.IsNullOrEmpty(code))
        {
            ShowError("Please enter a lobby code");
            return;
        }

        UIManager.ShowLoading("Joining lobby...");
        
        try
        {
            var joinOptions = new JoinLobbyByCodeOptions
            {
                Player = CreatePlayerData()
            };

            var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code, joinOptions);
            await HandleLobbyJoined(lobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError(e.Message);
            ShowError("Failed to join lobby. Please check the code and try again.");
        }
        finally
        {
            UIManager.HideLoading();
        }
    }

    private async void JoinLobby(string lobbyId)
    {
        UIManager.ShowLoading("Joining lobby...");
        
        string playerName = UIManager.GetElement("PlayerNameInput").InputField.text;
        if (!ValidateUserName(playerName)) return;
        
        try
        {
            var joinOptions = new JoinLobbyByIdOptions
            {
                Player = CreatePlayerData()
            };

            var lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, joinOptions);
            await HandleLobbyJoined(lobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError(e.Message);
            ShowError("Failed to join lobby. Please try again.");
        }
        finally
        {
            UIManager.HideLoading();
        }
    }

    private Player CreatePlayerData()
    {
        string playerName = UIManager.GetElement("PlayerNameInput").InputField.text;
        if (string.IsNullOrEmpty(playerName)) playerName = $"Player_{UnityEngine.Random.Range(0, 10000)}";

        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                {"PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName)},
                {"Team", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "1")},
                {"IsReady", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "false")},
                {"LastChat", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "")}
            }
        };
    }

    private async void ToggleReady()
    {
        if (currentLobby == null) return;

        try
        {
            var player = currentLobby.Players.First(p => p.PlayerId == AuthenticationService.Instance.PlayerId);
            var updateOptions = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {"IsReady", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, (!player.IsReady).ToString())}
                }
            };

            await LobbyService.Instance.UpdatePlayerAsync(currentLobby.LobbyId, 
                AuthenticationService.Instance.PlayerId, updateOptions);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError(e.Message);
            ShowError("Failed to update ready status");
        }
    }

    private async void ChangeTeam(int teamIndex)
    {
        if (currentLobby == null) return;

        try
        {
            var updateOptions = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {"Team", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, (teamIndex + 1).ToString())}
                }
            };

            await LobbyService.Instance.UpdatePlayerAsync(currentLobby.LobbyId, 
                AuthenticationService.Instance.PlayerId, updateOptions);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError(e.Message);
            ShowError("Failed to change team");
        }
    }

    private async void SendChatMessage(string message)
    {
        if (string.IsNullOrEmpty(message) || currentLobby == null) return;

        try
        {
            string playerId = AuthenticationService.Instance.PlayerId;
            var player = currentLobby.Players.First(p => p.PlayerId == playerId);
            string formattedMessage = $"[{player.PlayerName}] {message}";
            // Update player data with the new chat message
            var updatePlayerOptions = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { "LastChat", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, formattedMessage) }
                }
            };

            await LobbyService.Instance.UpdatePlayerAsync(currentLobby.LobbyId, playerId, updatePlayerOptions);

            // Clear input field
            UIManager.GetElement("ChatInput").InputField.text = "";
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError(e.Message);
            ShowError("Failed to send message");
        }
    }

    private void AddChatMessage(string message)
    {
        chatMessages.Enqueue(message);
        if (chatMessages.Count > 10) chatMessages.Dequeue();
        
        var chatDisplay = UIManager.GetElement("ChatDisplay").Text;
        chatDisplay.text = string.Join("\n", chatMessages);
    }

    private void UpdateLobbyUI()
    {
        if (currentLobby == null || (currentLobby.Settings.ContainsKey("Started") && currentLobby.Settings["Started"] == "true")) return;


        var playerList = UIManager.GetElement("PlayerList").Text;
        var lobbyCode = UIManager.GetElement("LobbyCode").Text;
        var startButton = UIManager.GetElement("StartButton").Button;
        var settingsDisplay = UIManager.GetElement("SettingsDisplay").Text;
        var readyButton = UIManager.GetElement("ReadyButton").Button;

        // Update player list
        string playerListText = "Players:\n";
        foreach (var player in currentLobby.Players)
        {
            string readyStatus = player.IsReady || player.IsHost ? "Ready" : "Unready";
            string hostStatus = player.IsHost ? " (Host)" : "";
            playerListText += $"Team {player.Team} - {player.PlayerName}{hostStatus} - {readyStatus}\n";
        }
        playerList.text = playerListText;

        // Update lobby code
        lobbyCode.text = $"Lobby Code: {currentLobby.LobbyCode}";

        // Update settings display
        string settingsText = "Lobby Settings:\n";
        settingsText += $"Lobby Name: {currentLobby.Settings["LobbyName"]}\n";
        settingsText += $"Game Mode: {currentLobby.Settings["GameMode"]}\n";
        settingsText += $"Map: {currentLobby.Settings["MapName"]}\n";
        settingsDisplay.text = settingsText;

        // Update start button
        bool isHost = currentLobby.HostId == AuthenticationService.Instance.PlayerId;
        bool allPlayersReady = currentLobby.Players.All(p => p.IsReady || p.IsHost);
        bool enoughPlayers = currentLobby.Players.Count >= int.Parse(currentLobby.Settings["MinPlayers"]);
        
        startButton.gameObject.SetActive(isHost);
        startButton.interactable = isHost && allPlayersReady && enoughPlayers;

        // Update ready button
        var localPlayer = currentLobby.Players.First(p => p.PlayerId == AuthenticationService.Instance.PlayerId);
        readyButton.gameObject.SetActive(!localPlayer.IsHost);
        readyButton.GetComponentInChildren<TextMeshProUGUI>().text = localPlayer.IsReady ? "Unready" : "Ready";

        // Poll chat messages
        var chatDisplay = UIManager.GetElement("ChatDisplay").Text;
        chatDisplay.text = string.Join("\n", chatMessages);
    }

    private void ShowError(string message)
    {
        Debug.LogError(message);
        // You could implement a proper UI popup here
        // For now, we'll just log to console
    }

    private async void CreateLobby()
    {
        var lobbyName = UIManager.GetElement("LobbyNameInput").InputField.text;
        if (!ValidateLobbyName(lobbyName))
        {
            ShowError("Please enter a lobby name");
            return;
        }
        
        string playerName = UIManager.GetElement("PlayerNameInput").InputField.text;
        if (!ValidateUserName(playerName)) return;

        UIManager.ShowLoading("Creating lobby...");
        
        try
        {
            pendingLobbySettings = new LobbySettings
            {
                LobbyName = lobbyName,
                MaxPlayers = (int)UIManager.GetElement("MaxPlayersSlider").Slider.value,
                MinPlayersToStart = 2, // Could be made configurable
                IsPrivate = UIManager.GetElement("PrivateToggle").Toggle.isOn,
                GameMode = UIManager.GetElement("GameModeDropdown").Dropdown.options[
                    UIManager.GetElement("GameModeDropdown").Dropdown.value].text,
                MapName = UIManager.GetElement("MapDropdown").Dropdown.options[
                    UIManager.GetElement("MapDropdown").Dropdown.value].text
            };

            var createLobbyOptions = new CreateLobbyOptions
            {
                Data = pendingLobbySettings.ToLobbyData(),
                IsPrivate = pendingLobbySettings.IsPrivate,
                Player = CreatePlayerData()
            };

            var lobby = await LobbyService.Instance.CreateLobbyAsync(
                pendingLobbySettings.LobbyName,
                pendingLobbySettings.MaxPlayers,
                createLobbyOptions);

            await HandleLobbyJoined(lobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError(e.Message);
            ShowError("Failed to create lobby. Please try again.");
        }
        finally
        {
            UIManager.HideLoading();
        }
    }

    private bool ValidateUserName(string userName)
    {
        if (userName.Length > MaxUserNameLength)
        {
            ShowError($"User name cannot exceed {MaxUserNameLength} characters");
            return false;
        }

        return true;
    }

    private bool ValidateLobbyName(string lobbyName)
    {
        if (string.IsNullOrEmpty(lobbyName))
        {
            ShowError("Lobby name cannot be empty");
            return false;
        }

        if (lobbyName.Length > MaxLobbyNameLength)
        {
            ShowError($"Lobby name cannot exceed {MaxLobbyNameLength} characters");
            return false;
        }

        return true;
    }

    private async void StartGame()
    {
        if (currentLobby == null || currentLobby.HostId != AuthenticationService.Instance.PlayerId) return;

        try
        {
            // Create Relay allocation
            var allocation = await RelayService.Instance.CreateAllocationAsync(currentLobby.Players.Count);
            var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // Update lobby with relay join code and started status
            var updateOptions = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    {"RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, joinCode)},
                    {"Started", new DataObject(DataObject.VisibilityOptions.Public, "true")}
                }
            };

            await LobbyService.Instance.UpdateLobbyAsync(currentLobby.LobbyId, updateOptions);

            // Load the game scene
            SceneManager.LoadScene("GameScene");

            // Set relay server data before starting the server
            var relayServerData = new RelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // Host joins the Relay
            NetworkManager.Singleton.StartHost();
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            ShowError("Failed to start game. Please try again.");
        }
    }

    private async Task JoinGame(string joinCode)
    {
        if (currentLobby.HostId == AuthenticationService.Instance.PlayerId) return;
        await RelayService.Instance.JoinAllocationAsync(joinCode);
        // Start client
        NetworkManager.Singleton.StartClient();
    }

    private async void LeaveLobby()
    {
        if (currentLobby == null) return;

        try
        {
            // UnsubscribeFromLobbyEvents();
            await LobbyService.Instance.RemovePlayerAsync(currentLobby.LobbyId, AuthenticationService.Instance.PlayerId);
            currentLobby = null;
            UIManager.ShowScreen(UIManager.Screen.MainMenu);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Error leaving lobby: {e.Message}");
        }
    }

    private void Update()
    {
        if (currentLobby == null || !currentLobby.Players.Any(p => p.PlayerId == AuthenticationService.Instance.PlayerId)) return;

        // Handle heartbeat for host
        if (currentLobby.HostId == AuthenticationService.Instance.PlayerId)
        {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer <= 0)
            {
                SendHeartbeat();
                heartbeatTimer = heartbeatInterval;
            }
        }
    }

    private async void SendHeartbeat()
    {
        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.LobbyId);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to send heartbeat: {e.Message}");
            if (e.Reason == LobbyExceptionReason.LobbyNotFound)
            {
                currentLobby = null;
                UIManager.ShowScreen(UIManager.Screen.MainMenu);
                ShowError("Lobby no longer exists");
            }
        }
    }

    private void OnLobbyDataChanged(Dictionary<string, ChangedOrRemovedLobbyValue<DataObject>> data)
    {
        foreach (var kvp in data)
        {
            if (kvp.Value.Removed)
            {
                currentLobby.Settings.Remove(kvp.Key);
            }
            else
            {
                currentLobby.Settings[kvp.Key] = kvp.Value.Value.Value;
            }
        }
        UpdateLobbyUI();
    }

    private async Task HandleLobbyJoined(Lobby lobby)
    {
        currentLobby = new LobbyData
        {
            LobbyId = lobby.Id,
            LobbyCode = lobby.LobbyCode,
            HostId = lobby.HostId,
            IsLocked = lobby.IsLocked,
            LastHeartbeat = lobby.LastUpdated,
            Settings = lobby.Data?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Value
            ) ?? new Dictionary<string, string>(),
            Players = new List<LobbyData.PlayerData>()
        };

        currentLobby.Players = lobby.Players.Select(p => new LobbyData.PlayerData(
            p.Id,
            p.Data["PlayerName"].Value,
            int.Parse(p.Data["Team"].Value),
            bool.Parse(p.Data["IsReady"].Value),
            currentLobby
        )).ToList();

        await SubscribeToLobbyEvents();

        chatMessages.Clear();
        UpdateLobbyUI();
        UIManager.ShowScreen(UIManager.Screen.LobbyRoom);
        heartbeatTimer = heartbeatInterval;
    }

    private async Task SubscribeToLobbyEvents()
    {
        if (isLobbyEventActive) return;

        try
        {
            var callbacks = new LobbyEventCallbacks();
            
            callbacks.LobbyChanged += OnLobbyChanged;
            callbacks.DataChanged += OnLobbyDataChanged;
            callbacks.LobbyEventConnectionStateChanged += OnLobbyEventConnectionStateChanged;

            lobbyEvents = await LobbyService.Instance.SubscribeToLobbyEventsAsync(currentLobby.LobbyId, callbacks);
            isLobbyEventActive = true;
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to subscribe to lobby events: {e.Message}");
            ShowError("Failed to connect to lobby. Please try again.");
        }
    }

    private async void OnLobbyChanged(ILobbyChanges changes)
    {
        if (changes.LobbyDeleted)
        {
            currentLobby = null;
            UIManager.ShowScreen(UIManager.Screen.MainMenu);
            ShowError("Lobby was deleted");
            return;
        }

        if (changes.HostId.Changed)
        {
            currentLobby.HostId = changes.HostId.Value;
        }

        if (changes.IsLocked.Changed)
        {
            currentLobby.IsLocked = changes.IsLocked.Value;
        }

        if (changes.Data.Changed)
        {
            foreach (var change in changes.Data.Value)
            {
                if (change.Value.Removed)
                {
                    currentLobby.Settings.Remove(change.Key);
                }
                else if (change.Value.Changed)
                {
                    currentLobby.Settings[change.Key] = change.Value.Value.Value;
                }
            }

            // Check if game has started
            if (currentLobby.Settings.ContainsKey("Started") && 
                currentLobby.Settings["Started"] == "true" &&
                currentLobby.Settings.ContainsKey("RelayJoinCode"))
            {
                // Load the game scene
                SceneManager.LoadScene("GameScene");

                // Join the Relay
                await JoinGame(currentLobby.Settings["RelayJoinCode"]);
                return;
            }
        }

        // Handle players who left
        if (changes.PlayerLeft.Changed)
        {
            foreach (var playerIndex in changes.PlayerLeft.Value)
            {
                var player = currentLobby.Players[playerIndex];
                currentLobby.Players.RemoveAt(playerIndex);
            }
        }

        // Handle players who joined
        if (changes.PlayerJoined.Changed)
        {
            foreach (var joinedPlayer in changes.PlayerJoined.Value)
            {
                currentLobby.Players.Add(new LobbyData.PlayerData(
                    joinedPlayer.Player.Id,
                    joinedPlayer.Player.Data["PlayerName"].Value,
                    int.Parse(joinedPlayer.Player.Data["Team"].Value),
                    bool.Parse(joinedPlayer.Player.Data["IsReady"].Value),
                    currentLobby
                ));
            }
        }

        // Handle player data changes
        if (changes.PlayerData.Changed)
        {
            foreach (var playerChange in changes.PlayerData.Value)
            {
                var player = currentLobby.Players[playerChange.Key];
                var changedData = playerChange.Value.ChangedData.Value;

                if (changedData.ContainsKey("PlayerName") && changedData["PlayerName"].Changed)
                    player.PlayerName = changedData["PlayerName"].Value.Value;
                    
                if (changedData.ContainsKey("Team") && changedData["Team"].Changed)
                    player.Team = int.Parse(changedData["Team"].Value.Value);
                
                if (changedData.ContainsKey("LastChat") && changedData["LastChat"].Changed)
                    AddChatMessage(changedData["LastChat"].Value.Value);
                    
                if (changedData.ContainsKey("IsReady") && changedData["IsReady"].Changed)
                    player.IsReady = bool.Parse(changedData["IsReady"].Value.Value);
            }
        }

        UpdateLobbyUI();
    }

    private void OnLobbyEventConnectionStateChanged(LobbyEventConnectionState state)
    {
        switch (state)
        {
            case LobbyEventConnectionState.Unsubscribed:
                Debug.Log("Unsubscribed from lobby events.");
                isLobbyEventActive = false;
                break;
            case LobbyEventConnectionState.Subscribing:
                Debug.Log("Subscribing to lobby events.");
                break;
            case LobbyEventConnectionState.Subscribed:
                Debug.Log("Subscribed to lobby events.");
                isLobbyEventActive = true;
                break;
            case LobbyEventConnectionState.Unsynced:
                Debug.Log("Lobby events connection is unsynced.");
                // Might want to attempt a resubscription here
                break;
            case LobbyEventConnectionState.Error:
                Debug.Log("Error in lobby events connection.");
                isLobbyEventActive = false;
                // Might want to attempt a resubscription after a delay
                break;
        }
    }
}