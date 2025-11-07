using UnityEngine;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using Roombie.UI;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Core References")]
    public GameObject pacmanPrefab;
    public Transform pacmanContainer;
    public Transform pacmanSpawnPosition;

    private Pacman[] pacmans;
    private Pacman currentPacman;
    private int currentPacmanIndex = 0;
    public Pacman Pacman => currentPacman;
    private Animator pacmanAnimator;
    private ArrowIndicator pacmanArrowIndicator;
    private InputActionAsset originalActionsAsset;

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
    public PauseUIController pauseUI;
    public DisconnectOverlayController disconnectOverlay;

    [Serializable]
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
    public int TotalPlayers => totalPlayers;
    public int PlayerCount => Mathf.Max(1, totalPlayers);
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
    public HashSet<int> GetPelletIDsEatenByCurrentPlayer() => CurrentPlayerData.eatenPellets;

    // Just in case
    public int GetPelletsEatenForCurrentPlayer() => CurrentPlayerData.pelletsEaten;
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
    [HideInInspector] public int currentPlayer = 1;
    [HideInInspector] public int CurrentIndex => currentPlayer - 1;
    private int startingLives;
    [HideInInspector] public CharacterData[] selectedCharacters;
    private CharacterSkin[] selectedSkins;
    private int[] scores;
    private static readonly int[] extraPointValues = GameConstants.ExtraPoints;
    public static IReadOnlyList<int> ExtraPointValues => extraPointValues;
    private int nextLifeScoreThreshold = 0;
    public event Action<int, int, int> OnRoundChanged;

    public CharacterSkin GetSelectedSkinForPlayer(int playerIndex)
    {
        return selectedSkins[playerIndex - 1];
    }

    public int GetCurrentIndex()
    {
        return CurrentIndex;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
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
        IsTwoPlayerMode = totalPlayers > 1;

        // Initialize pacmans array
        pacmans = new Pacman[totalPlayers];

        startingLives = PlayerPrefs.GetInt(SettingsKeys.PacmanLivesKey, GameConstants.MaxLives);
    }

    private void Start()
    {
        SetState(GameState.Intro);

        currentPlayer = 1;
        InitializePlayerScores();
        InitializePlayers();
        InitializeExtraLifeThreshold();

        CreatePacmanInstances();

        uiManager.InitializeUI(totalPlayers);
        uiManager.UpdateHighScore(highScore);
        pelletManager.OnAllPelletsCollected += () => StartCoroutine(HandleAllPelletsCollected());
        globalGhostModeController.OnFrightenedStarted += HandleFrightenedStarted;
        globalGhostModeController.OnFrightenedEnded += HandleFrightenedEnded;

        NewGame();
    }

    void Update()
    {
        if (InputManager.Instance.waitingForRejoin && InputManager.Instance != null && CurrentGameState != GameState.Intermission)
            InputManager.Instance.PollRejoinInput();
    }

    #region Initialize Pacman

    private void CreatePacmanInstances()
    {
        if (pacmanPrefab == null)
        {
            Debug.LogError("Pacman prefab is not assigned!");
            return;
        }

        if (pacmanContainer == null)
        {
            Debug.LogError("Pacman parent container is not assigned!");
            return;
        }

        for (int i = 0; i < totalPlayers; i++)
        {
            var pacmanObj = Instantiate(pacmanPrefab, pacmanSpawnPosition.position, Quaternion.identity, pacmanContainer);
            pacmanObj.name = $"Pacman_Player{i + 1}";
            pacmanObj.SetActive(false);
            pacmans[i] = pacmanObj.GetComponent<Pacman>();
        }

        InputManager.Instance.ConfigurePacmanInputs(pacmans, IsTwoPlayerMode);

        // Activa el primer jugador
        SetCurrentPacman(0);
    }


    // Add this method to switch between pacmans
    public void SetCurrentPacman(int playerIndex, bool activateInmediately = true)
    {
        // Validate index
        if (playerIndex < 0 || playerIndex >= pacmans.Length || pacmans[playerIndex] == null)
        {
            Debug.LogError($"Invalid pacman index: {playerIndex}");
            return;
        }

        // Deactivate previous pacman
        if (currentPacman != null)
        {
            currentPacman.gameObject.SetActive(false);

            // Disable input on previous pacman
            var prevPlayerInput = currentPacman.GetComponent<PlayerInput>();
            if (prevPlayerInput != null)
            {
                prevPlayerInput.enabled = false;
            }
        }

        // Update current index and activate new pacman
        currentPacmanIndex = playerIndex;
        currentPacman = pacmans[playerIndex];

        if (currentPacman != null)
        {
            if (activateInmediately)
            {
                currentPacman.gameObject.SetActive(true);
            }

            // Enable input on current pacman
            var currentPlayerInput = currentPacman.GetComponent<PlayerInput>();
            if (currentPlayerInput != null)
            {
                currentPlayerInput.enabled = true;
            }

            // Cache components for current pacman
            pacmanAnimator = currentPacman.GetComponent<Animator>();
            pacmanArrowIndicator = currentPacman.GetComponent<ArrowIndicator>();

            // Apply character data for the current player
            ApplyCharacterDataForCurrentPlayer();
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

        // Update animator controller - make sure pacmanAnimator is not null
        if (pacmanAnimator != null && skin.animatorController != null)
        {
            Debug.Log($"Pacman Animator: {skin.animatorController}");
            pacmanAnimator.runtimeAnimatorController = skin.animatorController;
        }
        else
        {
            Debug.LogWarning($"Pacman animator or skin animator controller is null for player {CurrentIndex}");
        }

        // Update arrow indicator's color
        if (pacmanArrowIndicator != null)
        {
            pacmanArrowIndicator.SetColor(skin.arrowIndicatorColor);
        }
        else
        {
            Debug.LogWarning($"Pacman arrow indicator is null for player {CurrentIndex}");
        }
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

    private void InitializePlayerScores()
    {
        string highScoreKey = IsTwoPlayerMode ? "HighScoreMultiplayer" : "HighScoreSinglePlayer";

        if (!PlayerPrefs.HasKey(highScoreKey))
        {
            PlayerPrefs.SetInt(highScoreKey, 0);
        }

        highScore = PlayerPrefs.GetInt(highScoreKey, 0);
    }

    #endregion

    #region Game Flow

    public void SetState(GameState newState)
    {
        CurrentGameState = newState;
        Debug.Log($"Game State changed to: {newState}");
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

        currentPacman.gameObject.SetActive(false);

        uiManager.UpdateIntroText(CurrentIndex);
        uiManager.ShowReadyText(true);

        yield return new WaitForSeconds(StartSequenceDelay);

        uiManager.HidePlayerIntroText();

        globalGhostModeController.ActivateAllGhosts();
        globalGhostModeController.StopAllGhosts();

        currentPacman.gameObject.SetActive(true);
        currentPacman.enabled = false;
        currentPacman.animator.speed = 0f;
        currentPacman.UpdateIndicator(Vector2.right);

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

        currentPacman.animator.speed = 1f;
        currentPacman.enabled = true;
        currentPacman.movement.SetDirection(Vector2.right);
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
        uiManager.ShowReadyText(true);

        if (IsTwoPlayerMode)
        {
            uiManager.UpdateIntroText(CurrentIndex);
            globalGhostModeController.DeactivateAllGhosts();

            yield return new WaitForSeconds(StartSequenceDelay);

            uiManager.HidePlayerIntroText();
            globalGhostModeController.ActivateAllGhosts();
        }

        currentPacman.gameObject.SetActive(true);
        currentPacman.animator.speed = 0f;
        currentPacman.enabled = false;
        currentPacman.UpdateIndicator(Vector2.right);

        yield return new WaitForSeconds(NewRoundDelay);

        uiManager.ShowReadyText(false);
        if (!IsTwoPlayerMode)
        {
            uiManager.HidePlayerIntroText();
        }

        SetState(GameState.Playing);

        UpdateSiren(pelletManager.RemainingPelletCount());
        pelletManager.CachePelletLayout(currentPlayer);
        bonusItemThresholds = new Queue<int>(new[] { 70, 170 });

        globalGhostModeController.SetTimersFrozen(false);
        globalGhostModeController.StartAllGhosts();
        globalGhostModeController.SetEyesAudioAllowed(true);
        globalGhostModeController.SetHouseReleaseEnabled(true);

        currentPacman.animator.speed = 1f;
        currentPacman.enabled = true;
        currentPacman.movement.SetDirection(Vector2.right);
    }

    public void TogglePause()
    {
        if (InputManager.Instance.waitingForRejoin) return;
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

    public void PauseGame()
    {
        Time.timeScale = 0f;
        pauseUI.ShowPause();
        if (currentPacman != null)
        {
            var playerInput = currentPacman.GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                InputManager.Instance.SwitchToUIMap(playerInput);
                Debug.Log($"[GameManager] Switched Player {currentPlayer} to UI map for pause menu");
            }
        }
        SetState(GameState.Paused);
        Debug.Log("Game Paused");
    }

    public void ResumeGame()
    {
        Time.timeScale = 1f;
        pauseUI.HidePause();
        if (currentPacman != null)
        {
            var playerInput = currentPacman.GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                InputManager.Instance.SwitchToGameplayMap(playerInput);
                Debug.Log($"[GameManager] Switched Player {currentPlayer} to UI map for pause menu");
            }
        }
        SetState(GameState.Playing);
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

        currentPacman.animator.speed = 0f;
        currentPacman.enabled = false;
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

        currentPacman.gameObject.SetActive(true);

        yield return new WaitForSeconds(NewRoundDelay);

        if (!IsTwoPlayerMode)
        {
            uiManager.HidePlayerIntroText();
        }

        uiManager.ShowReadyText(false);

        currentPacman.animator.speed = 1f;
        currentPacman.enabled = true;
        currentPacman.movement.SetDirection(Vector2.right);
        currentPacman.UpdateIndicator(Vector2.right);

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
        currentPacman.enabled = false;
        currentPacman.animator.speed = 0f;
        currentPacman.movement.SetDirection(Vector2.zero);

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

        if (currentPacman)
        {
            if (currentPacman.movement && currentPacman.movement.enabled) { currentPacman.movement.enabled = false; disabled.Add(currentPacman.movement); }
            var anim = currentPacman.GetComponentInChildren<Animator>();
            if (anim && anim.enabled) { anim.enabled = false; disabled.Add(anim); }

            var rb = currentPacman.movement ? currentPacman.movement.rb : null;
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
        currentPacman.ResetState();
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
        if (totalPlayers <= 1) return;

        currentPlayer = GetNextPlayerWithLives();
        if (players.All(p => p.lives <= 0)) { GameOver(); return; }

        // Switch to the corresponding pacman
        SetCurrentPacman(CurrentIndex, false);

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
        currentPacman.isInputLocked = true;
        currentPacman.movement.enabled = false;
        currentPacman.animator.speed = 0f;
        currentPacman.gameObject.SetActive(false);

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
        currentPacman.gameObject.SetActive(true);
        currentPacman.movement.enabled = true;
        currentPacman.isInputLocked = false;
        currentPacman.animator.speed = 1f;

        // Unfreeze ghosts
        PopFreeze();
        globalGhostModeController.SetHomeExitsPaused(false);
    }

    private void GameOver()
    {
        globalGhostModeController.SetEyesAudioAllowed(false);
        currentPacman.gameObject.SetActive(false);
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
        bool canFlipNow = globalGhostModeController.AnyGhostWillFlipToFrightenedNow();
        bool alreadyActive = globalGhostModeController.IsFrightenedActive;
        bool homeCase = globalGhostModeController.AffectHomeGhostsDuringFrightenedEnabled
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
    #endregion
}