using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

/// <summary>
/// End-of-match screen for 2P: announces the winner (higher score, or a draw)
/// and both players' results. Lives in the Pacman scene as normal UI and is hidden
/// until the match ends. If a scene has none, a default one is built at runtime
/// </summary>
public class VictoryScreen : MonoBehaviour
{
    public struct PlayerResult
    {
        public int score;
        public int round;
        public string characterName;
        public Color color;
    }

    private const string Table = "GameText";

    [Header("References")]
    [SerializeField] private TMP_Text titleText;
    [Tooltip("One row per player, in player order. Extra rows are hidden.")]
    [SerializeField] private TMP_Text[] playerRows;
    [SerializeField] private TMP_Text promptText;

    [Header("Style")]
    [SerializeField] private Color drawColor = Color.white;
    [SerializeField] private Color loserColor = new Color(0.6f, 0.6f, 0.6f);
    [Tooltip("Upper-case all text to match the arcade HUD.")]
    [SerializeField] private bool uppercase = true;
    [Tooltip("Turn off if you localize the prompt yourself (e.g. with a LocalizeStringEvent).")]
    [SerializeField] private bool setPromptText = true;
    [SerializeField] private bool blinkPrompt = true;

    [Header("Timing")]
    [Tooltip("Presses are ignored for this long after the screen opens.")]
    [SerializeField] private float minShowSeconds = 1f;
    [Tooltip("Continue automatically after this many seconds (0 = wait for a press).")]
    [SerializeField] private float autoContinueSeconds = 15f;

    private bool showing;

    private void Awake()
    {
        // Saved visible in the scene so it's easy to edit; hidden until the match ends
        if (Application.isPlaying && !showing) gameObject.SetActive(false);
    }

    /// <summary>Shows the scene's victory screen (or a default one) and waits until someone confirms</summary>
    public static IEnumerator Show(PlayerResult[] results, TMP_FontAsset font)
    {
        var screen = FindAnyObjectByType<VictoryScreen>(FindObjectsInactive.Include);
        bool temporary = screen == null;
        if (temporary) screen = CreateLayout(font);

        yield return screen.Play(results);

        if (temporary) Destroy(screen.gameObject);
    }

    public IEnumerator Play(PlayerResult[] results)
    {
        showing = true;
        Fill(results);
        gameObject.SetActive(true);

        yield return WaitForConfirm();

        gameObject.SetActive(false);
        showing = false;
    }

    /// <summary>Index of the player with the highest score, or -1 on a draw</summary>
    public static int GetWinner(PlayerResult[] results)
    {
        int winner = -1;
        int best = int.MinValue;
        bool tie = false;

        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].score > best) { best = results[i].score; winner = i; tie = false; }
            else if (results[i].score == best) tie = true;
        }
        return tie ? -1 : winner;
    }

    private void Fill(PlayerResult[] results)
    {
        int winner = GetWinner(results);

        if (titleText)
        {
            titleText.text = Format(winner >= 0
                ? Loc("Victory_Winner", "Player {0} wins!", winner + 1)
                : Loc("Victory_Draw", "Draw!"));
            titleText.color = winner >= 0 ? Opaque(results[winner].color) : drawColor;
        }

        string roundLabel = Loc("Round_Text", "Round");
        for (int i = 0; i < (playerRows?.Length ?? 0); i++)
        {
            var row = playerRows[i];
            if (!row) continue;

            bool used = i < results.Length;
            row.gameObject.SetActive(used);
            if (!used) continue;

            var r = results[i];
            string playerLabel = Loc(i == 0 ? "PlayerOne_Label" : "PlayerTwo_Label", $"Player {i + 1}");
            string character = string.IsNullOrEmpty(r.characterName) ? "" : $"  {r.characterName}";
            row.text = Format($"{playerLabel}{character}   {r.score:D2}   {roundLabel} {r.round:D2}");
            row.color = (winner < 0 || winner == i) ? Opaque(r.color) : loserColor;
        }

        if (promptText && setPromptText)
            promptText.text = Format(Loc("Victory_Continue", "Press Space, / or A to continue"));
    }

    private IEnumerator WaitForConfirm()
    {
        float t = 0f;
        while (autoContinueSeconds <= 0f || t < autoContinueSeconds)
        {
            t += Time.unscaledDeltaTime;

            // Blink the prompt (unscaled so it works whatever the timeScale is)
            if (promptText && blinkPrompt)
                promptText.alpha = (Mathf.FloorToInt(t * 2f) % 2 == 0) ? 1f : 0.25f;

            if (t >= minShowSeconds && ConfirmPressed()) break;
            yield return null;
        }

        if (promptText) promptText.alpha = 1f;
    }

    // A (Confirm) for every scheme: Space (P1), / (P2), Enter, and South/Start on any gamepad
    private static bool ConfirmPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.slashKey.wasPressedThisFrame ||
                           kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            return true;

        foreach (var gp in Gamepad.all)
            if (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame) return true;

        return false;
    }

    private string Format(string s) => uppercase ? s.ToUpperInvariant() : s;

    private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

    private static string Loc(string key, string fallback, params object[] args)
    {
        try
        {
            var s = LocalizationSettings.StringDatabase.GetLocalizedString(Table, key, args);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        catch { /* table not loaded or key missing: use the English fallback */ }

        return args.Length > 0 ? string.Format(fallback, args) : fallback;
    }

    #region Default layout

    /// <summary>
    /// Builds the default layout (Canvas, dim panel, title, two player rows, prompt).
    /// Used at runtime if the scene has no VictoryScreen. Scales like the HUD canvas (800x600, match width)
    /// </summary>
    public static VictoryScreen CreateLayout(TMP_FontAsset font)
    {
        var go = new GameObject("Victory Screen");

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 600);
        scaler.matchWidthOrHeight = 0f;

        // Dim the maze behind the results
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(go.transform, false);
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        // Placeholder text so the layout reads correctly while editing
        var title = CreateText(panel.transform, "Title", "PLAYER 1 WINS!", 40f, Color.yellow, 108.33f, font);
        var row1 = CreateText(panel.transform, "Player 1 Row", "PLAYER 1  PAC-MAN   3270   ROUND 02", 22.5f, Color.yellow, 25f, font);
        var row2 = CreateText(panel.transform, "Player 2 Row", "PLAYER 2  MS. PAC-MAN   750   ROUND 01", 22.5f, new Color(0.6f, 0.6f, 0.6f), -16.67f, font);
        var prompt = CreateText(panel.transform, "Prompt", "PRESS SPACE, / OR A TO CONTINUE", 16.67f, Color.white, -125f, font);

        // Added last so Awake sees a finished layout
        var screen = go.AddComponent<VictoryScreen>();
        screen.titleText = title;
        screen.playerRows = new[] { row1, row2 };
        screen.promptText = prompt;
        return screen;
    }

    private static TMP_Text CreateText(Transform parent, string name, string text, float size, Color color, float y, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(750f, size * 1.6f);
        rect.anchoredPosition = new Vector2(0f, y);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }

    #endregion
}
