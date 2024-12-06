using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using System.Collections.Generic;


public static class UIManager
{
    public enum Screen
    {
        MainMenu,
        HostSettings,
        LobbyBrowser,
        LobbyRoom,
        LoadingScreen
    }

    public class ScreenConfig
    {
        public GameObject Panel { get; set; }
        public Dictionary<string, UIElement> Elements { get; set; }
    }

    public class UIElement
    {
        public GameObject GameObject { get; set; }
        public Button Button { get; set; }
        public TMP_InputField InputField { get; set; }
        public TextMeshProUGUI Text { get; set; }
        public Toggle Toggle { get; set; }
        public TMP_Dropdown Dropdown { get; set; }
        public Slider Slider { get; set; }
    }

    private static Dictionary<Screen, ScreenConfig> screens = new Dictionary<Screen, ScreenConfig>();
    private static Screen currentScreen;
    private static GameObject loadingOverlay;
    private static Transform uiRoot;

    public static void Initialize(Transform uiRoot)
    {
        UIManager.uiRoot = uiRoot;
        // Main Menu
        RegisterScreen(Screen.MainMenu, uiRoot.Find("MainMenu").gameObject, new Dictionary<string, UIElement>
        {
            {"HostButton", new UIElement { Button = GetComponent<Button>("HostButton") }},
            {"JoinButton", new UIElement { Button = GetComponent<Button>("JoinButton") }},
            {"PlayerNameInput", new UIElement { InputField = GetComponent<TMP_InputField>("PlayerNameInput") }},
            {"QuitButton", new UIElement { Button = GetComponent<Button>("QuitButton") }}
        });

        // Host Settings
        RegisterScreen(Screen.HostSettings, uiRoot.Find("HostSettings").gameObject, new Dictionary<string, UIElement>
        {
            {"LobbyNameInput", new UIElement { InputField = GetComponent<TMP_InputField>("LobbyNameInput") }},
            {"MaxPlayersSlider", new UIElement { Slider = GetComponent<Slider>("MaxPlayersSlider") }},
            {"MaxPlayersLabel", new UIElement { Text = GetComponent<TextMeshProUGUI>("MaxPlayersLabel") }},
            {"GameModeDropdown", new UIElement { Dropdown = GetComponent<TMP_Dropdown>("GameModeDropdown") }},
            {"MapDropdown", new UIElement { Dropdown = GetComponent<TMP_Dropdown>("MapDropdown") }},
            {"PrivateToggle", new UIElement { Toggle = GetComponent<Toggle>("PrivateToggle") }},
            {"CreateButton", new UIElement { Button = GetComponent<Button>("CreateButton") }},
            {"BackButtonHostSettings", new UIElement { Button = GetComponent<Button>("BackButtonHostSettings") }}
        });

        // Lobby Browser
        RegisterScreen(Screen.LobbyBrowser, uiRoot.Find("LobbyBrowser").gameObject, new Dictionary<string, UIElement>
        {
            {"LobbyList", new UIElement { GameObject = GetComponent<RectTransform>("LobbyList").gameObject }},
            {"JoinCodeInput", new UIElement { InputField = GetComponent<TMP_InputField>("JoinCodeInput") }},
            {"JoinByCodeButton", new UIElement { Button = GetComponent<Button>("JoinByCodeButton") }},
            {"RefreshButton", new UIElement { Button = GetComponent<Button>("RefreshButton") }},
            {"BackButtonLobbyBrowser", new UIElement { Button = GetComponent<Button>("BackButtonLobbyBrowser") }}
        });

        // Lobby Room
        RegisterScreen(Screen.LobbyRoom, uiRoot.Find("LobbyRoom").gameObject, new Dictionary<string, UIElement>
        {
            {"PlayerList", new UIElement { Text = GetComponent<TextMeshProUGUI>("PlayerList") }},
            {"LobbyCode", new UIElement { Text = GetComponent<TextMeshProUGUI>("LobbyCode") }},
            {"StartButton", new UIElement { Button = GetComponent<Button>("StartButton") }},
            {"ReadyButton", new UIElement { Button = GetComponent<Button>("ReadyButton") }},
            {"LeaveButton", new UIElement { Button = GetComponent<Button>("LeaveButton") }},
            {"TeamDropdown", new UIElement { Dropdown = GetComponent<TMP_Dropdown>("TeamDropdown") }},
            {"ChatInput", new UIElement { InputField = GetComponent<TMP_InputField>("ChatInput") }},
            {"ChatDisplay", new UIElement { Text = GetComponent<TextMeshProUGUI>("ChatDisplay") }},
            {"SettingsDisplay", new UIElement { Text = GetComponent<TextMeshProUGUI>("SettingsDisplay") }}
        });

        // Loading Screen
        RegisterScreen(Screen.LoadingScreen, uiRoot.Find("LoadingScreen").gameObject, new Dictionary<string, UIElement>
        {
            {"LoadingText", new UIElement { Text = GetComponent<TextMeshProUGUI>("LoadingText") }},
            {"ProgressBar", new UIElement { Slider = GetComponent<Slider>("ProgressBar") }}
        });

        loadingOverlay = uiRoot.Find("LoadingOverlay").gameObject;
        HideAllScreens();
    }

    private static T GetComponent<T>(string name) where T : Component
    {
        return uiRoot.GetComponentsInChildren<T>(true).FirstOrDefault(component => component.name == name);
    }

    private static void RegisterScreen(Screen screen, GameObject panel, Dictionary<string, UIElement> elements)
    {
        screens[screen] = new ScreenConfig
        {
            Panel = panel,
            Elements = elements
        };
    }

    public static void ShowScreen(Screen screen)
    {
        HideAllScreens();
        currentScreen = screen;
        screens[screen].Panel.SetActive(true);
    }

    private static void HideAllScreens()
    {
        foreach (var screen in screens.Values)
        {
            screen.Panel.SetActive(false);
        }
    }

    public static UIElement GetElement(string elementName)
    {
        if (screens[currentScreen].Elements.TryGetValue(elementName, out var element))
        {
            return element;
        }

        foreach (var screen in screens.Values)
        {
            if (screen.Elements.TryGetValue(elementName, out element))
            {
            return element;
            }
        }

        return null;
    }

    public static void ShowLoading(string message = "Loading...")
    {
        loadingOverlay.SetActive(true);
        var loadingText = loadingOverlay.GetComponentInChildren<TextMeshProUGUI>();
        if (loadingText != null) loadingText.text = message;
    }

    public static void HideLoading()
    {
        loadingOverlay.SetActive(false);
    }
}
