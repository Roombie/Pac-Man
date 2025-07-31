using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LifeIconsController : MonoBehaviour
{
    [SerializeField] private Transform livesPanel;
    [SerializeField] private GameObject lifeIconPrefab;

    private List<GameObject> lifeIcons = new();
    private Sprite currentLifeSprite;

    public void CreateIcons(Sprite lifeIconSprite)
    {
        currentLifeSprite = lifeIconSprite;
        ClearIcons();
        for (int i = 0; i < GameConstants.MaxLives; i++)
        {
            GameObject icon = Instantiate(lifeIconPrefab, livesPanel);
            Image img = icon.GetComponent<Image>();
            if (img != null && lifeIconSprite != null)
                img.sprite = lifeIconSprite;

            lifeIcons.Add(icon);
        }
    }

    public void UpdateIcons(int livesRemaining, Sprite lifeIconSprite = null)
    {
        if (lifeIconSprite != null && lifeIconSprite != currentLifeSprite)
        {
            currentLifeSprite = lifeIconSprite;
            foreach (var icon in lifeIcons)
            {
                var img = icon.GetComponent<Image>();
                if (img != null)
                    img.sprite = currentLifeSprite;
            }
        }

        for (int i = 0; i < lifeIcons.Count; i++)
        {
            lifeIcons[i].SetActive(i < livesRemaining);
        }
    }

    private void ClearIcons()
    {
        foreach (var icon in lifeIcons)
        {
            Destroy(icon);
        }
        lifeIcons.Clear();
    }
}