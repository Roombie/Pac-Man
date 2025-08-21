// BonusItemManager.cs
using System.Collections;
using UnityEngine;

public class BonusItemManager : MonoBehaviour
{
    [SerializeField] private Transform bonusSpawnPoint;
    [SerializeField] private GameObject[] bonusItemPrefabs;
    [SerializeField] private float bonusItemDuration = 8f;

    private GameObject currentBonusItem;
    private Coroutine removalRoutine;

    public void SpawnBonusItem(int level)
    {
        // Ensure only one exists
        DespawnBonusItem(true);

        int index = GetFruitIndexForLevel(level);
        GameObject prefab = bonusItemPrefabs[index];

        currentBonusItem = Instantiate(prefab, bonusSpawnPoint.position, Quaternion.identity);
        removalRoutine = StartCoroutine(RemoveAfterDelay());
    }

    public void DespawnBonusItem(bool immediate = false)
    {
        if (removalRoutine != null)
        {
            StopCoroutine(removalRoutine);
            removalRoutine = null;
        }

        if (currentBonusItem != null)
        {
            if (immediate) DestroyImmediate(currentBonusItem);
            else Destroy(currentBonusItem);
            currentBonusItem = null;
        }
    }

    private IEnumerator RemoveAfterDelay()
    {
        yield return new WaitForSeconds(bonusItemDuration);
        DespawnBonusItem();
    }

    private int GetFruitIndexForLevel(int currentLevel)
    {
        if (currentLevel == 1) return 0;
        if (currentLevel == 2) return 1;
        int fruitIndex = ((currentLevel - 2) / 2) + 1;
        return Mathf.Min(fruitIndex, bonusItemPrefabs.Length - 1);
    }

    private void OnDisable()
    {
        DespawnBonusItem(true);
    }
}