using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menu grids pack rows closer than their cell size (negative spacing), so each row's rect overlaps
/// its neighbours and a tap could land on the wrong row (tapping Return changed Music instead)
/// Limits taps on a row, and everything under it, to its own slot in the grid
/// Added automatically to the children of every overlapping GridLayoutGroup when a scene loads
/// </summary>
public class GridRowHitArea : MonoBehaviour, ICanvasRaycastFilter
{
    private GridLayoutGroup grid;
    private RectTransform rect;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookSceneLoads() => SceneManager.sceneLoaded += (_, _) => InstallInLoadedScenes();

    private static void InstallInLoadedScenes()
    {
        foreach (var layout in FindObjectsByType<GridLayoutGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (layout.spacing.x >= 0f && layout.spacing.y >= 0f) continue;

            foreach (Transform child in layout.transform)
                if (child.GetComponent<GridRowHitArea>() == null)
                    child.gameObject.AddComponent<GridRowHitArea>();
        }
    }

    private void Awake()
    {
        rect = (RectTransform)transform;
        grid = GetComponentInParent<GridLayoutGroup>(true);
    }

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (grid == null || transform.parent != grid.transform) return true;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, eventCamera, out var local))
            return true;

        // Slot = the distance between neighbouring cells, centred on this cell. Only the overlapping
        // axis is limited: controls like the toggles sit outside their row's rect on the other one
        Vector2 slot = grid.cellSize + grid.spacing;
        Vector2 offset = local - rect.rect.center;
        if (grid.spacing.x < 0f && Mathf.Abs(offset.x) > slot.x / 2f) return false;
        if (grid.spacing.y < 0f && Mathf.Abs(offset.y) > slot.y / 2f) return false;
        return true;
    }
}
