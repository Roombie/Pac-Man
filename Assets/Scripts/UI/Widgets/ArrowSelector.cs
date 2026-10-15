// ArrowSelector.cs (updated)
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
    private Coroutine pendingMove; // only the latest reposition runs (each one forces a canvas/layout rebuild)

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDisable()
    {
        pendingMove = null; // Unity stops coroutines on disable
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

                    if (UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject == buttons[i].button.gameObject)
                    {
                        suppressSoundOnFirstAutoSelection = false;
                    }
                    yield break;
                }
            }
        }

        suppressSoundOnFirstAutoSelection = false;
    }

    // Runs after every Update (selection changes included), so no per-frame coroutine is needed
    void LateUpdate()
    {
        if (firstFrame)
            firstFrame = false;

        VerifyArrowVisibility();
    }

    void VerifyArrowVisibility()
    {
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;

        if (!IsGameObjectActuallySelectable(selected) || isSelectingOption)
        {
            if (arrowIndicator.gameObject.activeSelf)
                arrowIndicator.gameObject.SetActive(false);
            return;
        }

        if (lastSelected >= 0 && lastSelected < buttons.Length)
        {
            var button = buttons[lastSelected].button;
            if (button != null && button.gameObject == selected)
            {
                if (!arrowIndicator.gameObject.activeSelf && pendingMove == null)
                    MoveIndicator(lastSelected);
            }
        }
    }

    private bool IsGameObjectActuallySelectable(GameObject go)
    {
        if (go == null || !go.activeInHierarchy)
            return false;

        var selectable = go.GetComponent<Selectable>();
        if (selectable == null || !selectable.IsInteractable())
            return false;

        return true;
    }

    public void PointerEnter(int b) { }
    public void PointerExit(int b) { }

    public void SuppressSoundOnNextSelection()
    {
        suppressSoundOnNextExternalSelection = true;
    }

    public void ButtonSelected(int b)
    {
        lastSelected = b;
        MoveIndicator(b);

        if (suppressSoundOnFirstAutoSelection)
        {
            suppressSoundOnFirstAutoSelection = false;
            return;
        }

        if (suppressSoundOnNextExternalSelection)
        {
            suppressSoundOnNextExternalSelection = false;
            return;
        }

        if (!isChangingPage && navigateSound != null && AudioManager.Instance != null)
            AudioManager.Instance.Play(navigateSound, SoundCategory.SFX);
    }

    public void MoveIndicator(int b)
    {
        if (isSelectingOption || firstFrame)
        {
            RestartPendingMove(MoveIndicatorLaterCoroutine(b));
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

        RestartPendingMove(DelayedUpdatePosition(b));
    }

    private void RestartPendingMove(IEnumerator routine)
    {
        if (pendingMove != null) StopCoroutine(pendingMove);
        pendingMove = StartCoroutine(routine);
    }

    IEnumerator DelayedUpdatePosition(int b)
    {
        yield return null;
        yield return null;
        yield return null;

        if (b < 0 || b >= buttons.Length || buttons[b].button == null)
        {
            arrowIndicator.gameObject.SetActive(false);
            pendingMove = null;
            yield break;
        }

        if (!buttons[b].button.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"[ArrowSelector] Button {b} is no longer active, skipping arrow update.");
            arrowIndicator.gameObject.SetActive(false);
            pendingMove = null;
            yield break;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(buttons[b].button);
        yield return null;

        Vector3 worldPos = buttons[b].button.TransformPoint((Vector3)buttons[b].arrowOffset);
        arrowIndicator.position = worldPos;
        arrowIndicator.gameObject.SetActive(true);
        pendingMove = null;
    }

    IEnumerator MoveIndicatorLaterCoroutine(int b)
    {
        yield return null;
        pendingMove = null;
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