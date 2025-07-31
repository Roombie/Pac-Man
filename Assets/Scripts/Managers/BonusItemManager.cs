using System.Collections;
using UnityEngine;

public class BonusItemManager : MonoBehaviour
{
    [SerializeField] private Transform bonusSpawnPoint;
    [SerializeField] private GameObject[] bonusItemPrefabs;
    [SerializeField] private float bonusItemDuration = 8f;

    private GameObject currentBonusItem;

    public void SpawnBonusItem(int level)
    {
        int index = GetFruitIndexForLevel(level);
        GameObject prefab = bonusItemPrefabs[index];

        currentBonusItem = Instantiate(prefab, bonusSpawnPoint.position, Quaternion.identity);
        StartCoroutine(RemoveAfterDelay());
    }

    private IEnumerator RemoveAfterDelay()
    {
        yield return new WaitForSeconds(bonusItemDuration);

        if (currentBonusItem != null)
        {
            Destroy(currentBonusItem);
        }
    }

    private int GetFruitIndexForLevel(int currentLevel)
    {
        if (currentLevel == 1) return 0;
        if (currentLevel == 2) return 1;
        int fruitIndex = ((currentLevel - 2) / 2) + 1;
        return Mathf.Min(fruitIndex, bonusItemPrefabs.Length - 1);
    }
}