using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Mobile-only changes to the menus, applied as each scene loads so the desktop build is untouched:
///   - an on-screen back button on every menu that can be backed out of
///   - prompts that talk about keys/buttons say "tap" instead
///   - desktop-only entries are hidden (navigation is re-linked around them)
/// Objects are found by their name in the scene; a missing one is skipped with a warning
/// </summary>
public static class MobileMenuAdapter
{
    // Text object -> string table entry to show instead
    private static readonly (string objectName, string key)[] TextSwaps =
    {
        ("Push Start Button Text (TMP)", "Mobile_TapToStart"),
    };

    // Entries with no use on a phone (Android apps aren't quit from a menu)
    private static readonly string[] HiddenObjects =
    {
        "Exit Game Button",
    };

    // Scenes that have menus
    private static readonly string[] MenuScenes = { "MainMenu", "Pacman" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookSceneLoads() => SceneManager.sceneLoaded += OnSceneLoaded;

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!TouchInput.IsMobile || System.Array.IndexOf(MenuScenes, scene.name) < 0) return;

        foreach (var (objectName, key) in TextSwaps)
            if (TryFind(scene, objectName, out var go)) LocalizedTextKey.Set(go, key);

        foreach (var objectName in HiddenObjects)
            if (TryFind(scene, objectName, out var go)) SelectableNavigation.HideAndRouteAround(go);

        MobileBackButton.Create();
    }

    private static bool TryFind(Scene scene, string objectName, out GameObject found)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == objectName)
                {
                    found = t.gameObject;
                    return true;
                }

        found = null;
        // Each scene only has some of these, so this is informational
        Debug.Log($"[MobileMenuAdapter] '{objectName}' not in scene '{scene.name}'");
        return false;
    }
}
