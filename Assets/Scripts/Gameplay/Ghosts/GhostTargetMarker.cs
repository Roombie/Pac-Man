using UnityEngine;

/// <summary>
/// Optional helper (Options → Ghost targets): a small dashed square in the ghost's color on the tile it's steering toward
///   - Only while the ghost aims at a tile (Scatter/Chase); frightened, eyes and ghosts at home hide it
///   - Targets past the maze (Pinky/Inky ahead of Pac-Man near a wall) are clamped to its edge so the marker stays visible
/// Added to every ghost by Ghost.Awake; the marker itself is a separate object so it doesn't move with the ghost
/// </summary>
[RequireComponent(typeof(Ghost))]
public class GhostTargetMarker : MonoBehaviour
{
    /// <summary>Set from the saved setting by SettingsManager</summary>
    public static bool Visible { get; set; }

    private const int TexSize = 12;
    private const float PixelsPerUnit = 14f; // ~0.85 tiles wide, so it sits inside a corridor
    private const float Alpha = 0.85f;

    private static Sprite sprite;

    // Maze bounds from the node grid, rebuilt when the scene changes
    private static Rect mazeBounds;
    private static int boundsSceneHandle = -1;

    private Ghost ghost;
    private SpriteRenderer marker;

    private void Awake()
    {
        ghost = GetComponent<Ghost>();
    }

    private void Start()
    {
        var go = new GameObject($"{name} Target Marker");
        marker = go.AddComponent<SpriteRenderer>();
        marker.sprite = GetSprite();
        marker.color = ColorFor(ghost.Type);

        // Just under the ghost's own sprites, above the maze and pellets
        var body = GetComponentInChildren<SpriteRenderer>(true);
        if (body)
        {
            marker.sortingLayerID = body.sortingLayerID;
            marker.sortingOrder = body.sortingOrder - 1;
        }

        go.SetActive(false);
    }

    private void OnDestroy()
    {
        if (marker) Destroy(marker.gameObject);
    }

    private void OnDisable()
    {
        if (marker) marker.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!marker) return;

        Vector2 target = default;
        bool show = Visible && ShouldShow(out target);
        if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
        if (show) marker.transform.position = ClampToMaze(TileCenter(target));
    }

    private bool ShouldShow(out Vector2 target)
    {
        target = default;

        var gm = GameManager.Instance;
        if (gm == null || gm.CurrentGameState != GameManager.GameState.Playing) return false;

        var pacman = ghost.CurrentPacman;
        if (pacman == null || pacman.isDead) return false;

        return ghost.TryGetTarget(out target);
    }

    // Tiles are centered on .5 (see Movement.TileCenter)
    private static Vector2 TileCenter(Vector2 p) => new(Mathf.Floor(p.x) + 0.5f, Mathf.Floor(p.y) + 0.5f);

    private Vector2 ClampToMaze(Vector2 p)
    {
        var scene = gameObject.scene;
        if (boundsSceneHandle != scene.handle)
        {
            boundsSceneHandle = scene.handle;
            mazeBounds = Rect.zero;

            bool any = false;
            foreach (var node in FindObjectsByType<Node>(FindObjectsSortMode.None))
            {
                Vector2 n = node.transform.position;
                if (!any) { mazeBounds = new Rect(n, Vector2.zero); any = true; }
                else mazeBounds.min = Vector2.Min(mazeBounds.min, n);
                mazeBounds.max = Vector2.Max(mazeBounds.max, n);
            }
            if (!any) boundsSceneHandle = -1; // Nodes not found yet, try again next frame
        }

        if (mazeBounds.size == Vector2.zero) return p;
        return new Vector2(Mathf.Clamp(p.x, mazeBounds.xMin, mazeBounds.xMax),
                           Mathf.Clamp(p.y, mazeBounds.yMin, mazeBounds.yMax));
    }

    private static Color ColorFor(GhostType type)
    {
        Color c = type switch
        {
            GhostType.Blinky => new Color(1f, 0.1f, 0.1f),
            GhostType.Pinky  => new Color(1f, 0.72f, 1f),
            GhostType.Inky   => new Color(0f, 1f, 1f),
            GhostType.Clyde  => new Color(1f, 0.72f, 0.32f),
            _                => Color.white,
        };
        c.a = Alpha;
        return c;
    }

    /// <summary>Dashed square outline with a dot in the middle, drawn once in white and tinted per ghost</summary>
    private static Sprite GetSprite()
    {
        if (sprite) return sprite;

        var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        var pixels = new Color32[TexSize * TexSize];
        int last = TexSize - 1;
        int mid = TexSize / 2;

        for (int y = 0; y < TexSize; y++)
        for (int x = 0; x < TexSize; x++)
        {
            bool edge = x == 0 || y == 0 || x == last || y == last;
            int along = (x == 0 || x == last) ? y : x;
            bool dash = edge && Mathf.Min(along, last - along) % 4 < 2; // 2 on, 2 off from each corner
            bool dot = (x == mid || x == mid - 1) && (y == mid || y == mid - 1);

            pixels[y * TexSize + x] = (dash || dot) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        sprite = Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        sprite.name = "Ghost Target Marker";
        return sprite;
    }
}
