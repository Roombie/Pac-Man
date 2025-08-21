using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Core References")]
    public Pacman pacman;
    private SpriteRenderer pacmanSpriteRenderer;
    private Animator pacmanAnimator;
    private ArrowIndicator pacmanArrowIndicator;

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

    private struct FreezeState {
        public bool hard;
        public List<Behaviour> disabled; // Movement/Animator we turned off in soft freeze
    }
    private readonly Stack<FreezeState> _freeze = new();
    private float _savedTimeScale = 1f;

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
        totalPlayers = Mathf.Clamp(PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, 1), 1, 2);
        
        pacmanAnimator = pacman.GetComponent<Animator>();
        pacmanSpriteRenderer = pacman.GetComponent<SpriteRenderer>();
        pacmanArrowIndicator = pacman.GetComponent<ArrowIndicator>();

        startingLives = PlayerPrefs.GetInt(SettingsKeys.PacmanLivesKey, GameConstants.MaxLives);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        SetState(GameState.Intro);
        
        IsTwoPlayerMode = totalPlayers > 1;

        InitializePlayerScores();
        InitializePlayers();

        int extraPointsIndex = PlayerPrefs.GetInt(SettingsKeys.ExtraLifeThresholdKey, 0); // 10000
        SetExtraPoints(extraPointsIndex);

        uiManager.InitializeUI(totalPlayers);
        uiManager.UpdateHighScore(highScore);

        pelletManager.OnAllPelletsCollected += () => StartCoroutine(HandleAllPelletsCollected());
        InitializeExtraLifeThreshold();
        NewGame();

        Debug.Log($"[GameManager] Game mode is {(IsTwoPlayerMode ? "2P" : "1P")} ");
    }

    #region Game Flow
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

        globalGhostModeController.DeactivateAllGhosts();

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

        pacman.animator.speed = 1f;
        pacman.enabled = true;
        pacman.movement.SetDirection(Vector2.right);

        globalGhostModeController.StartAllGhosts();
        globalGhostModeController.SetHouseReleaseEnabled(true);
        
    }

    private IEnumerator NewRoundSequence()
    {
        bonusItemManager.DespawnBonusItem(true);
        CurrentPlayerData.level++;
        globalGhostModeController.ActivateAllGhosts();
        UpdateBestRound();
        UpdateRoundsUI();
        OnRoundChanged?.Invoke(CurrentIndex, CurrentRound, BestRound);

        CurrentPlayerData.pelletsEaten = 0;
        CurrentPlayerData.eatenPellets.Clear();
        thresholdIndex = 0;

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
        
        pacman.animator.speed = 1f;
        pacman.enabled = true;
        pacman.movement.SetDirection(Vector2.right);

        bonusItemThresholds = new Queue<int>(new[] { 70, 170 });

        globalGhostModeController.StartAllGhosts();
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
        Debug.Log("Game Paused");
    }

    private void ResumeGame()
    {
        Time.timeScale = 1f;
        pauseUI.HidePause();
        SetState(GameState.Playing);
        Debug.Log("Game Resumed");
    }

    public void RestartLevel()
    {
        StartCoroutine(RestartLevelSequence());
    }

    private IEnumerator RestartLevelSequence()
    {
        bonusItemManager.DespawnBonusItem(true);

        yield return new WaitForSeconds(1f);
    
        ResetActorsState();

        if (IsTwoPlayerMode)
        {
            fruitDisplayController.RefreshFruits(CurrentRound);
            ApplyCharacterDataForCurrentPlayer();
        }
        
        UpdateRoundsUI();

        pelletManager.RestorePelletsForPlayer(GetPelletIDsEatenByCurrentPlayer());
        uiManager.StartPlayerFlicker(CurrentIndex);
        if (IsTwoPlayerMode)
        {
            uiManager.UpdateIntroText(CurrentIndex);
        }
        uiManager.ShowReadyText(true);

        pacman.gameObject.SetActive(true);
        pacman.animator.speed = 0f;
        pacman.enabled = false;

        UpdateLifeIconsUI();

        yield return new WaitForSeconds(NewRoundDelay);

        uiManager.HidePlayerIntroText();
        uiManager.ShowReadyText(false);

        pacman.animator.speed = 1f;
        pacman.enabled = true;
        pacman.movement.SetDirection(Vector2.right);
        pacman.UpdateIndicator(Vector2.right);

        SetState(GameState.Playing);

        globalGhostModeController.StartAllGhosts();
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
        AudioManager.Instance.StopAll();
        PushFreezeHard();

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
        CurrentPlayerData.lives = Mathf.Clamp(value, 0, GameConstants.MaxLives);

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
        if (uiManager != null)
        {
            int currentRound = GetRoundForPlayer(CurrentIndex + 1);
            int bestRound = GetBestRoundForPlayer(CurrentIndex + 1);

            uiManager.UpdateCurrentRound(currentRound);
            uiManager.UpdateBestRound(bestRound);
        }
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
        int index = PlayerPrefs.GetInt(SettingsKeys.ExtraLifeThresholdKey, 0);

        if (index >= 0 && index < extraPointValues.Length)
        {
            currentExtraPoints = extraPointValues[index];
            nextLifeScoreThreshold = currentExtraPoints > 0 ? currentExtraPoints : -1;
            Debug.Log($"[GameManager] Initialized extra life every {currentExtraPoints} points");
        }
        else
        {
            currentExtraPoints = 0;
            nextLifeScoreThreshold = -1;
            Debug.LogWarning("[GameManager] Invalid extra point index from PlayerPrefs. Extra life disabled.");
        }
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
    public void PushFreezeHard()
    {
        _freeze.Push(new FreezeState { hard = true, disabled = null });
        if (_freeze.Count == 1) {
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            globalGhostModeController?.SetTimersFrozen(true); // pauses frightened/phase + flicker
        }
    }

    public void PushFreezeSoftAllowEyes()
    {
        var list = new List<Behaviour>();

        // Pause timers/flicker but keep world time running
        globalGhostModeController?.SetTimersFrozen(true);

        // Stop Pac-Man movement and animations
        if (pacman) {
            if (pacman.movement && pacman.movement.enabled) { pacman.movement.enabled = false; list.Add(pacman.movement); }
            var anim = pacman.GetComponentInChildren<Animator>();
            if (anim && anim.enabled) { anim.enabled = false; list.Add(anim); }
        }

        // Stop all ghosts EXCEPT eyes (Eaten)
        if (globalGhostModeController)
        {
            foreach (var g in globalGhostModeController.ghosts)
            {
                if (!g) continue;
                if (g.CurrentMode == Ghost.Mode.Eaten) continue; // let eyes move!

                if (g.movement && g.movement.enabled) { g.movement.enabled = false; list.Add(g.movement); }

                // pause anims on body/frightened/white
                foreach (var a in g.GetComponentsInChildren<Animator>(true))
                    if (a.enabled) { a.enabled = false; list.Add(a); }
            }
        }

        _freeze.Push(new FreezeState { hard = false, disabled = list });
    }

    public void PopFreeze()
    {
        if (_freeze.Count == 0) return;
        var top = _freeze.Pop();

        if (top.hard)
        {
            if (_freeze.Count == 0 || !_freeze.Peek().hard) {
                Time.timeScale = _freeze.Count == 0 ? _savedTimeScale : 0f;
                if (_freeze.Count == 0) {
                    globalGhostModeController?.SetTimersFrozen(false);
                    // AudioListener.pause = false;
                }
            }
        }
        else // soft
        {
            // Re-enable anything we disabled
            if (top.disabled != null)
                foreach (var b in top.disabled)
                    if (b) b.enabled = true;

            if (_freeze.Count == 0 || _freeze.Peek().hard)
            {
                // If stack is empty or next is hard, timers remain as that state dictates
                if (_freeze.Count == 0)
                    globalGhostModeController?.SetTimersFrozen(false);
            }
        }
    }

    // Safety if you change scenes/states
    public void ClearAllFreezes()
    {
        _freeze.Clear();
        Time.timeScale = 1f;
        globalGhostModeController?.SetTimersFrozen(false);
        // AudioListener.pause = false;
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
        globalGhostModeController.SetTimersFrozen(true);

        // Freeze Pacman and all ghosts
        pacman.isInputLocked = true;
        pacman.movement.enabled = false;
        pacman.animator.speed = 0f;
        pacman.gameObject.SetActive(false);

        PushFreezeSoftAllowEyes();
        var visuals = ghost.GetComponent<GhostVisuals>();
        if (visuals != null)
        {
            visuals.HideAllForScore();
        }

        // Show points
        int comboIndex = Mathf.Clamp(ghostMultiplier - 1, 0, ghostComboPoints.Length - 1);
        int points = ghostComboPoints[comboIndex];
        AddScore(points);
        scorePopupManager.ShowGhostScore(ghost.transform.position, points);
        AudioManager.Instance.Play(AudioManager.Instance.ghostEaten, SoundCategory.SFX);

        ghostMultiplier++;

        // Wait one second (according to The Pacman Dossier)
        yield return new WaitForSecondsRealtime(1f);

        // The eaten ghost activates the enter home behavior
        // which set eyes mode, adjust their speed and go home to regenerate
        ghost.SetMode(Ghost.Mode.Eaten);

        // Unfreeze Pacman and the other ghosts
        pacman.gameObject.SetActive(true);
        pacman.movement.enabled = true;
        pacman.isInputLocked = false;
        pacman.animator.speed = 1f;

        PopFreeze(); 
        globalGhostModeController.SetTimersFrozen(false);
        globalGhostModeController.ApplyModeSpeed(ghost, Ghost.Mode.Eaten);
    }

    private void GameOver()
    {
        pacman.gameObject.SetActive(false);
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

    public void PowerPelletEaten(PowerPellet pellet)
    {
        // Play the frightened music
        AudioManager.Instance.Play(AudioManager.Instance.frightenedMusic, SoundCategory.Music, 1f, 1f, true);

        ghostMultiplier = 1;

        globalGhostModeController.TriggerFrightened(pellet.duration);

        PelletEaten(pellet);  // Continue with regular pellet eating logic

        // Ensure the reset happens after the full Power Pellet duration
        CancelInvoke(nameof(ResetGhostMultiplier));
        Invoke(nameof(ResetGhostMultiplier), pellet.duration);

        // Ensure EndPowerPellet is invoked after the full duration as well
        CancelInvoke(nameof(EndPowerPellet));
        Invoke(nameof(EndPowerPellet), pellet.duration);
    }

    private void EndPowerPellet()
    {
        // Update the siren to normal mode (or according to remaining pellets)
        UpdateSiren(pelletManager.RemainingPelletCount());

        // Reset the ghost multiplier and any other necessary resets
        ResetGhostMultiplier();
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
}