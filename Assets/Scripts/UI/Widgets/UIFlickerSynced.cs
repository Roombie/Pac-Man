using UnityEngine;
using UnityEngine.UI;

public class UIFlickerSynced : MonoBehaviour
{
    private Image image;
    private bool currentVisible;

    private void Awake()
    {
        image = GetComponent<Image>();
    }

    private void OnEnable()
    {
        if (FlickerSyncManager.Instance == null)
        {
            Debug.LogWarning("FlickerSyncManager.Instance is not yet initialized.");
            return;
        }

        bool visible = FlickerSyncManager.Instance.GetFlickerState();
        currentVisible = visible;
        SetAlpha(visible ? 1f : 0f);
    }

    private void Update()
    {
        if (FlickerSyncManager.Instance == null) return;

        bool visible = FlickerSyncManager.Instance.GetFlickerState();
        if (visible != currentVisible)
        {
            SetAlpha(visible ? 1f : 0f);
            currentVisible = visible;
        }
    }

    private void SetAlpha(float alpha)
    {
        if (image != null)
        {
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }
    }
}
