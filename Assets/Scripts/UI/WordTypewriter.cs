using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class WordTypewriter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text textUI;
    [SerializeField] private CanvasGroup canvasGroup;  // hides instantly if assigned

    [Header("Typing")]
    [Tooltip("Delay (seconds) between each word reveal.")]
    [Min(0f)] public float perWordDelay = 0.08f;
    [Tooltip("How many words to reveal per step (1 = classic word-by-word).")]
    [Min(1)] public int wordsPerStep = 1;

    [Header("After Typing")]
    [Tooltip("Seconds to wait after all words are visible, before hiding.")]
    [Min(0f)] public float waitAfterTyping = 1.0f;

    [Header("Layout / Overflow")]
    [Tooltip("If true, force Overflow so paging can’t stop the reveal.")]
    public bool forceOverflow = true;
    [Tooltip("If Overflow mode is Page, keep advancing to the last page while typing.")]
    public bool autoAdvancePagesWhenTyping = true;

    [Header("New Input System")]
    [Tooltip("Assign an InputActionReference (e.g., your UI/Submit action). If set, pressing it will skip typing or skip the wait.")]
    public InputActionReference submitAction;

    [Header("Events")]
    public UnityEvent OnRevealFinished; // Fired when full text is shown (before the wait)
    public UnityEvent OnHidden;         // Fired after CanvasGroup alpha is set to 0 (or immediately if no CanvasGroup)

    private Coroutine routine;
    private State state = State.Idle;
    private bool skipTypingRequested;
    private bool skipWaitRequested;

    private enum State { Idle, Typing, Waiting, Done }

    private void Reset()
    {
        if (!textUI) textUI = GetComponent<TMP_Text>();
        if (!canvasGroup) canvasGroup = GetComponentInParent<CanvasGroup>();
    }

    private void OnEnable()
    {
        if (!textUI) textUI = GetComponent<TMP_Text>();

        if (submitAction != null)
        {
            submitAction.action.performed += OnSubmitPerformed;
            submitAction.action.Enable();
        }

        if (textUI && !string.IsNullOrEmpty(textUI.text))
            Play(textUI.text);
    }

    private void OnDisable()
    {
        if (submitAction != null)
        {
            submitAction.action.performed -= OnSubmitPerformed;
            submitAction.action.Disable();
        }
    }

    private void OnSubmitPerformed(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        Submit();
    }

    /// <summary>Call to start. If 'text' is null, uses current TMP text.</summary>
    public void Play(string text = null)
    {
        if (!textUI)
        {
            Debug.LogError("[WordTypewriter] Missing TMP_Text reference.");
            return;
        }

        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Run(text));
    }

    /// <summary>Skip/advance:
    /// - If Typing → show all words immediately.
    /// - If Waiting → hide immediately (skip wait), fire OnHidden.
    /// </summary>
    public void Submit()
    {
        if (state == State.Typing)
        {
            skipTypingRequested = true;
            AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound1);
        }
        else if (state == State.Waiting)
        {
            skipWaitRequested = true;
            AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound1);  
        }
    }

    [ContextMenu("Skip To End (Dev)")]
    public void SkipToEndDev()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(SkipAndHideRoutine());
    }

    private IEnumerator Run(string incomingText)
    {
        skipTypingRequested = false;
        skipWaitRequested = false;

        // Prepare text
        string finalText = incomingText ?? textUI.text ?? string.Empty;
        textUI.richText = true; // keep TMP rich-text tags
        textUI.text = finalText;

        // IMPORTANT: set layout/overflow behavior BEFORE measuring words
        if (forceOverflow)
        {
            textUI.overflowMode = TextOverflowModes.Overflow; // avoid paging
        }
        // Recommended for predictable word boundaries
        textUI.textWrappingMode = TextWrappingModes.NoWrap;

        textUI.maxVisibleWords = 0;
        textUI.pageToDisplay = 1;  // start on first page regardless
        textUI.ForceMeshUpdate(true, true);

        // Ensure visible while typing
        if (canvasGroup)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        // Typing loop
        state = State.Typing;
        int totalWords = textUI.textInfo.wordCount;
        int visible = 0;

        while (visible < totalWords)
        {
            if (skipTypingRequested)
            {
                visible = totalWords;
                textUI.maxVisibleWords = visible;
                // update page in case Page mode is used
                if (!forceOverflow && autoAdvancePagesWhenTyping)
                {
                    textUI.ForceMeshUpdate();
                    textUI.pageToDisplay = textUI.textInfo.pageCount;
                }
                break;
            }

            int step = Mathf.Min(wordsPerStep, totalWords - visible);
            visible += step;
            textUI.maxVisibleWords = visible;

            // If Page overflow is active, auto-advance pages so we never "stall" on page 1
            if (!forceOverflow && autoAdvancePagesWhenTyping)
            {
                textUI.ForceMeshUpdate();
                textUI.pageToDisplay = textUI.textInfo.pageCount;
            }

            if (perWordDelay > 0f) yield return new WaitForSeconds(perWordDelay);
            else yield return null; // at least 1 frame to update UI
        }

        // Fully shown (belt & suspenders: show all words regardless of any layout reflow)
        textUI.maxVisibleWords = int.MaxValue;

        OnRevealFinished?.Invoke();

        // Wait phase (skippable)
        state = State.Waiting;
        float t = 0f;
        while (t < waitAfterTyping)
        {
            if (skipWaitRequested) break;
            t += Time.deltaTime;
            yield return null;
        }

        // Hide instantly + event
        HideInstant();
        OnHidden?.Invoke();

        state = State.Done;
        routine = null;
    }

    private IEnumerator SkipAndHideRoutine()
    {
        textUI.ForceMeshUpdate();
        textUI.maxVisibleWords = int.MaxValue;
        OnRevealFinished?.Invoke();
        HideInstant();
        OnHidden?.Invoke();
        state = State.Done;
        routine = null;
        yield break;
    }

    private void HideInstant()
    {
        if (!canvasGroup) return;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }
}
