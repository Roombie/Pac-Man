using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.PropertyVariants;
using UnityEngine.Localization.PropertyVariants.TrackedProperties;
using UnityEngine.SceneManagement;

/// <summary>
/// Replaces GameObjectLocalizers that only translate a TextMeshPro text with LocalizeStringEvents (same table and key)
/// GameObjectLocalizer (Property Variants) translates once in Start and writes the text field directly, so texts
/// that are hidden and shown again (animations) can miss the translation. LocalizeStringEvent refreshes on every
/// enable and language change and goes through TMP's text property
/// Localizers that do anything else (several properties, fonts, smart-string variables) are left alone and listed
/// </summary>
public static class ConvertTextLocalizers
{
    private const string Menu = "Tools/Localization/";

    [MenuItem(Menu + "Preview Text Localizer Conversion")]
    private static void Preview() => Run(convert: false);

    [MenuItem(Menu + "Convert Text Localizers To Localize String Event")]
    private static void Convert()
    {
        if (!EditorUtility.DisplayDialog("Convert text localizers",
                "Replaces every GameObjectLocalizer that only translates a TextMeshPro text with a LocalizeStringEvent, " +
                "in every scene in Build Settings, and saves those scenes.\n\nCommit or back up first.",
                "Convert", "Cancel"))
            return;

        Run(convert: true);
    }

    private static void Run(bool convert)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var setup = EditorSceneManager.GetSceneManagerSetup();
        var log = new StringBuilder();
        int converted = 0, skipped = 0;

        try
        {
            foreach (var buildScene in EditorBuildSettings.scenes)
            {
                if (!buildScene.enabled) continue;

                var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                log.AppendLine($"\n== {scene.name}");

                foreach (var localizer in FindLocalizers(scene))
                {
                    string path = PathOf(localizer.transform);

                    if (!TryGetText(localizer, out var text, out var source, out var reason))
                    {
                        skipped++;
                        log.AppendLine($"  SKIP {path}: {reason}");
                        continue;
                    }

                    converted++;
                    log.AppendLine($"  {(convert ? "OK  " : "WILL")} {path}");
                    if (convert) ConvertOne(localizer, text, source);
                }

                if (convert) EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        string verb = convert ? "Converted" : "Would convert";
        Debug.Log($"[ConvertTextLocalizers] {verb} {converted}, skipped {skipped}{log}");
    }

    private static IEnumerable<GameObjectLocalizer> FindLocalizers(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var localizer in root.GetComponentsInChildren<GameObjectLocalizer>(true))
                yield return localizer;
    }

    /// <summary>Only localizers whose single job is a TMP text's m_text, without smart-string variables</summary>
    private static bool TryGetText(GameObjectLocalizer localizer, out TMP_Text text, out LocalizedString source, out string reason)
    {
        text = null;
        source = null;

        if (localizer.GetComponent<LocalizeStringEvent>()) { reason = "already has a LocalizeStringEvent"; return false; }
        if (localizer.TrackedObjects.Count != 1) { reason = $"tracks {localizer.TrackedObjects.Count} objects"; return false; }

        var tracked = localizer.TrackedObjects[0];
        text = tracked?.Target as TMP_Text;
        if (text == null) { reason = "doesn't target a TextMeshPro text"; return false; }

        var properties = tracked.TrackedProperties;
        if (properties.Count != 1) { reason = $"tracks {properties.Count} properties"; return false; }

        if (properties[0] is not LocalizedStringProperty { PropertyPath: "m_text" } property)
        {
            reason = $"tracks '{properties[0].PropertyPath}', not the text";
            return false;
        }

        source = property.LocalizedString;
        if (source == null || source.IsEmpty) { reason = "no table entry set"; return false; }
        if (source.Count > 0) { reason = "uses local variables (smart string)"; return false; }

        reason = null;
        return true;
    }

    private static void ConvertOne(GameObjectLocalizer localizer, TMP_Text text, LocalizedString source)
    {
        var go = localizer.gameObject;

        var localizeEvent = Undo.AddComponent<LocalizeStringEvent>(go);
        localizeEvent.StringReference = new LocalizedString(source.TableReference, source.TableEntryReference)
        {
            FallbackState = source.FallbackState,
        };

        // Same wiring Unity's "Localize" context menu creates: OnUpdateString -> TMP_Text.text
        var setter = typeof(TMP_Text).GetProperty(nameof(TMP_Text.text)).GetSetMethod();
        var action = (UnityAction<string>)Delegate.CreateDelegate(typeof(UnityAction<string>), text, setter);
        UnityEventTools.AddPersistentListener(localizeEvent.OnUpdateString, action);

        Undo.DestroyObjectImmediate(localizer);
        EditorUtility.SetDirty(localizeEvent);
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    private static string PathOf(Transform t)
    {
        var path = t.name;
        while ((t = t.parent) != null) path = t.name + "/" + path;
        return path;
    }
}
