using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles instantiation, activation, and character-data application for all Pacman instances.
/// Keeps spawn/setup logic out of GameManager so it only deals with game flow.
/// Attach to the same GameObject as GameManager.
/// </summary>
public class PacmanSpawner : MonoBehaviour
{
    public static PacmanSpawner Instance { get; private set; }

    [Header("Spawn")]
    [SerializeField] private GameObject pacmanPrefab;
    [SerializeField] private Transform pacmanContainer;
    [SerializeField] private Transform pacmanSpawnPosition;

    private Pacman[] pacmans;
    private Pacman currentPacman;
    private int currentPacmanIndex = 0;
    private Animator pacmanAnimator;
    private ArrowIndicator pacmanArrowIndicator;

    private CharacterData[] selectedCharacters;
    private CharacterSkin[] selectedSkins;

    private int  totalPlayers;
    private bool isTwoPlayerMode;

    public Pacman CurrentPacman => currentPacman;
    public CharacterData CurrentCharacter => selectedCharacters?[GameManager.Instance.CurrentIndex];
    public CharacterSkin CurrentSkin => selectedSkins?[GameManager.Instance.CurrentIndex];
    public CharacterData[] SelectedCharacters => selectedCharacters;
    public CharacterSkin[] SelectedSkins => selectedSkins;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Called once from GameManager.Start. Spawns all Pacman instances,
    /// loads character/skin data from PlayerPrefs, and wires input.
    /// </summary>
    public void Initialize(int total, bool twoPlayer)
    {
        totalPlayers = total;
        isTwoPlayerMode = twoPlayer;

        pacmans = new Pacman[totalPlayers];
        selectedCharacters = new CharacterData[totalPlayers];
        selectedSkins = new CharacterSkin[totalPlayers];

        LoadCharacterSelections();
        SpawnPacmans();

        InputManager.Instance.ConfigurePacmanInputs(pacmans, isTwoPlayerMode);

        SetCurrentPacman(0);
    }

    /// <summary>
    /// Switches the active Pacman to the given player index.
    /// Deactivates the previous one and enables input on the new one.
    /// </summary>
    public void SetCurrentPacman(int playerIndex, bool activateImmediately = true)
    {
        if (playerIndex < 0 || playerIndex >= pacmans.Length || pacmans[playerIndex] == null)
        {
            Debug.LogError($"[PacmanSpawner] Invalid pacman index: {playerIndex}");
            return;
        }

        // Deactivate previous
        if (currentPacman != null)
        {
            currentPacman.gameObject.SetActive(false);
            var prev = currentPacman.GetComponent<PlayerInput>();
            if (prev != null) prev.enabled = false;
        }

        currentPacmanIndex = playerIndex;
        currentPacman      = pacmans[playerIndex];

        if (currentPacman != null)
        {
            if (activateImmediately)
                currentPacman.gameObject.SetActive(true);

            var pi = currentPacman.GetComponent<PlayerInput>();
            if (pi != null) pi.enabled = true;

            pacmanAnimator        = currentPacman.GetComponent<Animator>();
            pacmanArrowIndicator  = currentPacman.GetComponent<ArrowIndicator>();

            ApplyCharacterData();
        }
    }

    /// <summary>
    /// Re-applies animator controller and arrow color for the current player.
    /// Call after switching players or returning from character select.
    /// </summary>
    public void ApplyCharacterData()
    {
        if (selectedCharacters == null || selectedSkins == null)
        {
            Debug.LogWarning("[PacmanSpawner] Character data not initialized.");
            return;
        }

        int idx = GameManager.Instance.CurrentIndex;
        if (idx < 0 || idx >= selectedCharacters.Length)
        {
            Debug.LogWarning("[PacmanSpawner] Invalid current player index.");
            return;
        }

        var character = selectedCharacters[idx];
        var skin      = selectedSkins[idx];

        if (character == null || skin == null)
        {
            Debug.LogWarning($"[PacmanSpawner] Character or skin missing for player {idx}.");
            return;
        }

        Debug.Log($"[PacmanSpawner] Applying character data for player {idx}: {character.characterName} / {skin.skinName}");

        if (pacmanAnimator != null && skin.animatorController != null)
        {
            Debug.Log($"Pacman Animator: {skin.animatorController}");
            pacmanAnimator.runtimeAnimatorController = skin.animatorController;
        }
        else
        {
            Debug.LogWarning($"[PacmanSpawner] Animator or skin controller null for player {idx}");
        }

        if (pacmanArrowIndicator != null)
            pacmanArrowIndicator.SetColor(skin.arrowIndicatorColor);
        else
            Debug.LogWarning($"[PacmanSpawner] ArrowIndicator null for player {idx}");
    }

    private void SpawnPacmans()
    {
        if (pacmanPrefab == null)    { Debug.LogError("[PacmanSpawner] Pacman prefab not assigned!"); return; }
        if (pacmanContainer == null) { Debug.LogError("[PacmanSpawner] Pacman container not assigned!"); return; }

        for (int i = 0; i < totalPlayers; i++)
        {
            var obj = Instantiate(pacmanPrefab, pacmanSpawnPosition.position, Quaternion.identity, pacmanContainer);
            obj.name = $"Pacman_Player{i + 1}";
            obj.SetActive(false);
            pacmans[i] = obj.GetComponent<Pacman>();
        }
    }

    private void LoadCharacterSelections()
    {
        CharacterData[] allCharacters = Resources.LoadAll<CharacterData>("Characters");

        for (int i = 0; i < totalPlayers; i++)
        {
            string charName = PlayerPrefs.GetString($"SelectedCharacter_Player{i + 1}_Name", "");
            string skinName = PlayerPrefs.GetString($"SelectedCharacter_Player{i + 1}_Skin", "");

            var character = allCharacters.FirstOrDefault(c => c.characterName == charName);
            if (character != null)
            {
                selectedCharacters[i] = character;
                selectedSkins[i]      = character.GetSkinByName(skinName);
            }
        }
    }
}