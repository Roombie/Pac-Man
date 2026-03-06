using System.Collections;
using UnityEngine;

/// <summary>
/// Handles ghost-eaten sequences, frightened state responses,
/// and siren music transitions based on pellet count.
/// Attach to the same GameObject as GameManager.
/// </summary>
public class GhostInteractionHandler : MonoBehaviour
{
    public static GhostInteractionHandler Instance { get; private set; }

    [SerializeField] private ScorePopupManager scorePopupManager;

    private readonly int[] comboPoints = { 200, 400, 800, 1600 };
    private int ghostMultiplier = 1;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    #region Ghost Eaten

    public void GhostEaten(Ghost ghost)
    {
        GameManager.Instance.StartCoroutine(GhostEatenSequence(ghost));
    }

    private IEnumerator GhostEatenSequence(Ghost ghost)
    {
        var gmc = GameManager.Instance.globalGhostModeController;
        var pacman = PacmanSpawner.Instance.CurrentPacman;

        gmc.SetHomeExitsPaused(true);

        pacman.isInputLocked = true;
        pacman.movement.enabled = false;
        pacman.animator.speed = 0f;
        pacman.gameObject.SetActive(false);

        FreezeController.Instance.PushFreezeSoftAllowEyes();

        var visuals = ghost.GetComponent<GhostVisuals>();
        if (visuals) visuals.HideAllForScore();

        int comboIndex = Mathf.Clamp(ghostMultiplier - 1, 0, comboPoints.Length - 1);
        int points = comboPoints[comboIndex];

        ScoreManager.Instance.AddScore(points);
        scorePopupManager.ShowGhostScore(ghost.transform.position, points);
        AudioManager.Instance.Play(AudioManager.Instance.ghostEaten, SoundCategory.SFX);
        GameEvents.GhostEaten(ghost, points);
        ghostMultiplier++;

        yield return new WaitForSecondsRealtime(1f);

        ghost.SetMode(Ghost.Mode.Eaten);
        gmc.ApplyModeSpeed(ghost, Ghost.Mode.Eaten);

        pacman.gameObject.SetActive(true);
        pacman.movement.enabled = true;
        pacman.isInputLocked = false;
        pacman.animator.speed = 1f;

        FreezeController.Instance.PopFreeze();
        gmc.SetHomeExitsPaused(false);
    }

    #endregion

    #region Frightened

    public void OnFrightenedStarted(float duration)
    {
        ghostMultiplier = 1;
        AudioManager.Instance.Play(AudioManager.Instance.frightenedMusic, SoundCategory.Music, 1f, 1f, true);
    }

    public void OnFrightenedEnded()
    {
        UpdateSiren(GameManager.Instance.pelletManager.RemainingPelletCount());
        ghostMultiplier = 1;
    }

    #endregion

    #region Siren

    public void UpdateSiren(int pelletsRemaining)
    {
        if (GameManager.Instance.CurrentGameState != GameManager.GameState.Playing) return;
        if (pelletsRemaining <= 0) return;

        AudioClip target = pelletsRemaining > 170 ? AudioManager.Instance.firstSirenLoop
                         : pelletsRemaining > 90  ? AudioManager.Instance.secondSirenLoop
                         :                          AudioManager.Instance.thirdSirenLoop;

        if (AudioManager.Instance.CurrentMusic != target)
            AudioManager.Instance.Play(target, SoundCategory.Music, 1f, 1f, true);
    }

    #endregion
}