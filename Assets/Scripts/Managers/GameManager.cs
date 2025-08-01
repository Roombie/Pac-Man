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

    [Header("UI Elements")]
    [SerializeField] private GameObject menus;
    [SerializeField] private GameObject pauseMenu;
    [SerializeField] private GameObject optionsMenu;
    [SerializeField] private GameObject resumeButton;

    [Header("Ghost Score Prefabs")]
    public GameObject ghostScore200Prefab;
    public GameObject ghostScore400Prefab;
    public GameObject ghostScore800Prefab;
    public GameObject ghostScore1600Prefab;
    private int[] ghostComboPoints = { 200, 400, 800, 1600 };

    [Header("Controllers")]
    [SerializeField] private UIManager uiManager;
    // [SerializeField] private GhostBehaviorManager ghostBehaviorManager;
    [SerializeField] private MazeFlashController mazeFlashController;
    [SerializeField] private LifeIconsController lifeIconsController;
    [SerializeField] private FruitDisplayController fruitDisplayController;
    [SerializeField] private BonusItemManager bonusItemManager;
    [SerializeField] private PelletManager pelletManager;
    [SerializeField] private CoffeeBreakManager coffeeBreakManager;

    public enum GameState { Playing, Paused, Intermission, Transition, GameOver }
    public GameState CurrentGameState { get; private set; }

    [SerializeField] private int maxPlayers = 2;
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

    public int CurrentRound => players[currentPlayer - 1].level;
    public int BestRound => players[currentPlayer - 1].bestRound;
    public int GetPelletsEatenForCurrentPlayer() => players[currentPlayer - 1].pelletsEaten;

    public HashSet<int> GetPelletIDsEatenByCurrentPlayer() => players[currentPlayer - 1].eatenPellets;
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
    private int startingLives;
    private CharacterData[] selectedCharacters = new CharacterData[2];
    private static readonly int[] extraPointValues = GameConstants.ExtraPoints;
    public static IReadOnlyList<int> ExtraPointValues => extraPointValues;
    private int nextLifeScoreThreshold = 0;
    public event System.Action<int, int, int> OnRoundChanged; 

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

        players = new PlayerData[maxPlayers];
        for (int i = 0; i < maxPlayers; i++)
        {
            players[i] = new PlayerData();
            players[i].bestRound = PlayerPrefs.GetInt($"BestRound_P{i + 1}", 0);
        }
        
        startingLives = Mathf.Clamp(startingLives, 1, GameConstants.MaxLives);
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
        if (uiManager == null)
        {
            Debug.LogError("[GameManager] UIManager is not assigned to the inspector");
            return;
        }

        bool is2P = PlayerPrefs.GetInt(SettingsKeys.GameModeKey, 0) == 1;
        IsTwoPlayerMode = is2P;

        string key = is2P ? "HighScoreMultiplayer" : "HighScoreSinglePlayer";
        highScore = PlayerPrefs.GetInt(key, 0);

        string p1Name = PlayerPrefs.GetString("SelectedCharacter_Player1_Name", "");
        string p2Name = PlayerPrefs.GetString("SelectedCharacter_Player2_Name", "");

        CharacterData[] allCharacters = Resources.LoadAll<CharacterData>("Characters");

        foreach (var character in allCharacters)
        {
            if (character.characterName == p1Name)
                selectedCharacters[0] = character;
            if (character.characterName == p2Name)
                selectedCharacters[1] = character;
        }

        uiManager.InitializeUI(is2P);
        uiManager.UpdateHighScore(highScore);

        pelletManager.OnAllPelletsCollected += () => StartCoroutine(HandleAllPelletsCollected());
        InitializeExtraLifeThreshold();
        NewGame();

        Debug.Log($"[GameManager] Game mode is {(IsTwoPlayerMode ? "2P" : "1P")} ");
        SetState(GameState.Playing);
    }

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
        players[currentPlayer - 1].level = 1;

        bonusItemThresholds = new Queue<int>(new[] { 70, 170 });
        
        for (int i = 0; i < maxPlayers; i++)
        {
            players[i] = new PlayerData
            {
                score = 0,
                level = 1,
                lives = startingLives,
                eatenPellets = new HashSet<int>()
            };
        }

        UpdateBestRound();
        ApplyCharacterDataForCurrentPlayer();

        OnRoundChanged?.Invoke(currentPlayer - 1, CurrentRound, BestRound);

        ResetState();

        int[] initialScores = new int[maxPlayers];
        uiManager.UpdateScores(initialScores);
        uiManager.StartPlayerFlicker(currentPlayer);

        // The lives are being set on SettingsApplier.cs
        // which calls SetLives method on the GameManager and use it as soon as it detects this script on the scene

        Sprite currentLifeSprite = selectedCharacters[currentPlayer - 1].lifeIconSprite;
        lifeIconsController.CreateIcons(currentLifeSprite);
        lifeIconsController.UpdateIcons(players[currentPlayer - 1].lives - 1, currentLifeSprite);

        fruitDisplayController.RefreshFruits(players[currentPlayer - 1].level);

        StartCoroutine(StartSequence());
    }

    private IEnumerator StartSequence()
    {
        AudioManager.Instance.Play(AudioManager.Instance.gameMusic, SoundCategory.Music);

        /*foreach (Ghost ghost in ghostBehaviorManager.ghosts)
        {
            ghost.gameObject.SetActive(false);
        }*/

        pacman.gameObject.SetActive(false);
        // It'll always start the first player but whatever
        // Maybe it's easier to just put true on playerOneText and false to playerTwoText, but this is better
        uiManager.UpdateIntroText(currentPlayer - 1, IsTwoPlayerMode);
        uiManager.ShowReadyText(true);

        yield return new WaitForSeconds(StartSequenceDelay);

        uiManager.HidePlayerIntroText();

        ActivateAllGhosts();

        pacman.gameObject.SetActive(true);
        pacman.enabled = false;
        pacman.animator.speed = 0f;

        UpdateLifeIconsUI();

        yield return new WaitForSeconds(StartSequenceDelay);

        uiManager.ShowReadyText(false);
        UpdateSiren(pelletManager.RemainingPelletCount());
        pelletManager.CachePelletLayout(currentPlayer);

        pacman.animator.speed = 1f;
        pacman.enabled = true;
        pacman.movement.SetDirection(Vector2.right);
        pacman.UpdateIndicator(Vector2.right);

        StartAllGhosts();
        //ghostBehaviorManager.ResetBehaviorCycle();
    }

    private IEnumerator NewRoundSequence()
    {
        players[currentPlayer - 1].level++;
        UpdateBestRound();
        OnRoundChanged?.Invoke(currentPlayer - 1, CurrentRound, BestRound);

        players[currentPlayer - 1].pelletsEaten = 0;
        players[currentPlayer - 1].eatenPellets.Clear();
        thresholdIndex = 0;

        ResetState();
        StopAllGhosts();
        fruitDisplayController.RefreshFruits(CurrentRound);

        uiManager.ShowGameOverText(false);
        uiManager.HidePlayerIntroText();
        uiManager.ShowReadyText(true);

        pacman.gameObject.SetActive(true);
        pacman.animator.speed = 0f;
        pacman.enabled = false;

        yield return new WaitForSeconds(NewRoundDelay);

        uiManager.ShowReadyText(false);

        UpdateSiren(pelletManager.RemainingPelletCount());
        pacman.animator.speed = 1f;
        pacman.enabled = true;
        pacman.movement.SetDirection(Vector2.right);
        pacman.UpdateIndicator(Vector2.right);

        StartAllGhosts();
        SetState(GameState.Playing);
    }

    public void TogglePause()
    {
        if (CurrentGameState == GameState.Playing)
        {
            PauseGame();
        }
        else if (CurrentGameState == GameState.Paused)
        {
            ResumeGame();
        }
    }

    public void PauseGame()
    {
        Time.timeScale = 0f;
        SetState(GameState.Paused);
        menus?.SetActive(true);
        pauseMenu?.SetActive(true);
        optionsMenu?.SetActive(false);
        EventSystem.current.SetSelectedGameObject(resumeButton);
        Debug.Log("Game Paused");
    }

    public void ResumeGame()
    {
        Time.timeScale = 1f;
        SetState(GameState.Playing);
        menus?.SetActive(false);
        pauseMenu?.SetActive(false);
        optionsMenu?.SetActive(false);
        EventSystem.current.SetSelectedGameObject(resumeButton);
        Debug.Log("Game Resumed");
    }

    public void RestartLevel()
    {
        StartCoroutine(RestartLevelSequence());
    }

    private IEnumerator RestartLevelSequence()
    {
        yield return new WaitForSeconds(1f);

        ResetActorsState();

        if (IsTwoPlayerMode)
            fruitDisplayController.RefreshFruits(CurrentRound);
            ApplyCharacterDataForCurrentPlayer();

        pelletManager.RestorePelletsForPlayer(GetPelletIDsEatenByCurrentPlayer());
        uiManager.StartPlayerFlicker(currentPlayer);
        uiManager.UpdateIntroText(currentPlayer - 1, IsTwoPlayerMode);
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

        AudioManager.Instance.ResumeAll();

        StartAllGhosts();
        UpdateSiren(pelletManager.RemainingPelletCount());
    }
    #endregion

    #region Pellet & Bonus Management
    public void PelletEaten(Pellet pellet)
    {
        alternatePelletSound = !alternatePelletSound;
        AudioManager.Instance.PlayPelletEatenSound(alternatePelletSound);
        AddScore(pellet.points);
        pelletManager.PelletEaten(pellet);
        players[currentPlayer - 1].pelletsEaten++;
        players[currentPlayer - 1].eatenPellets.Add(pellet.pelletID);
        CheckBonusItemSpawn();
    }

    private void CheckBonusItemSpawn()
    {
        int pellets = players[currentPlayer - 1].pelletsEaten;
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

        yield return new WaitForSeconds(2f);

        bool flashCompleted = false;
        mazeFlashController.StartFlash(() => flashCompleted = true);

        yield return new WaitUntil(() => flashCompleted);

        yield return new WaitForSeconds(0.25f);
    }

    private IEnumerator HandleAllPelletsCollected()
    {
        AudioManager.Instance.PauseAll();

        yield return StartCoroutine(PlayMazeFlashSequence());

        if (coffeeBreakManager.HasCoffeeBreakForLevel(players[currentPlayer - 1].level))
        {
            coffeeBreakManager.StartCoffeeBreak(players[currentPlayer - 1].level);
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
        int index = currentPlayer - 1;
        players[index].lives = Mathf.Clamp(value, 0, GameConstants.MaxLives);
        
        Debug.Log($"[GameManager] SetLives for Player {currentPlayer}: {players[index].lives}");

        if (lifeIconsController != null && selectedCharacters[index] != null)
        {
            Sprite newLifeSprite = selectedCharacters[index].lifeIconSprite;
            lifeIconsController.UpdateIcons(players[index].lives - 1, newLifeSprite);
        }
        else
        {
            Debug.LogWarning("[GameManager] lifeIconsController or selectedCharacter not initialized yet.");
        }
    }

    private void UpdateBestRound()
    {
        int p = currentPlayer - 1;
        if (players[p].level > players[p].bestRound)
        {
            players[p].bestRound = players[p].level;
            PlayerPrefs.SetInt($"BestRound_P{currentPlayer}", players[p].bestRound);
            PlayerPrefs.Save();
        }
        OnRoundChanged?.Invoke(p, players[p].level, players[p].bestRound);
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
            nextLifeScoreThreshold = int.MaxValue; // Effectively disables it
    }

    public void SetHighScore(int newScore)
    {
        highScore = newScore;
    }

    public void AddScore(int amount)
    {
        int i = currentPlayer - 1;
        var player = players[i];
        
        player.score += amount;

        int[] scores = new int[maxPlayers];
        for (int j = 0; j < maxPlayers; j++)
            scores[j] = players[j].score;

        uiManager.UpdateScores(scores);

        int highestNow = players.Max(p => p.score);
        if (highestNow > highScore)
        {
            highScore = highestNow;
            string key = IsTwoPlayerMode ? "HighScoreMultiplayer" : "HighScoreSinglePlayer";
            PlayerPrefs.SetInt(key, highestNow);
            PlayerPrefs.Save();
            uiManager.UpdateHighScore(highestNow);
        }

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
        }
    }

    private void InitializeExtraLifeThreshold()
    {
        int index = PlayerPrefs.GetInt(SettingsKeys.ExtraKey, 0);

        if (index >= 0 && index < extraPointValues.Length)
        {
            currentExtraPoints = extraPointValues[index];
            nextLifeScoreThreshold = currentExtraPoints > 0 ? currentExtraPoints : int.MaxValue;
            Debug.Log($"[GameManager] Initialized extra life every {currentExtraPoints} points");
        }
        else
        {
            currentExtraPoints = 0;
            nextLifeScoreThreshold = int.MaxValue;
            Debug.LogWarning("[GameManager] Invalid extra point index from PlayerPrefs. Extra life disabled.");
        }
    }

    private void UpdateLifeIconsUI()
    {
        int lives = players[currentPlayer - 1].lives;
        Debug.Log($"UpdateLifeIconsUI: player {currentPlayer}, lives = {lives}");
        Sprite currentLifeSprite = selectedCharacters[currentPlayer - 1].lifeIconSprite;
        lifeIconsController.UpdateIcons(players[currentPlayer - 1].lives - 1, currentLifeSprite);
    }

    public void ShowGhostScore(Vector3 position, int points)
    {
        GameObject prefab = GetGhostScorePrefab(points);

        if (prefab != null)
        {
            Instantiate(prefab, position, Quaternion.identity);
        }
    }

    private GameObject GetGhostScorePrefab(int points)
    {
        switch (points)
        {
            case 200: return ghostScore200Prefab;
            case 400: return ghostScore400Prefab;
            case 800: return ghostScore800Prefab;
            case 1600: return ghostScore1600Prefab;
            default: return null;
        }
    }
    #endregion

    #region Game State & Resets
    public void ResetActorsState()
    {
        pacman.ResetState();
        /*foreach (Ghost ghost in ghostBehaviorManager.ghosts)
        {
            ghost.ResetState();
        }*/
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
        players[currentPlayer - 1].lives--; // Player loses a life

        Debug.Log($"Is Player Two mode? {IsTwoPlayerMode}");

        if (IsTwoPlayerMode) // If it's on 2P mode
        {
            UpdateLifeIconsUI(); // Once the player dies, it'll update inmediately compared to 1P, which does it after restarting the level

            StartCoroutine(HandleTurnAfterDeath());
        }
        else
        {
            if (players[currentPlayer - 1].lives > 0)
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

        int other = currentPlayer == 1 ? 2 : 1;
        bool currentAlive = players[currentPlayer - 1].lives > 0;
        bool otherAlive = players[other - 1].lives > 0;

        if (!currentAlive && !otherAlive)
            GameOver();
        else if (!currentAlive && otherAlive)
        {
            SwitchPlayerTurn();
            RestartLevel();
        }
        else
        {
            SwitchPlayerTurn();
            RestartLevel();
        }
    }

    /*public void GhostEaten(Ghost ghost)
    {
        StartCoroutine(GhostEatenSequence(ghost));
    }*/

    /*private IEnumerator GhostEatenSequence(Ghost ghost)
    {
        // Freeze Pacman and all ghosts
        pacman.isInputLocked = true;
        pacman.movement.enabled = false;

        foreach (Ghost g in ghostBehaviorManager.ghosts)
        {
            g.movement.enabled = false;
        }

        // Show points
        int comboIndex = Mathf.Clamp(ghostMultiplier - 1, 0, ghostComboPoints.Length - 1);
        int points = ghostComboPoints[comboIndex];

        AddScore(points);
        ShowGhostScore(ghost.transform.position, points);
        AudioManager.Instance.Play(ghostEaten, SoundCategory.SFX);

        ghostMultiplier++;

        // Wait one second (according to The Pacman Dossier)
        yield return new WaitForSeconds(1f);

        // The eaten ghost activates the enter home behavior
        // which set eyes mode, adjust their speed and go home to regenerate
        ghost.EnterHome();

        // Unfreeze Pacman and the other ghosts
        pacman.movement.enabled = true;
        pacman.isInputLocked = false;

        foreach (Ghost g in ghostBehaviorManager.ghosts)
        {
            // Only unfreeze the ones that weren't eaten
            if (!g.IsEaten)
            {
                g.movement.enabled = true;
            }
            else
            {
                // For other ghosts that are eaten, disable their movement while they are transitioning to home
                g.movement.enabled = false;
            }
        }
    }*/

    private void ActivateAllGhosts()
    {
        /*foreach (Ghost ghost in ghostBehaviorManager.ghosts)
        {
            ghost.gameObject.SetActive(true);
            ghost.ResetState();  // Reset the state of each ghost
        }*/
    }

    private void StartAllGhosts()
    {
        /*foreach (Ghost ghost in ghostBehaviorManager.ghosts)
        {
            ghost.enabled = true;
            ghost.movement.enabled = true;
            ghost.GetComponent<CircleCollider2D>().enabled = true;
        }*/
    }

    public void StopAllGhosts()
    {
        /*foreach (Ghost ghost in ghostBehaviorManager.ghosts)
        {
            ghost.movement.SetDirection(Vector2.zero);
            ghost.movement.enabled = false;
        }*/
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
        ReturnToMainMenu();
    }

    public void ReturnToMainMenu(bool playSound = false)
    {
        if (SceneTransitionManager.Instance != null && SceneTransitionManager.Instance.isTransitioning)
            return;

        if (playSound)
        {
            AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
        }    
        SceneTransitionManager.Instance.LoadScene("MainMenu");
    }

    public void PowerPelletEaten(PowerPellet pellet)
    {
        // Play the frightened music
        AudioManager.Instance.Play(AudioManager.Instance.frightenedMusic, SoundCategory.Music, 1f, 1f, true);

        ghostMultiplier = 1;

        /*foreach (Ghost ghost in ghostBehaviorManager.ghosts)
        {
            // Only apply frightened state to ghosts that are not already eaten or in Home
            if (ghost.IsEaten || ghost.IsInHome)
                continue;

            // Apply frightened mode for the full duration
            ghost.EnterFrightened(pellet.duration);
        }*/

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

        // Do not play siren if in frightened mode
        /*if (AnyGhostFrightened())
            return;*/

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

    /*private bool AnyGhostFrightened()
    {
        foreach (Ghost ghost in ghostBehaviorManager.ghosts)
        {
            if (ghost.ghostFrightened.enabled)
                return true;
        }

        return false;
    }*/

    private void ApplyCharacterDataForCurrentPlayer()
    {
        CharacterData data = selectedCharacters[currentPlayer - 1];

        if (pacmanAnimator == null)
            pacmanAnimator = pacman.GetComponent<Animator>();

        if (data != null)
        {
            pacmanAnimator.runtimeAnimatorController = data.playerAnimatorController;
        }
        else
        {
            Debug.LogWarning($"[GameManager] CharacterData is null for player {currentPlayer}");
        }
    }


    private void SwitchPlayerTurn()
    {
        if (!IsTwoPlayerMode) return;

        // Check if the other player still has lives
        int nextPlayer = currentPlayer == 1 ? 2 : 1;
        if (players[nextPlayer - 1].lives <= 0)
        {
            Debug.Log($"[GameManager] Player {nextPlayer} has no lives left. Staying with Player {currentPlayer}");
            return;
        }

        currentPlayer = nextPlayer;
        Debug.Log($"[GameManager] Turn switched. Now it's Player {currentPlayer}");

        OnRoundChanged?.Invoke(currentPlayer - 1, CurrentRound, BestRound);
    }
    #endregion
}