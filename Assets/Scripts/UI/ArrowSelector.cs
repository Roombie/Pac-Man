using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ArrowSelector : MonoBehaviour
{
    public static ArrowSelector Instance { get; private set; }
    
    [System.Serializable]
    public struct ButtonData
    {
        public RectTransform button;
        public Vector2 arrowOffset;
    }

    public bool showDebugLinesOnlyOnActiveObjects = true;
    [SerializeField] private AudioClip navigateSound;
    [SerializeField] ButtonData[] buttons;
    [SerializeField] RectTransform arrowIndicator;
    [HideInInspector] public bool isSelectingOption = false;

    [HideInInspector] public int lastSelected = -1;
    private bool firstFrame = true;
    private bool isChangingPage = false;
    private bool suppressSoundOnFirstAutoSelection = true;
    public static bool suppressSoundOnNextExternalSelection = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        StartCoroutine(SuppressFirstAutoSelect());
    }

    IEnumerator SuppressFirstAutoSelect()
    {
        yield return null;

        GameObject selected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
        if (selected != null)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].button != null && buttons[i].button.gameObject == selected)
                {
                    lastSelected = i;
                    suppressSoundOnFirstAutoSelection = true;
                    MoveIndicator(i);
                    yield return null;
                    suppressSoundOnFirstAutoSelection = false;
                    yield break;
                }
            }
        }

        suppressSoundOnFirstAutoSelection = false;
    }

    void LateUpdate()
    {
        if (firstFrame)
            firstFrame = false;

        StartCoroutine(VerifyArrowVisibilityEndOfFrame());
    }

    IEnumerator VerifyArrowVisibilityEndOfFrame()
    {
        yield return new WaitForEndOfFrame();

        var selected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;

        if (!IsGameObjectActuallySelectable(selected) || isSelectingOption)
        {
            arrowIndicator.gameObject.SetActive(false);
            yield break;
        }

        if (lastSelected >= 0 && lastSelected < buttons.Length)
        {
            var button = buttons[lastSelected].button;
            if (button != null && button.gameObject == selected)
            {
                if (!arrowIndicator.gameObject.activeSelf)
                    MoveIndicator(lastSelected);
            }
        }
    }

    private bool IsGameObjectActuallySelectable(GameObject go)
    {
        if (go == null || !go.activeInHierarchy)
            return false;

        // Check if it's in the visible canvas hierarchy
        var selectable = go.GetComponent<Selectable>();
        if (selectable == null || !selectable.IsInteractable())
            return false;

        return true;
    }

    // MOUSE ONLY
    public void PointerEnter(int b)
    {
        // MoveIndicator(b); 
    }
    public void PointerExit(int b)
    {
        // MoveIndicator(b); 
    }

    public void SuppressSoundOnNextSelection()
    {
        suppressSoundOnNextExternalSelection = true;
    }

    public void ButtonSelected(int b)
    {
        lastSelected = b;
        MoveIndicator(b);

        if (suppressSoundOnNextExternalSelection)
        {
            suppressSoundOnNextExternalSelection = false;
            Debug.Log("Selection sound suppressed via static flag");
            return;
        }

        Debug.Log($"suppressFirstSound: {suppressSoundOnFirstAutoSelection}");

        if (suppressSoundOnFirstAutoSelection)
        {
            suppressSoundOnFirstAutoSelection = false;
            Debug.Log("First sound suppressed");
            return;
        }

        if (!isChangingPage && navigateSound != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.Play(navigateSound, SoundCategory.SFX);
            Debug.Log("Audio plays, you're not changing pages or it's not the first time time scene has loaded");
        }
        else
        {
            Debug.LogWarning("Either a navigateSound wasn't referenced or the AudioManager isn't on the scene");
        }
    }

    public void MoveIndicator(int b)
    {
        if (isSelectingOption || firstFrame)
        {
            StartCoroutine(MoveIndicatorLaterCoroutine(b));
            return;
        }

        if (b < 0 || b >= buttons.Length || buttons[b].button == null)
        {
            arrowIndicator.gameObject.SetActive(false);
            return;
        }

        if (!buttons[b].button.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"Button {b} isn't active, arrow cancelled");
            arrowIndicator.gameObject.SetActive(false);
            return;
        }

        StartCoroutine(DelayedUpdatePosition(b));
    }

    IEnumerator DelayedUpdatePosition(int b)
    {
        yield return null;
        yield return null;
        yield return null;

        if (b < 0 || b >= buttons.Length || buttons[b].button == null)
        {
            arrowIndicator.gameObject.SetActive(false);
            yield break;
        }

        if (!buttons[b].button.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"[ArrowSelector] Button {b} is no longer active, skipping arrow update.");
            arrowIndicator.gameObject.SetActive(false);
            yield break;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(buttons[b].button);
        yield return null;

        Vector3 worldPos = buttons[b].button.TransformPoint((Vector3)buttons[b].arrowOffset);
        arrowIndicator.position = worldPos;
        arrowIndicator.gameObject.SetActive(true);
    }

    IEnumerator MoveIndicatorLaterCoroutine(int b)
    {
        yield return null;
        MoveIndicator(b);
    }

    public void SetChangingPage(bool value) => isChangingPage = value;
    public void SetSelecting(bool value) => isSelectingOption = value;

    void OnDrawGizmos()
    {
        if (buttons == null || buttons.Length == 0) return;

        for (int i = 0; i < buttons.Length; i++)
        {
            RectTransform rect = buttons[i].button;
            if (rect == null) continue;
            if (showDebugLinesOnlyOnActiveObjects && !rect.gameObject.activeInHierarchy) continue;

            Vector3 buttonWorldPos = rect.TransformPoint(Vector3.zero);
            Vector3 offsetWorldPos = rect.TransformPoint(buttons[i].arrowOffset);

            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(buttonWorldPos, 1f);

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(offsetWorldPos, 1.25f);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(buttonWorldPos, offsetWorldPos);
        }

        if (lastSelected >= 0 && lastSelected < buttons.Length && buttons[lastSelected].button != null)
        {
            RectTransform selectedRect = buttons[lastSelected].button;
            Vector3 selectedWorldPos = selectedRect.TransformPoint(buttons[lastSelected].arrowOffset);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(selectedWorldPos, 1.5f);
        }
    }
}
