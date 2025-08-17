using UnityEngine;

public class FlickerSyncManager : MonoBehaviour
{
    public static FlickerSyncManager Instance { get; private set; }

    public float flickerInterval = 0.25f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool GetFlickerState()
    {
        return Mathf.FloorToInt(Time.unscaledTime / flickerInterval) % 2 == 0;
    }
}