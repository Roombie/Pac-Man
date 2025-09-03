using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using System.Text.RegularExpressions;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.LowLevel;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Core References")]
    public Pacman pacman;
    private SpriteRenderer pacmanSpriteRenderer;
    private Animator pacmanAnimator;
    private ArrowIndicator pacmanArrowIndicator;
    private InputActionAsset originalActionsAsset;
    private InputActionAsset singlePlayerClone;
    private string prevActionMapName;
    private int _runtimeArrowCompositeIndex = -1;
    private int _runtimeUiNavigateCompositeIndex = -1;

    private int[] ghostComboPoints = { 200, 400, 800, 1600 };

    [Header("Controllers")]
    public GlobalGhostModeController globalGhostModeController;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private MazeFlashController mazeFlashController;
    [SerializeField] private LifeIconsController lifeIconsController;
    [SerializeField] private FruitDisplayController fruitDisplayController;
    [SerializeField] private BonusItemManager bonusItemManager;
    [SerializeField] private PelletManager pelletManager;
    [SerializeField] private CoffeeBreakManager coffeeBreakManager;
    [SerializeField] private ScorePopupManager scorePopupManager;
    [SerializeField] private PauseUIController pauseUI;

    [System.Serializable]
    private struct FreezeFrame
    {
        public bool hard; // hard = Time.timeScale = 0
        public bool freezeTimers; // whether we froze controller timers
        public List<Behaviour> disabled; // components this frame disabled (soft)
        public List<Rigidbody2D> pausedBodies; // pause the rigidbodies
    }

    private int timersCount = 0; // any timers -> timers frozen
    private readonly Stack<FreezeFrame> freeze = new Stack<FreezeFrame>();
    private float savedTimeScale = 1f;
    private int hardCount = 0;
    private readonly Dictionary<Behaviour, int> softDisableRef = new();

    public enum GameState { Intro, Playing, Paused, LevelClear, Intermission, GameOver }
    public GameState CurrentGameState { get; private set; }
    private int totalPlayers;
    private class PlayerData
    {
        public int score = 0;
        public int level = 1;
        public int lives;
        public int bestRound = 1;
        public int pelletsEaten = 0;
        public HashSet<int> eatenPellets = new();
    }

    private PlayerData[] players;
    private PlayerData CurrentPlayerData => players[CurrentIndex];
    public int CurrentRound => CurrentPlayerData.level;
    public int BestRound => CurrentPlayerData.bestRound;
    public int GetPelletsEatenForCurrentPlayer() => CurrentPlayerData.pelletsEaten;
    public HashSet<int> GetPelletIDsEatenByCurrentPlayer() => CurrentPlayerData.eatenPellets;
    public int GetRoundForPlayer(int playerIndex) => players[playerIndex - 1].level;
    public int GetBestRoundForPlayer(int playerIndex) => players[playerIndex - 1].bestRound;
    private int ghostMultiplier = 1;
    private int thresholdIndex = 0;
    private Queue<int> bonusItemThresholds = new Queue<int>(new List<int> { 70, 170 });

    private bool alternatePelletSound = false;

    private const float StartSequenceDelay = 2f;
    private const float NewRoundDelay = 2f;

    public int highScore { get; private set; }
    public bool IsTwoPlayerMode { get; private set; }
    private int currentExtraPoints = 0;
    private int currentPlayer = 1;
    [HideInInspector] public int CurrentIndex => currentPlayer - 1;
    private int startingLives;
    [HideInInspector] public CharacterData[] selectedCharacters;
    private CharacterSkin[] selectedSkins;
    private int[] scores;
    private static readonly int[] extraPointValues = GameConstants.ExtraPoints;
    public static IReadOnlyList<int> ExtraPointValues => extraPointValues;
    private int nextLifeScoreThreshold = 0;
    public event System.Action<int, int, int> OnRoundChanged;

    public CharacterSkin GetSelectedSkinForPlayer(int playerIndex)
    {
        return selectedSkins[playerIndex - 1];
    }

    public int GetCurrentIndex()
    {
        return CurrentIndex;
    }

    private bool listeningForUnpaired = false;
    private InputUser pacmanUser;
    private const string SchemeGamepad = "Gamepad";
    private const string SchemeKeyboardFmt = "P{0}Keyboard"; // example: P1Keyboard
    // Saved → per-slot keyboard (if authored) → P1Keyboard → Gamepad
    private string PickSchemeForSlot(PlayerInput pi, int slot, string savedScheme)
    {
        if (!string.IsNullOrEmpty(savedScheme)) return savedScheme;

        // Prefer a per-slot keyboard scheme if it exists in the asset (P1Keyboard, P2Keyboard, P3Keyboard, …)
        string perSlot = string.Format(SchemeKeyboardFmt, slot);
        foreach (var s in pi.actions.controlSchemes)
            if (s.name == perSlot) return perSlot;

        // Fallback to P1Keyboard if you haven’t added P{N}Keyboard yet
        foreach (var s in pi.actions.controlSchemes)
            if (s.name == "P1Keyboard") return "P1Keyboard";

        return SchemeGamepad;
    }

    // If you use action maps named P1/P2/P3..., enable only the active slot’s map.
    // If you use a single "Player" map, this does nothing (safe no-op).
    private void EnableOnlySlotActionMap(PlayerInput pi, int slot)
    {
        var rx = new Regex(@"^P(\d+)$");
        InputActionMap winner = null;
        foreach (var m in pi.actions.actionMaps)
        {
            var match = rx.Match(m.name);
            if (match.Success)
            {
                int num = int.Parse(match.Groups[1].Value);
                if (num == slot) winner = m;
                m.Disable();
            }
        }
        if (winner != null) winner.Enable();
    }

    private void Awake()
    {
        if (Instance != null)
        {
            DestroyImmediate(gameObject);
        }
        else
        {
            Instance = this;
        }

        // Get the amount of allowed players based on the player count key
        totalPlayers = Mathf.Max(1, PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, 1));

        // Cache pacman's component
        pacmanAnimator = pacman.GetComponent<Animator>();
        pacmanSpriteRenderer = pacman.GetComponent<SpriteRenderer>();
        pacmanArrowIndicator = pacman.GetComponent<ArrowIndicator>();
        var pi = pacman ? pacman.GetComponent<PlayerInput>() : null;
        originalActionsAsset = pi ? pi.actions : null;

        if (pi != null) // This will be worth it for single player
        {
            prevActionMapName = pi.currentActionMap?.name;
            var uiMap = pi.actions.FindActionMap("UI", throwIfNotFound: false); // Find UI action map on pacman's current input action
            if (uiMap != null) pi.SwitchCurrentActionMap(uiMap.name); // When found, switch to that action map

            // make sure arrows work in the pause menu
            if (!IsTwoPlayerMode) EnsureSinglePlayerUiNavigateComposite(pi);
        }

        startingLives = PlayerPrefs.GetInt(SettingsKeys.PacmanLivesKey, GameConstants.MaxLives);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        DisableHotSwap();
    }

    private void Start()
    {
        SetState(GameState.Intro);

        IsTwoPlayerMode = totalPlayers > 1;
        currentPlayer = 1;

        EnableHotSwap();
        InitializePlayerScores();
        InitializePlayers();
        InitializeExtraLifeThreshold();
        ApplyActivePlayerInputDevices();

        // lock in the gameplay map and wire UI module to our actions
        var pi = pacman ? pacman.GetComponent<PlayerInput>() : null;
        ForceGameplayMap(pi);
        WireUIToPlayerInput(pi);

        uiManager.InitializeUI(totalPlayers);
        uiManager.UpdateHighScore(highScore);

        pelletManager.OnAllPelletsCollected += () => StartCoroutine(HandleAllPelletsCollected());
        globalGhostModeController.OnFrightenedStarted += HandleFrightenedStarted;
        globalGhostModeController.OnFrightenedEnded   += HandleFrightenedEnded;

        NewGame();
        Debug.Log($"[GM] totalPlayers={totalPlayers}, IsTwoPlayerMode={IsTwoPlayerMode}");
    }

    #region Game Flow
    private void EnableHotSwap()
    {
        var pi = pacman ? pacman.GetComponent<PlayerInput>() : null;
        if (pi == null) return;

        pacmanUser = pi.user;

        if (!listeningForUnpaired)
        {
            // Let Input System raise events when an unpaired device is used
            InputUser.listenForUnpairedDeviceActivity++;
            InputUser.onUnpairedDeviceUsed += OnUnpairedDeviceUsed;
            listeningForUnpaired = true;
        }

        // Also react to device loss/regain for the paired user
        pi.onDeviceLost += OnPairedDeviceLost;
        pi.onDeviceRegained += OnPairedDeviceRegained;
    }

    private void DisableHotSwap()
    {
        var pi = pacman ? pacman.GetComponent<PlayerInput>() : null;
        if (pi != null)
        {
            pi.onDeviceLost -= OnPairedDeviceLost;
            pi.onDeviceRegained -= OnPairedDeviceRegained;
        }

        if (listeningForUnpaired)
        {
            InputUser.onUnpairedDeviceUsed -= OnUnpairedDeviceUsed;
            InputUser.listenForUnpairedDeviceActivity--;
            listeningForUnpaired = false;
        }
    }

    public void SetState(GameState newState)
    {
        CurrentGameState = newState;
        Debug.Log($"Game State changed to: {newState}");
    }

    private void InitializePlayers()
    {
        // Initialize player-related arrays dynamically based on totalPlayers
        players = new PlayerData[totalPlayers];
        selectedCharacters = new CharacterData[totalPlayers];
        selectedSkins = new CharacterSkin[totalPlayers];
        scores = new int[totalPlayers];

        // Initialize players' data
        for (int i = 0; i < totalPlayers; i++)
        {
            players[i] = new PlayerData
            {
                score = 0,
                level = 1,
                lives = startingLives,
                eatenPellets = new HashSet<int>(),
                bestRound = PlayerPrefs.GetInt($"BestRound_P{i + 1}", 0)
            };

            scores[i] = 0;
        }

        // Load all available characters from resources
        CharacterData[] allCharacters = Resources.LoadAll<CharacterData>("Characters");

        // Initialize selected characters and skins based on PlayerPrefs
        for (int i = 0; i < totalPlayers; i++)
        {
            string characterName = PlayerPrefs.GetString($"SelectedCharacter_Player{i + 1}_Name", "");
            string skinName = PlayerPrefs.GetString($"SelectedCharacter_Player{i + 1}_Skin", "");

            var character = allCharacters.FirstOrDefault(c => c.characterName == characterName);
            if (character != null)
            {
                selectedCharacters[i] = character;
                selectedSkins[i] = character.GetSkinByName(skinName);
            }
        }
    }

    // Ensure we’re on the gameplay map when the scene loads (in case something left us on "UI")
    private void ForceGameplayMap(PlayerInput pi)
    {
        if (pi == null || pi.actions == null) return;
        var gameplay = pi.actions.FindActionMap("Player", throwIfNotFound: false); // change name if different
        if (gameplay != null)
            pi.SwitchCurrentActionMap(gameplay.name);
    }

    // Make sure the EventSystem’s InputSystemUIInputModule uses our actions asset.
    // This covers cases where a previous scene’s module lost its reference during load.
    private void WireUIToPlayerInput(PlayerInput pi)
    {
        var es = EventSystem.current;
        if (es == null) return;

        var uiModule = es.GetComponent<InputSystemUIInputModule>();
        if (uiModule == null) return;

        // If the module isn't already using an actions asset, or it's pointing at a stale one,
        // give it Pacman's actions. This works whether your module is set to "Actions Asset" mode
        // or had its reference cleared by a scene switch.
        if (uiModule.actionsAsset == null || uiModule.actionsAsset != pi.actions)
            uiModule.actionsAsset = pi.actions;
    }

    private static InputBinding MaskByGroups(params string[] groups)
    => InputBinding.MaskByGroup(string.Join(";", groups));

    private static void EnableAllActionMaps(PlayerInput pi)
    {
        foreach (var m in pi.actions.actionMaps) m.Enable();
    }

    private void ApplyActivePlayerInputDevices()
    {
        var pi = pacman.GetComponent<PlayerInput>();
        if (pi == null) return;

        pi.neverAutoSwitchControlSchemes = true;

        int total = Mathf.Max(1, totalPlayers);
        int slot = (total == 1) ? 1 : Mathf.Clamp(currentPlayer, 1, total);

        string savedScheme = PlayerPrefs.GetString($"P{slot}_Scheme", "");
        string csv = PlayerPrefs.GetString($"P{slot}_Devices", "");

        var wantList = new List<InputDevice>();
        if (!string.IsNullOrEmpty(csv))
        {
            foreach (var part in csv.Split(','))
            {
                if (!int.TryParse(part, out var id)) continue;
                var dev = InputSystem.GetDeviceById(id);
                if (dev != null) wantList.Add(dev);
            }
        }

        string scheme = PickSchemeForSlot(pi, slot, savedScheme);
        if (wantList.Count == 0 && scheme.Contains("Keyboard") && Keyboard.current != null)
            wantList.Add(Keyboard.current);

        var user = pi.user;

        // On singleplayer, allow WASD + Arrows (no mask)
        if (total == 1)
        {
            // Use a fresh clone so nothing lingering affects resolution
            if (singlePlayerClone == null && originalActionsAsset != null)
                singlePlayerClone = ScriptableObject.Instantiate(originalActionsAsset);

            if (singlePlayerClone != null && pi.actions != singlePlayerClone)
                pi.actions = singlePlayerClone;

            // Fully reset PlayerInput filtering and re-resolve
            pi.DeactivateInput();

            // Clear any default/current scheme so PlayerInput doesn't reapply a mask
            pi.defaultControlScheme = null;
            try { user.ActivateControlScheme(null); } catch { }

            // Allow both keyboard and gamepad:
            var devs = new List<InputDevice>();
            if (Keyboard.current != null) devs.Add(Keyboard.current);
            if (Gamepad.current != null) devs.Add(Gamepad.current);
            pi.actions.devices = devs.ToArray();

            // No mask in 1P — let everything resolve
            pi.actions.bindingMask = default(InputBinding?);

            pi.ActivateInput();

            // ensure the arrow keys are always available via a neutral runtime composite
            EnsureSinglePlayerArrowComposite(pi);
            EnsureSinglePlayerUiNavigateComposite(pi);

            // Ensure action maps are enabled
            EnableAllActionMaps(pi);
            // or: EnableOnlySlotActionMap(pi, 1);

            // Debug (safe)
            var move = pi.actions.FindAction("Move", throwIfNotFound: false);
            var devsROA = pi.actions.devices;
            string devsStr = "NONE";
            if (devsROA.HasValue && devsROA.Value.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var d in devsROA.Value) { if (sb.Length > 0) sb.Append(","); sb.Append(d.layout); }
                devsStr = sb.ToString();
            }
            Debug.Log($"[GM][1P] scheme='{(pi.currentControlScheme ?? "NONE")}', " +
                    $"mask='{(pi.actions.bindingMask?.groups ?? "NONE")}', " +
                    $"devices={devsStr}, bindings={move?.bindings.Count}");

            return;
        }

        // On multiplayer, use normal per-player schemes
        // Restore the original asset if we swapped it in 1P
        if (originalActionsAsset != null && pi.actions != originalActionsAsset)
            pi.actions = originalActionsAsset;

        // remove the 1P runtime arrow composite so P1/P2 remain isolated
        RemoveSinglePlayerArrowComposite(pi);
        RemoveSinglePlayerUiNavigateComposite(pi);

        if (user.pairedDevices.Count > 0) user.UnpairDevices();

        var want = wantList.ToArray();
        foreach (var dev in want)
        {
            foreach (var u in InputUser.all)
                if (u.pairedDevices.Contains(dev)) u.UnpairDevice(dev);
            InputUser.PerformPairingWithDevice(dev, user);
        }

        if (want.Length == 0)
        {
            var gp = Gamepad.all.FirstOrDefault();
            if (gp != null)
            {
                InputUser.PerformPairingWithDevice(gp, user);
                want = new[] { gp };
                scheme = "Gamepad";
            }
        }

        pi.actions.Disable();
        pi.actions.devices = want;
        pi.actions.bindingMask = !string.IsNullOrEmpty(scheme)
                                ? InputBinding.MaskByGroup(scheme) // P1Keyboard / P2Keyboard, etc.
                                : default(InputBinding?);
        pi.actions.Enable();

        if (want.Length > 0 && !string.IsNullOrEmpty(scheme))
            pi.SwitchCurrentControlScheme(scheme, want);

        EnableOnlySlotActionMap(pi, slot);
    }

    // Add a neutral (no group) arrow 2DVector on Player/Move at runtime for single-player.
    private void EnsureSinglePlayerArrowComposite(PlayerInput pi)
    {
        var move = pi.actions.FindAction("Move", throwIfNotFound: false);
        if (move == null || _runtimeArrowCompositeIndex >= 0) return;

        bool wasEnabled = move.enabled;
        if (wasEnabled) move.Disable();

        // Add a composite that is NOT tied to any binding group
        // so it's always available regardless of scheme/mask.
        move.AddCompositeBinding("2DVector")
            .With("up", "<Keyboard>/upArrow")
            .With("down", "<Keyboard>/downArrow")
            .With("left", "<Keyboard>/leftArrow")
            .With("right", "<Keyboard>/rightArrow");

        // The composite root + 4 parts were appended at the end
        _runtimeArrowCompositeIndex = move.bindings.Count - 5;

        if (wasEnabled) move.Enable();

        Debug.Log("[GM][1P] Added runtime Arrow composite to Player/Move.");
    }

    // Remove the runtime arrow composite when leaving single-player.
    private void RemoveSinglePlayerArrowComposite(PlayerInput pi)
    {
        var move = pi.actions.FindAction("Move", throwIfNotFound: false);
        if (move == null || _runtimeArrowCompositeIndex < 0) return;

        bool wasEnabled = move.enabled;
        if (wasEnabled) move.Disable();

        // Erase parts first (right-to-left), then the composite root.
        int root = _runtimeArrowCompositeIndex;
        for (int k = 4; k >= 0; k--)
        {
            int idx = root + k;
            if (idx >= 0 && idx < move.bindings.Count)
                move.ChangeBinding(idx).Erase();
        }

        _runtimeArrowCompositeIndex = -1;

        if (wasEnabled) move.Enable();

        Debug.Log("[GM] Removed runtime Arrow composite from Player/Move.");
    }

    // Add a neutral (no-group) arrow 2DVector on UI/Navigate for single-player
    private void EnsureSinglePlayerUiNavigateComposite(PlayerInput pi)
    {
        if (pi == null || pi.actions == null) return;

        var uiMap = pi.actions.FindActionMap("UI", throwIfNotFound: false); // change name if different
        var navigate = uiMap != null ? uiMap.FindAction("Navigate", throwIfNotFound: false) : null;
        if (navigate == null || _runtimeUiNavigateCompositeIndex >= 0) return;

        bool wasEnabled = navigate.enabled;
        if (wasEnabled) navigate.Disable();

        navigate.AddCompositeBinding("2DVector")
            .With("up", "<Keyboard>/upArrow")
            .With("down", "<Keyboard>/downArrow")
            .With("left", "<Keyboard>/leftArrow")
            .With("right", "<Keyboard>/rightArrow");

        // composite root + 4 parts appended at end
        _runtimeUiNavigateCompositeIndex = navigate.bindings.Count - 5;

        if (wasEnabled) navigate.Enable();

        Debug.Log("[GM][1P] Added runtime Arrow composite to UI/Navigate.");
    }

    // Remove the runtime UI/Navigate composite when not in single-player
    private void RemoveSinglePlayerUiNavigateComposite(PlayerInput pi)
    {
        if (pi == null || pi.actions == null) return;

        var uiMap = pi.actions.FindActionMap("UI", throwIfNotFound: false);
        var navigate = uiMap != null ? uiMap.FindAction("Navigate", throwIfNotFound: false) : null;
        if (navigate == null || _runtimeUiNavigateCompositeIndex < 0) return;

        bool wasEnabled = navigate.enabled;
        if (wasEnabled) navigate.Disable();

        int root = _runtimeUiNavigateCompositeIndex;
        for (int k = 4; k >= 0; k--)
        {
            int idx = root + k;
            if (idx >= 0 && idx < navigate.bindings.Count)
                navigate.ChangeBinding(idx).Erase();
        }
        _runtimeUiNavigateCompositeIndex = -1;

        if (wasEnabled) navigate.Enable();

        Debug.Log("[GM] Removed runtime Arrow composite from UI/Navigate.");
    }

    private void InitializePlayerScores()
    {
        string highScoreKey = IsTwoPlayerMode ? "HighScoreMultiplayer" : "HighScoreSinglePlayer";

        if (!PlayerPrefs.HasKey(highScoreKey))
        {
            PlayerPrefs.SetInt(highScoreKey, 0);
        }

        highScore = PlayerPrefs.GetInt(highScoreKey, 0);
    }

    private void CompleteRound()
    {
        StartNextLevel();
    }

    public void StartNextLevel()
    {
        StartCoroutine(NewRoundSequence());
    }

    private void NewGame()
    {
        bonusItemThresholds = new Queue<int>(new[] { 70, 170 });

        bonusItemManager.DespawnBonusItem(true);
        UpdateRoundsUI();
        UpdateBestRound();
        ApplyCharacterDataForCurrentPlayer();

        OnRoundChanged?.Invoke(CurrentIndex, CurrentRound, BestRound);

        ResetState();

        int[] initialScores = new int[totalPlayers];
        uiManager.UpdateScores(initialScores);
        for (int i = 0; i < totalPlayers; i++)
            uiManager.StopPlayerFlicker(i);
        uiManager.StartPlayerFlicker(CurrentIndex);

        CharacterSkin currentSkin = GetSelectedSkinForPlayer(currentPlayer);
        Sprite currentLifeSprite = currentSkin.lifeIconSprite;
        lifeIconsController.CreateIcons(currentLifeSprite);
        lifeIconsController.UpdateIcons(CurrentPlayerData.lives, currentLifeSprite);

        fruitDisplayController.RefreshFruits(CurrentPlayerData.level);

        StartCoroutine(StartSequence());
    }

    private IEnumerator StartSequence()
    {
        AudioManager.Instance.Play(AudioManager.Instance.gameMusic, SoundCategory.Music);
        // Freeze the global timers & visuals during READY intro
        globalGhostModeController.SetTimersFrozen(true);
        globalGhostModeController.DeactivateAllGhosts();
        globalGhostModeController.SetEyesAudioAllowed(false);

        pacman.gameObject.SetActive(false);

        uiManager.UpdateIntroText(CurrentIndex);
        uiManager.ShowReadyText(true);

        yield return new WaitForSeconds(StartSequenceDelay);

        uiManager.HidePlayerIntroText();

        globalGhostModeController.ActivateAllGhosts();
        globalGhostModeController.StopAllGhosts();

        pacman.gameObject.SetActive(true);
        pacman.enabled = false;
        pacman.animator.speed = 0f;
        pacman.UpdateIndicator(Vector2.right);

        UpdateLifeIconsUI();

        yield return new WaitForSeconds(StartSequenceDelay);

        uiManager.ShowReadyText(false);

        SetState(GameState.Playing);

        UpdateSiren(pelletManager.RemainingPelletCount());
        pelletManager.CachePelletLayout(currentPlayer);

        // Unfreeze timers exactly when gameplay begins
        globalGhostModeController.SetTimersFrozen(false);
        globalGhostModeController.StartAllGhosts();
        globalGhostModeController.SetEyesAudioAllowed(true);
        globalGhostModeController.SetHouseReleaseEnabled(true);

        pacman.animator.speed = 1f;
        pacman.enabled = true;
        pacman.movement.SetDirection(Vector2.right);
    }

    private IEnumerator NewRoundSequence()
    {
        globalGhostModeController.SetTimersFrozen(true);
        globalGhostModeController.SetEyesAudioAllowed(false);
        ResetActorsState();
        bonusItemManager.DespawnBonusItem(true);
        CurrentPlayerData.level++;
        globalGhostModeController.ApplyLevel(CurrentRound);
        globalGhostModeController.ActivateAllGhosts();
        UpdateBestRound();
        UpdateRoundsUI();
        OnRoundChanged?.Invoke(CurrentIndex, CurrentRound, BestRound);

        CurrentPlayerData.pelletsEaten = 0;
        CurrentPlayerData.eatenPellets.Clear();
        thresholdIndex = 0;
        pelletManager.ResetPellets(new HashSet<int>());

        globalGhostModeController.StopAllGhosts();
        fruitDisplayController.RefreshFruits(CurrentRound);

        uiManager.ShowGameOverText(false);
        if (IsTwoPlayerMode)
        {
            uiManager.UpdateIntroText(CurrentIndex);
        }
        uiManager.ShowReadyText(true);

        pacman.gameObject.SetActive(true);
        pacman.animator.speed = 0f;
        pacman.enabled = false;
        pacman.UpdateIndicator(Vector2.right);

        yield return new WaitForSeconds(NewRoundDelay);

        uiManager.ShowReadyText(false);

        SetState(GameState.Playing);

        UpdateSiren(pelletManager.RemainingPelletCount());
        pelletManager.CachePelletLayout(currentPlayer);
        bonusItemThresholds = new Queue<int>(new[] { 70, 170 });

        globalGhostModeController.SetTimersFrozen(false);
        globalGhostModeController.StartAllGhosts();
        globalGhostModeController.SetEyesAudioAllowed(true);
        globalGhostModeController.SetHouseReleaseEnabled(true);

        pacman.animator.speed = 1f;
        pacman.enabled = true;
        pacman.movement.SetDirection(Vector2.right);
    }

    public void TogglePause()
    {
        if (CurrentGameState == GameState.Playing)
        {
            PauseGame();
        }
        else if (CurrentGameState == GameState.Paused)
        {
            AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
            ResumeGame();
        }
    }

    private void PauseGame()
    {
        Time.timeScale = 0f;
        pauseUI.ShowPause();
        SetState(GameState.Paused);

        var pi = pacman ? pacman.GetComponent<PlayerInput>() : null;
        if (pi != null)
        {
            // DON'T SwitchCurrentActionMap("UI");
            var uiMap = pi.actions.FindActionMap("UI", throwIfNotFound: false);
            if (uiMap != null) uiMap.Enable();   // enable UI alongside Player
        }
        Debug.Log("Game Paused");
    }

    private void ResumeGame()
    {
        Time.timeScale = 1f;
        pauseUI.HidePause();
        SetState(GameState.Playing);

        var pi = pacman ? pacman.GetComponent<PlayerInput>() : null;
        if (pi != null)
        {
            // Turn UI back off; keep Player map active so Pause still works
            var uiMap = pi.actions.FindActionMap("UI", throwIfNotFound: false);
            if (uiMap != null) uiMap.Disable();
        }
        Debug.Log("Game Resumed");
    }

    public void RestartLevel()
    {
        StartCoroutine(RestartLevelSequence());
    }

    private IEnumerator RestartLevelSequence()
    {
        globalGhostModeController.SetTimersFrozen(true); // Just in case, no proof but no doubts
        bonusItemManager.DespawnBonusItem(true);

        yield return new WaitForSeconds(1f);

        ResetState();

        if (IsTwoPlayerMode)
        {
            fruitDisplayController.RefreshFruits(CurrentRound);
            ApplyCharacterDataForCurrentPlayer();
        }

        UpdateRoundsUI();

        pelletManager.RestorePelletsForPlayer(GetPelletIDsEatenByCurrentPlayer());
        for (int i = 0; i < totalPlayers; i++)
            uiManager.StopPlayerFlicker(i);
        uiManager.StartPlayerFlicker(CurrentIndex);

        pacman.animator.speed = 0f;
        pacman.enabled = false;
        uiManager.ShowReadyText(true);

        UpdateLifeIconsUI();

        if (IsTwoPlayerMode)
        {
            uiManager.UpdateIntroText(CurrentIndex);
            globalGhostModeController.DeactivateAllGhosts();

            yield return new WaitForSeconds(StartSequenceDelay);

            uiManager.HidePlayerIntroText();
            globalGhostModeController.ActivateAllGhosts();
        }

        pacman.gameObject.SetActive(true);

        yield return new WaitForSeconds(NewRoundDelay);

        uiManager.HidePlayerIntroText();
        uiManager.ShowReadyText(false);

        pacman.animator.speed = 1f;
        pacman.enabled = true;
        pacman.movement.SetDirection(Vector2.right);
        pacman.UpdateIndicator(Vector2.right);

        SetState(GameState.Playing);

        globalGhostModeController.SetTimersFrozen(false);
        globalGhostModeController.StartAllGhosts();
        globalGhostModeController.SetHouseReleaseEnabled(true);
        UpdateSiren(pelletManager.RemainingPelletCount());
        pelletManager.CachePelletLayout(currentPlayer);
    }
    #endregion

    #region Pellet & Bonus Management
    public void PelletEaten(Pellet pellet)
    {
        alternatePelletSound = !alternatePelletSound;
        PlayPelletEatenSound(alternatePelletSound);
        AddScore(pellet.points);
        pelletManager.PelletEaten(pellet);
        CurrentPlayerData.pelletsEaten++;
        CurrentPlayerData.eatenPellets.Add(pellet.pelletID);
        CheckBonusItemSpawn();
        globalGhostModeController.OnPelletCountChanged(pelletManager.RemainingPelletCount());
    }

    public void PlayPelletEatenSound(bool alternate)
    {
        AudioClip clip = alternate ? selectedCharacters[CurrentIndex].pelletEatenSound1 : selectedCharacters[CurrentIndex].pelletEatenSound2;
        AudioManager.Instance.Play(clip, SoundCategory.SFX);
    }

    private void CheckBonusItemSpawn()
    {
        int pellets = CurrentPlayerData.pelletsEaten;
        if (bonusItemThresholds.Count > 0 && pellets == bonusItemThresholds.Peek())
        {
            bonusItemManager.SpawnBonusItem(CurrentRound);
            bonusItemThresholds.Dequeue();
        }
    }

    private IEnumerator PlayMazeFlashSequence()
    {
        pacman.enabled = false;
        pacman.animator.speed = 0f;
        pacman.movement.SetDirection(Vector2.zero);

        yield return new WaitForSecondsRealtime(2f);

        bool flashCompleted = false;
        mazeFlashController.StartFlash(() => flashCompleted = true);

        globalGhostModeController.DeactivateAllGhosts();

        yield return new WaitUntil(() => flashCompleted);

        yield return new WaitForSecondsRealtime(0.25f);
    }

    private IEnumerator HandleAllPelletsCollected()
    {
        SetState(GameState.LevelClear);
        globalGhostModeController.SetEyesAudioAllowed(false);
        globalGhostModeController.SetHouseReleaseEnabled(false);
        AudioManager.Instance.StopAll();
        PushFreeze(true);

        if (coffeeBreakManager.HasCoffeeBreakForLevel(CurrentPlayerData.level))
            SetState(GameState.Intermission);

        yield return StartCoroutine(PlayMazeFlashSequence());

        PopFreeze();

        if (coffeeBreakManager.HasCoffeeBreakForLevel(CurrentPlayerData.level))
        {
            coffeeBreakManager.StartCoffeeBreak(CurrentPlayerData.level);
        }
        else
        {
            CompleteRound();
        }
    }
    #endregion

    #region Score & Lives Management
    public void SetLives(int value)
    {
        // Clamp the lives to a maximum of the amount set in MaxLives
        CurrentPlayerData.lives = Mathf.Clamp(value, 1, GameConstants.MaxLives);

        Debug.Log($"[GameManager] SetLives for Player {currentPlayer}: {CurrentPlayerData.lives}");

        // Ensure life icons are updated
        if (lifeIconsController != null && selectedCharacters[CurrentIndex] != null)
        {
            Sprite newLifeSprite = selectedSkins[CurrentIndex].lifeIconSprite;
            lifeIconsController.UpdateIcons(CurrentPlayerData.lives, newLifeSprite);  // Update icons based on remaining lives
        }
        else
        {
            Debug.LogWarning("[GameManager] lifeIconsController or selectedCharacter not initialized yet.");
        }
    }

    public void UpdateRoundsUI()
    {
        if (uiManager == null || players == null || players.Length == 0) return;

        int idx = Mathf.Clamp(CurrentIndex, 0, players.Length - 1);
        uiManager.UpdateCurrentRound(players[idx].level);
        uiManager.UpdateBestRound(players[idx].bestRound);
    }


    private void UpdateBestRound()
    {
        if (CurrentPlayerData.level > CurrentPlayerData.bestRound)
        {
            CurrentPlayerData.bestRound = CurrentPlayerData.level;
            PlayerPrefs.SetInt($"BestRound_P{currentPlayer}", CurrentPlayerData.bestRound);
            PlayerPrefs.Save();
        }

        OnRoundChanged?.Invoke(CurrentIndex, CurrentPlayerData.level, CurrentPlayerData.bestRound);
    }

    public void SetExtraPoints(int index)
    {
        if (index < 0 || index >= extraPointValues.Length)
        {
            Debug.LogWarning($"[GameManager] Invalid extra point index: {index}");
            return;
        }

        currentExtraPoints = extraPointValues[index];
        Debug.Log($"[GameManager] Extra life every {currentExtraPoints} points");

        if (currentExtraPoints > 0)
            nextLifeScoreThreshold = currentExtraPoints;
        else
            nextLifeScoreThreshold = -1; // Effectively disables it
    }

    public void AddScore(int amount)
    {
        var player = CurrentPlayerData;

        player.score += amount;

        if (player.score > highScore)
        {
            highScore = player.score;
            string highScoreKey = IsTwoPlayerMode ? "HighScoreMultiplayer" : "HighScoreSinglePlayer";
            PlayerPrefs.SetInt(highScoreKey, highScore);
            PlayerPrefs.Save();

            uiManager.UpdateHighScore(highScore);
        }

        int[] scores = new int[totalPlayers];
        for (int j = 0; j < totalPlayers; j++)
            scores[j] = players[j].score;

        uiManager.UpdateScores(scores);

        int currentScore = player.score;
        while (nextLifeScoreThreshold > 0 && currentScore >= nextLifeScoreThreshold)
        {
            if (player.lives < GameConstants.MaxLives)
            {
                SetLives(player.lives + 1);
                AudioManager.Instance.Play(AudioManager.Instance.extend, SoundCategory.SFX);
                Debug.Log("Extra life awarded!");
            }
            else
            {
                Debug.Log("Extra life skipped (at max lives).");
            }

            nextLifeScoreThreshold += currentExtraPoints;
            Debug.Log($"Threshold index increased: {thresholdIndex}");
        }
    }

    private void InitializeExtraLifeThreshold()
    {
        int[] values = GameConstants.ExtraPoints;

        // Read saved value; default to first option (e.g., 10000)
        int saved = PlayerPrefs.GetInt(SettingsKeys.ExtraLifeThresholdKey, values[0]);

        // Resolve to an index (supports legacy index OR points-in-array)
        int idx = (saved >= 0 && saved < values.Length)
                    ? saved
                    : System.Array.IndexOf(values, saved);
        if (idx < 0) idx = 0;

        SetExtraPoints(idx); // single source of truth
    }

    private void UpdateLifeIconsUI()
    {
        int lives = CurrentPlayerData.lives;
        Debug.Log($"UpdateLifeIconsUI: player {currentPlayer}, lives = {lives}");
        Sprite currentLifeSprite = selectedSkins[CurrentIndex].lifeIconSprite;
        // if your UI shows "extra lives" (not the current life), subtract 1 but clamp to 0
        lifeIconsController.UpdateIcons(Mathf.Max(0, lives - 1), currentLifeSprite);
    }
    #endregion

    #region Game State & Resets
    public void PushFreeze(bool hard, bool freezeTimers = false, IEnumerable<Behaviour> toDisable = null)
    {
        var frame = new FreezeFrame { hard = hard, freezeTimers = freezeTimers, disabled = toDisable != null ? new List<Behaviour>(toDisable) : null };
        freeze.Push(frame);

        if (hard)
        {
            if (hardCount++ == 0)
                savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        if (freezeTimers && ++timersCount == 1)
            globalGhostModeController?.SetTimersFrozen(true);

        if (!hard && frame.disabled != null)
        {
            foreach (var b in frame.disabled)
            {
                if (!b) continue;
                softDisableRef.TryGetValue(b, out var n);
                softDisableRef[b] = n + 1;
                if (n == 0) b.enabled = false;
            }
        }
    }

    public void PushFreezeSoftAllowEyes()
    {
        var disabled = new List<Behaviour>();
        var bodies = new List<Rigidbody2D>();

        if (++timersCount == 1)
            globalGhostModeController?.SetTimersFrozen(true);

        if (pacman)
        {
            if (pacman.movement && pacman.movement.enabled) { pacman.movement.enabled = false; disabled.Add(pacman.movement); }
            var anim = pacman.GetComponentInChildren<Animator>();
            if (anim && anim.enabled) { anim.enabled = false; disabled.Add(anim); }

            var rb = pacman.movement ? pacman.movement.rb : null;
            if (rb && rb.simulated)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.simulated = false;
                bodies.Add(rb);
            }
        }

        if (globalGhostModeController)
        {
            foreach (var ghost in globalGhostModeController.ghosts)
            {
                if (!ghost) continue;
                if (ghost.CurrentMode == Ghost.Mode.Eaten) continue;

                if (ghost.movement && ghost.movement.enabled) { ghost.movement.enabled = false; disabled.Add(ghost.movement); }

                foreach (var a in ghost.GetComponentsInChildren<Animator>(true))
                    if (a.enabled) { a.enabled = false; disabled.Add(a); }

                var rb = ghost.movement ? ghost.movement.rb : null;
                if (rb && rb.simulated)
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                    rb.simulated = false;
                    bodies.Add(rb);
                }
            }
        }

        freeze.Push(new FreezeFrame { hard = false, freezeTimers = true, disabled = disabled, pausedBodies = bodies });
    }

    public void PopFreeze()
    {
        if (freeze.Count == 0) return;
        var top = freeze.Pop();

        if (top.hard)
        {
            if (--hardCount == 0)
                Time.timeScale = savedTimeScale;
        }
        else
        {
            // Physics back first
            if (top.pausedBodies != null)
            {
                foreach (var rb in top.pausedBodies)
                {
                    if (!rb) continue;
                    rb.angularVelocity = 0f;
                    rb.linearVelocity = Vector2.zero;
                    rb.simulated = true;
                }
            }

            // Behaviours back
            if (top.disabled != null)
            {
                foreach (var b in top.disabled)
                    if (b) b.enabled = true;
            }
        }

        // Timers unfreeze if this frame requested them and we’re the last one
        if (top.freezeTimers && --timersCount == 0)
            globalGhostModeController?.SetTimersFrozen(false);
    }

    public void ResetActorsState()
    {
        pacman.ResetState();
        globalGhostModeController.ResetAllGhosts();
        globalGhostModeController.ResetElroy();
        mazeFlashController.ResetMaze();
    }

    public void ResetState()
    {
        ResetActorsState();
        var pelletsToReactivate = GetPelletIDsEatenByCurrentPlayer();
        pelletManager.ResetPellets(pelletsToReactivate);
    }

    public void PacmanEaten()
    {
        CurrentPlayerData.lives--; // Player loses a life

        Debug.Log($"Is Player Two mode? {IsTwoPlayerMode}");

        if (IsTwoPlayerMode) // If it's on 2P mode
        {
            UpdateLifeIconsUI(); // Once the player dies, it'll update inmediately compared to 1P, which does it after restarting the level

            StartCoroutine(HandleTurnAfterDeath());
        }
        else
        {
            if (CurrentPlayerData.lives > 0)
            {
                RestartLevel();
            }
            else
            {
                GameOver();
            }
        }
    }

    private IEnumerator HandleTurnAfterDeath()
    {
        yield return new WaitForSeconds(0.1f);

        // Always change to the next player who still has lives, regardless of the current player's state
        SwitchPlayerTurn();

        // Restart the level after the turn change
        RestartLevel();
    }

    private void SwitchPlayerTurn()
    {
        if (totalPlayers <= 1) return;  // If there's only one player, don't switch turns

        // Get the next player who has lives, skipping dead players
        currentPlayer = GetNextPlayerWithLives();

        // If all players have no lives left, end the game
        if (players.All(p => p.lives <= 0))
        {
            GameOver(); // End the game if no players are left
            return;
        }

        ApplyActivePlayerInputDevices();

        // Log and invoke the round change event when the turn switches
        Debug.Log($"[GameManager] Turn switched. Now it's Player {currentPlayer}");
        OnRoundChanged?.Invoke(CurrentIndex, CurrentRound, BestRound);
    }

    private int GetNextPlayerWithLives()
    {
        int nextPlayer = currentPlayer;

        // Loop to find the next player who has lives remaining, even if it's more than one player ahead
        do
        {
            nextPlayer = (nextPlayer % totalPlayers) + 1;
        }
        while (players[nextPlayer - 1].lives <= 0); // Skip dead players

        return nextPlayer;
    }

    public void GhostEaten(Ghost ghost)
    {
        StartCoroutine(GhostEatenSequence(ghost));
    }

    private IEnumerator GhostEatenSequence(Ghost ghost)
    {
        globalGhostModeController.SetHomeExitsPaused(true);

        // Freeze Pac-Man
        pacman.isInputLocked = true;
        pacman.movement.enabled = false;
        pacman.animator.speed = 0f;
        pacman.gameObject.SetActive(false);

        // Freeze ghosts (soft) during popup; your version allows eyes logic as needed
        PushFreezeSoftAllowEyes();

        // Hide this ghost's visuals under the score popup
        var visuals = ghost.GetComponent<GhostVisuals>();
        if (visuals) visuals.HideAllForScore();

        // Score & SFX
        int comboIndex = Mathf.Clamp(ghostMultiplier - 1, 0, ghostComboPoints.Length - 1);
        int points = ghostComboPoints[comboIndex];
        AddScore(points);
        scorePopupManager.ShowGhostScore(ghost.transform.position, points);
        AudioManager.Instance.Play(AudioManager.Instance.ghostEaten, SoundCategory.SFX);

        ghostMultiplier++;

        // Arcade-accurate 1.0s pause (unscaled)
        yield return new WaitForSecondsRealtime(1f);

        // Switch to Eaten and set correct speed BEFORE unfreezing anything
        ghost.SetMode(Ghost.Mode.Eaten);
        globalGhostModeController.ApplyModeSpeed(ghost, Ghost.Mode.Eaten);

        // Bring Pac-Man back
        pacman.gameObject.SetActive(true);
        pacman.movement.enabled = true;
        pacman.isInputLocked = false;
        pacman.animator.speed = 1f;

        // Unfreeze ghosts
        PopFreeze();
        globalGhostModeController.SetHomeExitsPaused(false);
    }

    private void GameOver()
    {
        globalGhostModeController.SetEyesAudioAllowed(false);
        pacman.gameObject.SetActive(false);
        bonusItemManager.DespawnBonusItem(true);
        uiManager.ShowGameOverText(true);
        Debug.Log("Game Over!");

        StartCoroutine(GameOverSequence());
    }

    private IEnumerator GameOverSequence()
    {
        yield return new WaitForSeconds(3f);
        ExitLevel();
    }

    public void ExitLevel()
    {
        if (SceneTransitionManager.Instance != null && SceneTransitionManager.Instance.isTransitioning)
            return;

        SceneTransitionManager.Instance.LoadScene("MainMenu");
    }

    private void HandleFrightenedStarted(float duration)
    {
        // Reset combo on each new/extended frightened
        ghostMultiplier = 1;

        // Start/ensure frightened music; it will loop until the controller says it ended
        AudioManager.Instance.Play(AudioManager.Instance.frightenedMusic, SoundCategory.Music, 1f, 1f, true);
    }

    private void HandleFrightenedEnded()
    {
        // Snap music back to siren appropriate for what’s left
        UpdateSiren(pelletManager.RemainingPelletCount());
        ResetGhostMultiplier();
    }

    public void PowerPelletEaten(PowerPellet pellet)
    {
        bool canFlipNow    = globalGhostModeController.AnyGhostWillFlipToFrightenedNow();
        bool alreadyActive = globalGhostModeController.IsFrightenedActive;
        bool homeCase      = globalGhostModeController.AffectHomeGhostsDuringFrightenedEnabled
                            && globalGhostModeController.AnyGhostInHome();

        // Truly nothing to do? (no flips now, not active, no home ghosts to affect)
        if (!canFlipNow && !alreadyActive && !homeCase)
        {
            PelletEaten(pellet); // just points
            return;
        }

        // Controller owns timers/visuals; this (re)starts or extends frightened
        globalGhostModeController.TriggerFrightened(pellet.duration);

        // still score the pellet
        PelletEaten(pellet);
    }

    private void ResetGhostMultiplier()
    {
        ghostMultiplier = 1;
    }

    public void UpdateSiren(int pelletsRemaining)
    {
        if (CurrentGameState != GameState.Playing)
            return;

        if (pelletsRemaining <= 0) return;

        if (pelletsRemaining > 170)
        {
            if (AudioManager.Instance.CurrentMusic != AudioManager.Instance.firstSirenLoop)
            {
                AudioManager.Instance.Play(AudioManager.Instance.firstSirenLoop, SoundCategory.Music, 1f, 1f, true);
            }
        }
        else if (pelletsRemaining > 90)
        {
            if (AudioManager.Instance.CurrentMusic != AudioManager.Instance.secondSirenLoop)
            {
                AudioManager.Instance.Play(AudioManager.Instance.secondSirenLoop, SoundCategory.Music, 1f, 1f, true);
            }
        }
        else
        {
            if (AudioManager.Instance.CurrentMusic != AudioManager.Instance.thirdSirenLoop)
            {
                AudioManager.Instance.Play(AudioManager.Instance.thirdSirenLoop, SoundCategory.Music, 1f, 1f, true);
            }
        }
    }

    private void ApplyCharacterDataForCurrentPlayer()
    {
        if (selectedCharacters == null || selectedSkins == null)
        {
            Debug.LogWarning("[GameManager] Character data or skins not initialized.");
            return;
        }

        if (CurrentIndex < 0 || CurrentIndex >= selectedCharacters.Length)
        {
            Debug.LogWarning("[GameManager] Invalid currentPlayer index.");
            return;
        }

        CharacterData character = selectedCharacters[CurrentIndex];
        CharacterSkin skin = selectedSkins[CurrentIndex];

        if (character == null || skin == null)
        {
            Debug.LogWarning($"[GameManager] Character or skin missing for player {CurrentIndex}.");
            return;
        }

        Debug.Log($"[GameManager] Applying character data for player {CurrentIndex}: {character.characterName} / {skin.skinName}");

        // Update animator controller
        if (pacmanAnimator != null && skin.animatorController != null)
        {
            Debug.Log($"Pacman Animator: {skin.animatorController}");
            pacmanAnimator.runtimeAnimatorController = skin.animatorController;
        }

        // Update arrow indicator's color
        if (pacmanArrowIndicator != null)
        {
            pacmanArrowIndicator.SetColor(skin.arrowIndicatorColor);
        }
    }
    #endregion

    #region Gamepad
    private void OnUnpairedDeviceUsed(InputControl control, InputEventPtr eventPtr)
    {
        // Only care about Gamepads
        if (control?.device is not Gamepad gp) return;

        // In 2P mode, don't steal a pad already paired to someone else
        var owner = InputUser.FindUserPairedToDevice(gp);
        if (owner.HasValue && owner.Value.valid && owner.Value != pacmanUser)
            return;

        // Unpair any old gamepads from this user, then pair the newly-used one
        foreach (var d in pacmanUser.pairedDevices.ToArray())
            if (d is Gamepad) pacmanUser.UnpairDevice(d);

        InputUser.PerformPairingWithDevice(gp, pacmanUser);

        // Switch scheme to Gamepad for this user (keep keyboard if you want)
        var pi = pacman.GetComponent<PlayerInput>();
        if (pi != null)
        {
            var devices = new List<InputDevice>();
            if (pi.actions.devices.HasValue)
            {
                foreach (var d in pi.actions.devices.Value)
                    devices.Add(d);
            }
            if (!devices.Contains(gp)) devices.Add(gp);

            pi.actions.Disable();
            pi.actions.devices = devices.ToArray();
            pi.actions.bindingMask = default; // no mask in 1P; in 2P we re-assert scheme below
            pi.actions.Enable();

            if (IsTwoPlayerMode)
                pi.SwitchCurrentControlScheme("Gamepad", devices.Where(d => d is Gamepad).ToArray());
        }

        // Persist for the active slot so scene reloads use this controller
        int slot = Mathf.Max(1, IsTwoPlayerMode ? currentPlayer : 1);
        PlayerPrefs.SetString($"P{slot}_Scheme", "Gamepad");
        PlayerPrefs.SetString($"P{slot}_Devices", gp.deviceId.ToString());
        PlayerPrefs.Save();

        Debug.Log($"[GM] Paired new gamepad {gp.displayName} to Player {slot}.");
    }

    private void OnPairedDeviceLost(PlayerInput input)
    {
        if (CurrentGameState == GameState.Playing)
            PauseGame(); // you already have a pause menu

        Debug.Log("[GM] Controller lost. Waiting for a new one (press any button on a controller)...");
    }

    private void OnPairedDeviceRegained(PlayerInput input)
    {
        Debug.Log("[GM] Controller reconnected.");
    }
    #endregion
}