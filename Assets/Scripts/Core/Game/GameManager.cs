using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using Roombie.UI;

/// <summary>
/// Orchestrates top-level game flow: state machine, round sequencing,
/// player turn management, and subsystem wiring.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    #region Inspector References

    [Header("Ghost & World")]
    public GlobalGhostModeController globalGhostModeController;
    [SerializeField] private MazeFlashController mazeFlashController;
    [SerializeField] private CoffeeBreakManager coffeeBreakManager;

    [Header("Managers")]
    public PelletManager pelletManager;
    public BonusItemManager bonusItemManager;

    [Header("UI")]
    [SerializeField] private UIManager uiManager;
    [SerializeField] private LifeIconsController lifeIconsController;
    [SerializeField] private FruitDisplayController fruitDisplayController;
    public DisconnectOverlayController disconnectOverlay;

    #endregion

    #region State

    public enum GameState { Intro, Playing, Paused, LevelClear, Intermission, GameOver }
    public GameState CurrentGameState { get; private set; }

    private int totalPlayers;
    public int TotalPlayers => totalPlayers;
    public int PlayerCount => Mathf.Max(1, totalPlayers);
    public bool IsTwoPlayerMode { get; private set; }

    [HideInInspector] public int currentPlayer = 1;
    public int CurrentIndex => currentPlayer - 1;

    private const float StartSequenceDelay = 2f;
    private const float NewRoundDelay = 2f;

    private static readonly int[] extraPointValues = GameConstants.ExtraPoints;
    public static IReadOnlyList<int> ExtraPointValues => extraPointValues;

    // Legacy event kept so existing external listeners don't break
    public event Action<int, int, int> OnRoundChanged;

    #endregion

    #region Pass-throughs

    // Game state is still read widely — these stay on GameManager
    public Pacman Pacman => PacmanSpawner.Instance.CurrentPacman;
    public int CurrentRound => ScoreManager.Instance.CurrentPlayerData.level;
    public int BestRound => ScoreManager.Instance.CurrentPlayerData.bestRound;
    public int highScore => ScoreManager.Instance.HighScore;

    // Kept for CoffeeBreakManager and any external UI that reads round data
    public int GetRoundForPlayer(int playerIndex) => ScoreManager.Instance.GetRoundForPlayer(playerIndex);
    public int GetBestRoundForPlayer(int playerIndex) => ScoreManager.Instance.GetBestRoundForPlayer(playerIndex);
    public HashSet<int> GetPelletIDsEatenByCurrentPlayer() => ScoreManager.Instance.GetPelletIDsEaten();

    public void UpdateLifeIconsUI()
    {
        ScoreManager.Instance.UpdateLifeIconsUI(PacmanSpawner.Instance.CurrentSkin);
    }

    // Kept for compatibility with settings/options menus
    public void SetExtraPoints(int index) => ScoreManager.Instance.SetExtraPoints(index);
    public void UpdateRoundsUI() => ScoreManager.Instance.UpdateRoundsUI();

    // Freeze — external scripts (cutscenes) may still call these
    public void PushFreeze(bool hard, bool freezeTimers = false, IEnumerable<Behaviour> toDisable = null)
        => FreezeController.Instance.PushFreeze(hard, freezeTimers, toDisable);
    public void PopFreeze() => FreezeController.Instance.PopFreeze();

    // Pause — kept so InputManager.RequestDejoin signature doesn't need changing
    public void PauseGame() => PauseMenuController.Instance.Pause();
    public void ResumeGame() => PauseMenuController.Instance.Resume();

    public void AddScore(int amount)
    {
        ScoreManager.Instance.AddScore(amount);
    }

    public int GetCurrentIndex()
    {
        return CurrentIndex;
    }

    #endregion

    #region Lifecycle

    private void OnDestroy()
    {
        if (Instance == this)
        {
            GameEvents.ClearAll();
            Instance = null;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        totalPlayers = Mathf.Max(1, PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, 1));
        IsTwoPlayerMode = totalPlayers > 1;
    }

    private void Start()
    {
        int startingLives = PlayerPrefs.GetInt(SettingsKeys.PacmanLivesKey, GameConstants.MaxLives);

        ScoreManager.Instance.Initialize(totalPlayers, startingLives, IsTwoPlayerMode);
        ScoreManager.Instance.UIManager = uiManager;
        ScoreManager.Instance.LifeIcons = lifeIconsController;

        FreezeController.Instance.GhostModeController = globalGhostModeController;
        FreezeController.Instance.GetCurrentPacman = () => PacmanSpawner.Instance.CurrentPacman;

        // Bridge GameEvents to the legacy OnRoundChanged event
        GameEvents.OnRoundChanged += (idx, round, best) => OnRoundChanged?.Invoke(idx, round, best);

        SetState(GameState.Intro);

        currentPlayer = 1;
        ScoreManager.Instance.SetCurrentPlayerIndex(CurrentIndex);
        ScoreManager.Instance.InitializeExtraLifeThreshold();

        PacmanSpawner.Instance.Initialize(totalPlayers, IsTwoPlayerMode);

        uiManager.InitializeUI(totalPlayers);
        uiManager.UpdateHighScore(highScore);

        pelletManager.OnAllPelletsCollected += () => StartCoroutine(HandleAllPelletsCollected());
        globalGhostModeController.OnFrightenedStarted += GhostInteractionHandler.Instance.OnFrightenedStarted;
        globalGhostModeController.OnFrightenedEnded += GhostInteractionHandler.Instance.OnFrightenedEnded;

        NewGame();
    }

    private void Update()
    {
        if (InputManager.Instance != null
            && InputManager.Instance.waitingForRejoin
            && CurrentGameState != GameState.Intermission)
            InputManager.Instance.PollRejoinInput();
    }

    #endregion

    #region State Machine

    public void SetState(GameState newState)
    {
        CurrentGameState = newState;
        Debug.Log($"Game State changed to: {newState}");
        GameEvents.GameStateChanged(newState);
    }

    #endregion

    #region Game Flow

    private void NewGame()
    {
        PelletHandler.Instance.ResetThresholds();
        bonusItemManager.DespawnBonusItem(true);
        ScoreManager.Instance.UpdateRoundsUI();
        ScoreManager.Instance.UpdateBestRound();
        PacmanSpawner.Instance.ApplyCharacterData();

        GameEvents.RoundChanged(CurrentIndex, CurrentRound, BestRound);
        ResetState();

        int[] initialScores = new int[totalPlayers];
        uiManager.UpdateScores(initialScores);
        for (int i = 0; i < totalPlayers; i++) uiManager.StopPlayerFlicker(i);
        uiManager.StartPlayerFlicker(CurrentIndex);

        var skin = PacmanSpawner.Instance.CurrentSkin;
        lifeIconsController.CreateIcons(skin.lifeIconSprite);
        lifeIconsController.UpdateIcons(ScoreManager.Instance.CurrentPlayerData.lives, skin.lifeIconSprite);

        fruitDisplayController.RefreshFruits(CurrentRound);

        StartCoroutine(StartSequence());
    }

    private void PreparePacmanForSequence(Pacman pacman)
    {
        pacman.gameObject.SetActive(true);
        pacman.animator.speed = 0f;
        pacman.enabled = false;
        pacman.movement.enabled = false;
        pacman.UpdateIndicator(Vector2.right);
    }

    private void ResumePacmanAfterSequence(Pacman pacman)
    {
        pacman.animator.speed = 1f;
        pacman.movement.enabled = true;
        pacman.movement.ForceStartMove(Vector2.right);
        pacman.UpdateIndicator(Vector2.right);
        pacman.enabled = true;
    }

    private IEnumerator StartSequence()
    {
        var pacman = PacmanSpawner.Instance.CurrentPacman;

        AudioManager.Instance.Play(AudioManager.Instance.gameMusic, SoundCategory.Music);
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

        PreparePacmanForSequence(pacman);
        UpdateLifeIconsUI();

        yield return new WaitForSeconds(StartSequenceDelay);

        uiManager.ShowReadyText(false);
        SetState(GameState.Playing);

        GhostInteractionHandler.Instance.UpdateSiren(pelletManager.RemainingPelletCount());
        pelletManager.CachePelletLayout(currentPlayer);

        globalGhostModeController.SetTimersFrozen(false);
        globalGhostModeController.StartAllGhosts();
        globalGhostModeController.SetEyesAudioAllowed(true);
        globalGhostModeController.SetHouseReleaseEnabled(true);

        ResumePacmanAfterSequence(pacman);
    }

    private IEnumerator NewRoundSequence()
    {
        var pacman = PacmanSpawner.Instance.CurrentPacman;

        globalGhostModeController.SetTimersFrozen(true);
        globalGhostModeController.SetEyesAudioAllowed(false);
        ResetActorsState();
        bonusItemManager.DespawnBonusItem(true);

        ScoreManager.Instance.CurrentPlayerData.level++;
        globalGhostModeController.ApplyLevel(CurrentRound);
        globalGhostModeController.ActivateAllGhosts();

        ScoreManager.Instance.UpdateBestRound();
        ScoreManager.Instance.UpdateRoundsUI();
        GameEvents.RoundChanged(CurrentIndex, CurrentRound, BestRound);

        ScoreManager.Instance.CurrentPlayerData.pelletsEaten = 0;
        ScoreManager.Instance.CurrentPlayerData.eatenPellets.Clear();
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

        PreparePacmanForSequence(pacman);

        yield return new WaitForSeconds(NewRoundDelay);

        uiManager.ShowReadyText(false);
        if (!IsTwoPlayerMode) uiManager.HidePlayerIntroText();

        SetState(GameState.Playing);

        GhostInteractionHandler.Instance.UpdateSiren(pelletManager.RemainingPelletCount());
        pelletManager.CachePelletLayout(currentPlayer);
        PelletHandler.Instance.ResetThresholds();

        globalGhostModeController.SetTimersFrozen(false);
        globalGhostModeController.StartAllGhosts();
        globalGhostModeController.SetEyesAudioAllowed(true);
        globalGhostModeController.SetHouseReleaseEnabled(true);

        ResumePacmanAfterSequence(pacman);
    }

    private void CompleteRound() => StartNextLevel();
    public void StartNextLevel() => StartCoroutine(NewRoundSequence());

    #endregion

    #region Restart & Exit

    public void RestartLevel() => StartCoroutine(RestartLevelSequence());

    private IEnumerator RestartLevelSequence()
    {
        var pacman = PacmanSpawner.Instance.CurrentPacman;

        globalGhostModeController.SetTimersFrozen(true);
        bonusItemManager.DespawnBonusItem(true);

        yield return new WaitForSeconds(1f);

        ResetState();

        if (IsTwoPlayerMode)
        {
            fruitDisplayController.RefreshFruits(CurrentRound);
            PacmanSpawner.Instance.ApplyCharacterData();
        }

        ScoreManager.Instance.UpdateRoundsUI();
        pelletManager.RestorePelletsForPlayer(GetPelletIDsEatenByCurrentPlayer());

        for (int i = 0; i < totalPlayers; i++) uiManager.StopPlayerFlicker(i);
        uiManager.StartPlayerFlicker(CurrentIndex);

        pacman.animator.speed = 0f;
        pacman.enabled = false;
        pacman.movement.enabled = false;
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
        pacman.UpdateIndicator(Vector2.right);

        yield return new WaitForSeconds(NewRoundDelay);

        if (!IsTwoPlayerMode) uiManager.HidePlayerIntroText();

        uiManager.ShowReadyText(false);
        SetState(GameState.Playing);

        ResumePacmanAfterSequence(pacman);

        globalGhostModeController.SetTimersFrozen(false);
        globalGhostModeController.StartAllGhosts();
        globalGhostModeController.SetHouseReleaseEnabled(true);
        GhostInteractionHandler.Instance.UpdateSiren(pelletManager.RemainingPelletCount());
        pelletManager.CachePelletLayout(currentPlayer);
    }

    public void ExitLevel()
    {
        if (SceneTransitionManager.Instance != null && SceneTransitionManager.Instance.isTransitioning) return;
        SceneTransitionManager.Instance.LoadScene("MainMenu");
    }

    #endregion

    #region Pacman Death & Player Turns

    public void PacmanEaten()
    {
        ScoreManager.Instance.CurrentPlayerData.lives--;
        GameEvents.PacmanEaten();

        Debug.Log($"Is two player mode? {IsTwoPlayerMode}");

        if (IsTwoPlayerMode)
        {
            UpdateLifeIconsUI();
            StartCoroutine(HandleTurnAfterDeath());
        }
        else
        {
            if (ScoreManager.Instance.CurrentPlayerData.lives > 0)
                RestartLevel();
            else
                GameOver();
        }
    }

    private IEnumerator HandleTurnAfterDeath()
    {
        yield return new WaitForSeconds(0.1f);
        SwitchPlayerTurn();
        RestartLevel();
    }

    private void SwitchPlayerTurn()
    {
        if (totalPlayers <= 1) return;

        currentPlayer = GetNextPlayerWithLives();
        if (ScoreManager.Instance.AllPlayersDead()) { GameOver(); return; }

        ScoreManager.Instance.SetCurrentPlayerIndex(CurrentIndex);
        PacmanSpawner.Instance.SetCurrentPacman(CurrentIndex, false);

        Debug.Log($"[GameManager] Turn switched. Now it's Player {currentPlayer}");
        GameEvents.PlayerTurnChanged(CurrentIndex);
        GameEvents.RoundChanged(CurrentIndex, CurrentRound, BestRound);
    }

    private int GetNextPlayerWithLives()
    {
        int next = currentPlayer;
        do { next = (next % totalPlayers) + 1; }
        while (ScoreManager.Instance.GetPlayerData(next - 1).lives <= 0);
        return next;
    }

    #endregion

    #region Game Over

    private void GameOver()
    {
        globalGhostModeController.SetEyesAudioAllowed(false);
        PacmanSpawner.Instance.CurrentPacman.gameObject.SetActive(false);
        bonusItemManager.DespawnBonusItem(true);
        uiManager.ShowGameOverText(true);
        SetState(GameState.GameOver);
        Debug.Log("Game Over!");
        StartCoroutine(GameOverSequence());
    }

    private IEnumerator GameOverSequence()
    {
        yield return new WaitForSeconds(3f);
        ExitLevel();
    }

    #endregion

    #region Resets

    public void ResetActorsState()
    {
        PacmanSpawner.Instance.CurrentPacman.ResetState();
        globalGhostModeController.ResetAllGhosts();
        globalGhostModeController.ResetElroy();
        mazeFlashController.ResetMaze();
    }

    public void ResetState()
    {
        ResetActorsState();
        pelletManager.ResetPellets(GetPelletIDsEatenByCurrentPlayer());
    }

    #endregion

    #region Level Clear

    private IEnumerator PlayMazeFlashSequence()
    {
        var pacman = PacmanSpawner.Instance.CurrentPacman;
        pacman.enabled = false;
        pacman.animator.speed = 0f;
        pacman.movement.SetDirection(Vector2.zero);

        yield return new WaitForSecondsRealtime(2f);

        bool flashDone = false;
        mazeFlashController.StartFlash(() => flashDone = true);
        globalGhostModeController.DeactivateAllGhosts();

        yield return new WaitUntil(() => flashDone);
        yield return new WaitForSecondsRealtime(0.25f);
    }

    private IEnumerator HandleAllPelletsCollected()
    {
        SetState(GameState.LevelClear);
        globalGhostModeController.SetEyesAudioAllowed(false);
        globalGhostModeController.SetHouseReleaseEnabled(false);
        AudioManager.Instance.StopAll();
        FreezeController.Instance.PushFreeze(true);

        int level = ScoreManager.Instance.CurrentPlayerData.level;

        if (coffeeBreakManager.HasCoffeeBreakForLevel(level))
            SetState(GameState.Intermission);

        yield return StartCoroutine(PlayMazeFlashSequence());

        FreezeController.Instance.PopFreeze();

        if (coffeeBreakManager.HasCoffeeBreakForLevel(level))
            coffeeBreakManager.StartCoffeeBreak(level);
        else
            CompleteRound();
    }

    #endregion
}