using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;

public class FruitDisplayController : MonoBehaviour
{
    [SerializeField] private Transform fruitDisplayPanel;
    [SerializeField] private GameObject fruitIconPrefab;
    [SerializeField] private Sprite[] fruitSprites;

    private readonly List<Image> fruitSlots = new();
    private readonly int maxFruitIcons = 7;

    private void Awake()
    {
        // When the scene starts, it creates all the panels based on maxFruitsIcons and disables it all just to add it on fruitSlots
        for (int i = 0; i < maxFruitIcons; i++)
        {
            GameObject icon = Instantiate(fruitIconPrefab, fruitDisplayPanel);
            var image = icon.GetComponent<Image>();
            image.enabled = false;
            fruitSlots.Add(image);
        }
    }

    public void RefreshFruits(int level)
    {
        List<Sprite> fruits = GetFruitSpritesUpToLevel(level);

        for (int i = 0; i < fruitSlots.Count; i++)
        {
            if (i < fruits.Count)
            {
                fruitSlots[i].sprite = fruits[i];
                fruitSlots[i].enabled = true;
            }
            else
            {
                fruitSlots[i].enabled = false;
            }
        }
    }

    private int GetFruitIndexForLevel(int currentLevel)
    {
        if (currentLevel == 1) return 0;
        if (currentLevel == 2) return 1;

        int fruitIndex = ((currentLevel - 2) / 2) + 1;
        return Mathf.Min(fruitIndex, fruitSprites.Length - 1);
    }

    private List<Sprite> GetFruitSpritesUpToLevel(int level)
    {
        List<Sprite> result = new();

        for (int l = 1; l <= level; l++)
        {
            int index = GetFruitIndexForLevel(l);
            result.Add(fruitSprites[index]);
        }

        if (result.Count > maxFruitIcons)
        {
            int start = result.Count - maxFruitIcons;
            result = result.GetRange(start, maxFruitIcons);
        }

        return result;
    }
}