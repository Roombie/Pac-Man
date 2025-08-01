using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.EventSystems;

public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance;

    public Animator animator;

    public bool isTransitioning = false;

    // Precomputed animator hashes
    // this is better because Unity internally stores all animation state names, parameters, and tags as integer hashes
    // which saves memory and it's faster
    private static readonly int CloseTrigger = Animator.StringToHash("close");
    private static readonly int OpenTrigger = Animator.StringToHash("open");
    private static readonly int TransitionCloseTag = Animator.StringToHash("TransitionClose");

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

    public void LoadScene(string sceneName)
    {
        if (isTransitioning) return;
        StartCoroutine(PerformSceneTransition(sceneName));
    }

    public void LoadScene(int sceneIndex)
    {
        if (isTransitioning) return;
        StartCoroutine(PerformSceneTransition(sceneIndex));
    }

    private IEnumerator PerformSceneTransition(string sceneName)
    {
        isTransitioning = true;

        // Disable input events during transition
        if (EventSystem.current != null)
            EventSystem.current.sendNavigationEvents = false;

        // Trigger the "close" animation
        animator.SetTrigger(CloseTrigger);

        // Wait until the state with tag "TransitionClose" is active
        yield return null;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        while (state.tagHash != TransitionCloseTag)
        {
            yield return null;
            state = animator.GetCurrentAnimatorStateInfo(0);
        }

        // Wait until the "close" animation finishes
        while (state.tagHash == TransitionCloseTag && state.normalizedTime < 1f)
        {
            yield return null;
            state = animator.GetCurrentAnimatorStateInfo(0);
        }

        // Load the scene asynchronously
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        // Wait until the scene is fully loaded (90%)
        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        // Activate the scene
        asyncLoad.allowSceneActivation = true;

        // Wait a frame to ensure the scene is initialized
        yield return null;

        // Trigger the "open" animation
        animator.SetTrigger(OpenTrigger);

        // Re-enable input
        if (EventSystem.current != null)
            EventSystem.current.sendNavigationEvents = true;

        isTransitioning = false;
    }

    private IEnumerator PerformSceneTransition(int sceneIndex)
    {
        isTransitioning = true;

        if (EventSystem.current != null)
            EventSystem.current.sendNavigationEvents = false;

        animator.SetTrigger(CloseTrigger);

        yield return null;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        while (state.tagHash != TransitionCloseTag)
        {
            yield return null;
            state = animator.GetCurrentAnimatorStateInfo(0);
        }

        while (state.tagHash == TransitionCloseTag && state.normalizedTime < 1f)
        {
            yield return null;
            state = animator.GetCurrentAnimatorStateInfo(0);
        }

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneIndex);
        asyncLoad.allowSceneActivation = false;

        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        asyncLoad.allowSceneActivation = true;

        yield return null;

        animator.SetTrigger(OpenTrigger);

        if (EventSystem.current != null)
            EventSystem.current.sendNavigationEvents = true;

        isTransitioning = false;
    }
}