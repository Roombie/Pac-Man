using System;

/// <summary>
/// Central static event bus for gameplay events.
/// Scripts fire events here; listeners subscribe without needing direct references.
/// Usage:  GameEvents.OnScoreChanged += MyHandler;
///         GameEvents.ScoreChanged(playerIndex, newScore, highScore);
/// </summary>
public static class GameEvents
{
    /// <summary>Fired whenever any player's score changes. (playerIndex, newScore, highScore)</summary>
    public static event Action<int, int, int> OnScoreChanged;
    public static void ScoreChanged(int playerIndex, int newScore, int highScore)
        => OnScoreChanged?.Invoke(playerIndex, newScore, highScore);
    
    /// <summary>Fired when a player's life count changes. (playerIndex, newLives)</summary>
    public static event Action<int, int> OnLivesChanged;
    public static void LivesChanged(int playerIndex, int newLives)
        => OnLivesChanged?.Invoke(playerIndex, newLives);

    /// <summary>Fired when an extra life is awarded. (playerIndex)</summary>
    public static event Action<int> OnExtraLifeAwarded;
    public static void ExtraLifeAwarded(int playerIndex)
        => OnExtraLifeAwarded?.Invoke(playerIndex);
    
    /// <summary>Fired when the current round changes for any player. (playerIndex, round, bestRound)</summary>
    public static event Action<int, int, int> OnRoundChanged;
    public static void RoundChanged(int playerIndex, int round, int bestRound)
        => OnRoundChanged?.Invoke(playerIndex, round, bestRound);
    
    /// <summary>Fired whenever the GameState changes.</summary>
    public static event Action<GameManager.GameState> OnGameStateChanged;
    public static void GameStateChanged(GameManager.GameState state)
        => OnGameStateChanged?.Invoke(state);
    
    /// <summary>Fired in 2P mode when the active player changes. (newPlayerIndex)</summary>
    public static event Action<int> OnPlayerTurnChanged;
    public static void PlayerTurnChanged(int newPlayerIndex)
        => OnPlayerTurnChanged?.Invoke(newPlayerIndex);

    /// <summary>Fired when Pac-Man is eaten (before life deduction).</summary>
    public static event Action OnPacmanEaten;
    public static void PacmanEaten() => OnPacmanEaten?.Invoke();
    
    /// <summary>Fired when a ghost is eaten. (ghost, points)</summary>
    public static event Action<Ghost, int> OnGhostEaten;
    public static void GhostEaten(Ghost ghost, int points) => OnGhostEaten?.Invoke(ghost, points);

    /// <summary>Fired when all pellets are collected.</summary>
    public static event Action OnAllPelletsCollected;
    public static void AllPelletsCollected() => OnAllPelletsCollected?.Invoke();

    /// <summary>Fired when a freeze layer is pushed. (isHard)</summary>
    public static event Action<bool> OnFreezePushed;
    public static void FreezePushed(bool isHard) => OnFreezePushed?.Invoke(isHard);

    /// <summary>Fired when a freeze layer is popped.</summary>
    public static event Action OnFreezePopped;
    public static void FreezePopped() => OnFreezePopped?.Invoke();

    /// <summary>
    /// Clears all event subscriptions. Call this on scene unload to prevent
    /// stale references from persisting across scenes.
    /// </summary>
    public static void ClearAll()
    {
        OnScoreChanged = null;
        OnLivesChanged = null;
        OnExtraLifeAwarded = null;
        OnRoundChanged = null;
        OnGameStateChanged = null;
        OnPlayerTurnChanged = null;
        OnPacmanEaten = null;
        OnGhostEaten = null;
        OnAllPelletsCollected = null;
        OnFreezePushed = null;
        OnFreezePopped = null;
    }
}