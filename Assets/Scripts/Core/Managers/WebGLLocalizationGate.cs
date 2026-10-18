using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

/// <summary>
/// WebGL only: hides a freshly loaded scene's UI until its localized texts have resolved, so they never show
/// for a frame in whatever language is baked into the scene
/// Other platforms don't need it: Localization Settings uses Force Synchronous, so every LocalizeStringEvent
/// has its text before the first frame. WebGL can't load synchronously and ignores that setting
/// </summary>
public class WebGLLocalizationGate : MonoBehaviour
{
    private const int MaxFramesHidden = 60; // Safety net: never leave the UI hidden if something fails to load

    private readonly List<Canvas> hidden = new();
    private readonly List<LocalizeStringEvent> pending = new();
    private int framesHidden;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer) return;

        var go = new GameObject(nameof(WebGLLocalizationGate)) { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        var gate = go.AddComponent<WebGLLocalizationGate>();

        // AfterSceneLoad runs after the first scene's sceneLoaded, so handle that one here
        gate.OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Release(); // A previous scene still waiting: the new one takes over

        foreach (var localizer in FindObjectsByType<LocalizeStringEvent>(FindObjectsSortMode.None))
            if (localizer.gameObject.scene == scene && !IsResolved(localizer))
                pending.Add(localizer);

        if (pending.Count == 0) return;

        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.gameObject.scene != scene || !canvas.isRootCanvas || !canvas.enabled) continue;
            canvas.enabled = false;
            hidden.Add(canvas);
        }
        framesHidden = 0;
    }

    // LateUpdate: Localization finishes its loads during Update, so texts are already set when this runs
    private void LateUpdate()
    {
        if (hidden.Count == 0) return;

        pending.RemoveAll(l => l == null || !l.isActiveAndEnabled || IsResolved(l));
        if (pending.Count == 0 || ++framesHidden >= MaxFramesHidden) Release();
    }

    private void Release()
    {
        foreach (var canvas in hidden)
            if (canvas) canvas.enabled = true;
        hidden.Clear();
        pending.Clear();
    }

    private static bool IsResolved(LocalizeStringEvent localizer)
    {
        var str = localizer.StringReference;
        if (str == null || str.IsEmpty) return true;

        var op = str.CurrentLoadingOperationHandle;
        return !op.IsValid() || op.IsDone;
    }
}
