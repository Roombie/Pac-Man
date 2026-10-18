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
using UnityEngine.Localization.PropertyVariants.TrackedObjects;
using UnityEngine.Localization.PropertyVariants.TrackedProperties;
using UnityEngine.SceneManagement;

/// <summary>
/// Replaces GameObjectLocalizers that only translate a TextMeshPro text with LocalizeStringEvents (same table and key)
/// GameObjectLocalizer (Property Variants) translates once in Start and writes the text field directly, so texts
/// that are hidden and shown again (animations) can miss the translation. LocalizeStringEvent refreshes on every
/// enable and language change and goes through TMP's text property
/// Localizers that also change layout per language are split: the text moves to a LocalizeStringEvent and the
/// localizer keeps the rest. Texts with other tracked text properties or smart-string variables are left alone and listed
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
                "Moves every localized TextMeshPro text from its GameObjectLocalizer to a LocalizeStringEvent " +
                "(localizers that also change layout keep the layout), in every scene in Build Settings, " +
                "and saves those scenes.\n\nCommit or back up first.",
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

                    if (!TryGetText(localizer, out var tracked, out var text, out var source, out var reason))
                    {
                        skipped++;
                        log.AppendLine($"  SKIP {path}: {reason}");
                        continue;
                    }

                    converted++;
                    bool split = localizer.TrackedObjects.Count > 1;
                    log.AppendLine($"  {(convert ? "OK  " : "WILL")} {path}{(split ? " (text only, localizer keeps the layout)" : "")}");
                    if (convert) ConvertOne(localizer, tracked, text, source);
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

    /// <summary>
    /// The localizer's TMP text, when that text only tracks m_text without smart-string variables
    /// Other tracked objects (e.g. the RectTransform for per-language layout) don't matter, they stay on the localizer
    /// </summary>
    private static bool TryGetText(GameObjectLocalizer localizer, out TrackedObject tracked, out TMP_Text text,
                                   out LocalizedString source, out string reason)
    {
        tracked = null;
        text = null;
        source = null;

        if (localizer.GetComponent<LocalizeStringEvent>()) { reason = "already has a LocalizeStringEvent"; return false; }

        foreach (var candidate in localizer.TrackedObjects)
        {
            if (candidate?.Target is not TMP_Text candidateText) continue;
            if (tracked != null) { reason = "tracks several texts"; return false; }
            tracked = candidate;
            text = candidateText;
        }
        if (tracked == null) { reason = "doesn't target a TextMeshPro text"; return false; }

        var properties = tracked.TrackedProperties;
        if (properties.Count != 1) { reason = $"text tracks {properties.Count} properties"; return false; }

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

    private static void ConvertOne(GameObjectLocalizer localizer, TrackedObject tracked, TMP_Text text, LocalizedString source)
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

        if (localizer.TrackedObjects.Count == 1)
        {
            Undo.DestroyObjectImmediate(localizer);
        }
        else
        {
            // Per-language layout stays on the localizer; only the text moves
            Undo.RecordObject(localizer, "Move localized text to LocalizeStringEvent");
            localizer.TrackedObjects.Remove(tracked);
            EditorUtility.SetDirty(localizer);
        }

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
