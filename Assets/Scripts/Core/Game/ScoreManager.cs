using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns all score, lives, and extra-life-threshold logic for all players.
/// Fires GameEvents for UI and other systems to react to.
/// Attach to the same GameObject as GameManager.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    public class PlayerData
    {
        public int score = 0;
        public int level = 1;
        public int lives;
        public int bestRound = 1;
        public int pelletsEaten = 0;
        public HashSet<int> eatenPellets = new();
    }

    public UIManager UIManager { private get; set; }
    public LifeIconsController LifeIcons { private get; set; }

    private PlayerData[] players;
    private int totalPlayers;
    private int startingLives;
    private int currentPlayerIndex; // 0-based

    private int  highScore;
    private bool isTwoPlayerMode;

    private int currentExtraPoints = 0;
    private int nextLifeScoreThreshold  = 0;

    public int HighScore => highScore;
    public PlayerData CurrentPlayerData  => players[currentPlayerIndex];
    public PlayerData GetPlayerData(int index) => players[index];
    public int TotalPlayers => totalPlayers;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Call from GameManager.Awake before anything else.</summary>
    public void Initialize(int total, int starting, bool twoPlayer)
    {
        totalPlayers    = total;
        startingLives   = starting;
        isTwoPlayerMode = twoPlayer;

        players = new PlayerData[totalPlayers];
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
        }

        // High score
        string key = twoPlayer ? "HighScoreMultiplayer" : "HighScoreSinglePlayer";
        if (!PlayerPrefs.HasKey(key)) PlayerPrefs.SetInt(key, 0);
        highScore = PlayerPrefs.GetInt(key, 0);
    }

    public void SetCurrentPlayerIndex(int index) => currentPlayerIndex = index;

    public void AddScore(int amount)
    {
        var player = CurrentPlayerData;
        player.score += amount;

        if (player.score > highScore)
        {
            highScore = player.score;
            string key = isTwoPlayerMode ? "HighScoreMultiplayer" : "HighScoreSinglePlayer";
            PlayerPrefs.SetInt(key, highScore);
            PlayerPrefs.Save();
            UIManager?.UpdateHighScore(highScore);
        }

        int[] allScores = new int[totalPlayers];
        for (int j = 0; j < totalPlayers; j++)
            allScores[j] = players[j].score;
        UIManager?.UpdateScores(allScores);

        GameEvents.ScoreChanged(currentPlayerIndex, player.score, highScore);

        // Extra life check
        while (nextLifeScoreThreshold > 0 && player.score >= nextLifeScoreThreshold)
        {
            if (player.lives < GameConstants.MaxLives)
            {
                SetLives(player.lives + 1);
                GameEvents.ExtraLifeAwarded(currentPlayerIndex);
                Debug.Log("Extra life awarded!");
            }
            else
            {
                Debug.Log("Extra life skipped (at max lives).");
            }
            nextLifeScoreThreshold += currentExtraPoints;
        }
    }

    public void SetLives(int value)
    {
        CurrentPlayerData.lives = Mathf.Clamp(value, 1, GameConstants.MaxLives);
        Debug.Log($"[ScoreManager] SetLives for player {currentPlayerIndex}: {CurrentPlayerData.lives}");
        GameEvents.LivesChanged(currentPlayerIndex, CurrentPlayerData.lives);
    }

    public void UpdateLifeIconsUI(CharacterSkin skin)
    {
        int lives = CurrentPlayerData.lives;
        Debug.Log($"UpdateLifeIconsUI: player {currentPlayerIndex + 1}, lives = {lives}");
        LifeIcons?.UpdateIcons(Mathf.Max(0, lives - 1), skin?.lifeIconSprite);
    }

    public void UpdateBestRound()
    {
        var data = CurrentPlayerData;
        if (data.level > data.bestRound)
        {
            data.bestRound = data.level;
            PlayerPrefs.SetInt($"BestRound_P{currentPlayerIndex + 1}", data.bestRound);
            PlayerPrefs.Save();
        }
        GameEvents.RoundChanged(currentPlayerIndex, data.level, data.bestRound);
    }

    public void UpdateRoundsUI()
    {
        if (UIManager == null || players == null || players.Length == 0) return;
        int idx = Mathf.Clamp(currentPlayerIndex, 0, players.Length - 1);
        UIManager.UpdateCurrentRound(players[idx].level);
        UIManager.UpdateBestRound(players[idx].bestRound);
    }

    public void InitializeExtraLifeThreshold()
    {
        int[] values = GameConstants.ExtraPoints;
        int saved    = PlayerPrefs.GetInt(SettingsKeys.ExtraLifeThresholdKey, values[0]);
        int idx      = (saved >= 0 && saved < values.Length)
                        ? saved
                        : System.Array.IndexOf(values, saved);
        if (idx < 0) idx = 0;
        SetExtraPoints(idx);
    }

    public void SetExtraPoints(int index)
    {
        int[] values = GameConstants.ExtraPoints;
        if (index < 0 || index >= values.Length)
        {
            Debug.LogWarning($"[ScoreManager] Invalid extra point index: {index}");
            return;
        }
        currentExtraPoints = values[index];
        Debug.Log($"[ScoreManager] Extra life every {currentExtraPoints} points");
        nextLifeScoreThreshold = currentExtraPoints > 0 ? currentExtraPoints : -1;
    }

    public int GetRoundForPlayer(int playerIndex) => players[playerIndex - 1].level;
    public int GetBestRoundForPlayer(int playerIndex) => players[playerIndex - 1].bestRound;
    public HashSet<int> GetPelletIDsEaten() => CurrentPlayerData.eatenPellets;
    public int GetPelletsEaten() => CurrentPlayerData.pelletsEaten;
    public bool AllPlayersDead() => System.Linq.Enumerable.All(players, p => p.lives <= 0);
}