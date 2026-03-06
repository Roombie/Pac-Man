using UnityEngine;

public class ScorePopupManager : MonoBehaviour
{
    [Header("Ghost Score Prefabs (by points)")]
    [SerializeField] private GameObject prefab200;
    [SerializeField] private GameObject prefab400;
    [SerializeField] private GameObject prefab800;
    [SerializeField] private GameObject prefab1600;

    public void ShowGhostScore(Vector3 worldPosition, int points)
    {
        var prefab = GetPrefab(points);
        if (prefab != null) Instantiate(prefab, worldPosition, Quaternion.identity);
        else Debug.LogWarning($"[ScorePopupManager] No prefab configured for {points} pts");
    }

    private GameObject GetPrefab(int points)
    {
        switch (points)
        {
            case 200: return prefab200;
            case 400: return prefab400;
            case 800: return prefab800;
            case 1600: return prefab1600;
            default: return null;
        }
    }
}