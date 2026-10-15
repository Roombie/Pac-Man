using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles pellet and power pellet consumption, bonus item spawn thresholds,
/// and pellet eat sounds. Attach to the same GameObject as GameManager.
/// </summary>
public class PelletHandler : MonoBehaviour
{
    public static PelletHandler Instance { get; private set; }

    private Queue<int> bonusItemThresholds = new Queue<int>(new[] { 70, 170 });
    private bool alternatePelletSound = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    #region Public API

    public void ResetThresholds()
    {
        bonusItemThresholds = new Queue<int>(new[] { 70, 170 });
        alternatePelletSound = false;
    }

    public void PelletEaten(Pellet pellet)
    {
        var gm = GameManager.Instance;

        alternatePelletSound = !alternatePelletSound;
        PlayPelletEatenSound(alternatePelletSound);

        // Arcade: Pac-Man stops 1 frame per dot and 3 frames per power pellet
        var pacman = PacmanSpawner.Instance != null ? PacmanSpawner.Instance.CurrentPacman : null;
        if (pacman != null && pacman.movement != null)
            pacman.movement.Stall((pellet is PowerPellet ? 3f : 1f) / 60f);

        ScoreManager.Instance.AddScore(pellet.points);
        gm.pelletManager.PelletEaten(pellet);

        ScoreManager.Instance.CurrentPlayerData.pelletsEaten++;
        ScoreManager.Instance.CurrentPlayerData.eatenPellets.Add(pellet.pelletID);

        CheckBonusItemSpawn();
        gm.globalGhostModeController.OnPelletCountChanged(gm.pelletManager.RemainingPelletCount());
        gm.globalGhostModeController.OnPelletEaten(); // ghost-house dot counters (power pellets count too)
    }

    public void PowerPelletEaten(PowerPellet pellet)
    {
        var gmc = GameManager.Instance.globalGhostModeController;

        bool canFlip = gmc.AnyGhostWillFlipToFrightenedNow();
        bool alreadyOn = gmc.IsFrightenedActive;
        bool homeCase = gmc.AffectHomeGhostsDuringFrightenedEnabled && gmc.AnyGhostInHome();

        if (!canFlip && !alreadyOn && !homeCase)
        {
            PelletEaten(pellet);
            return;
        }

        gmc.TriggerFrightened(); // duration comes from the level (GhostModeScheduleMatrix)
        PelletEaten(pellet);
    }

    public void PlayPelletEatenSound(bool alternate)
    {
        var chars = PacmanSpawner.Instance.SelectedCharacters;
        int idx = GameManager.Instance.CurrentIndex;
        AudioClip clip = alternate ? chars[idx].pelletEatenSound1 : chars[idx].pelletEatenSound2;
        AudioManager.Instance.Play(clip, SoundCategory.SFX);
    }

    #endregion

    #region Private

    private void CheckBonusItemSpawn()
    {
        int eaten = ScoreManager.Instance.CurrentPlayerData.pelletsEaten;
        if (bonusItemThresholds.Count > 0 && eaten == bonusItemThresholds.Peek())
        {
            GameManager.Instance.bonusItemManager.SpawnBonusItem(GameManager.Instance.CurrentRound);
            bonusItemThresholds.Dequeue();
        }
    }

    #endregion
}