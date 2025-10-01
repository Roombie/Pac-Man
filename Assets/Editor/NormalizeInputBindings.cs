#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public static class NormalizeInputBindings
{
    [MenuItem("Tools/Input System/Normalize Binding Groups (all .inputactions)")]
    public static void NormalizeAll()
    {
        var guids = AssetDatabase.FindAssets("t:InputActionAsset");
        int assetsChanged = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (asset == null) continue;

            if (NormalizeAsset(asset))
            {
                EditorUtility.SetDirty(asset);
                assetsChanged++;
            }
        }

        if (assetsChanged > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[NormalizeInputBindings] Completado. Assets modificados: {assetsChanged}");
    }

    private static bool NormalizeAsset(InputActionAsset asset)
    {
        bool changed = false;
        var so = new SerializedObject(asset);
        var mapsProp = so.FindProperty("m_ActionMaps");
        if (mapsProp == null || !mapsProp.isArray) return false;

        for (int m = 0; m < mapsProp.arraySize; m++)
        {
            var mapProp = mapsProp.GetArrayElementAtIndex(m);
            var bindingsProp = mapProp.FindPropertyRelative("m_Bindings");
            if (bindingsProp == null || !bindingsProp.isArray) continue;

            // 1) Normalizar todos los m_Groups: quitar ; y vacíos
            for (int i = 0; i < bindingsProp.arraySize; i++)
            {
                var b = bindingsProp.GetArrayElementAtIndex(i);
                var groupsProp = b.FindPropertyRelative("m_Groups");
                if (groupsProp == null) continue;

                var normalized = NormalizeGroups(groupsProp.stringValue);
                if (normalized != groupsProp.stringValue)
                {
                    groupsProp.stringValue = normalized;
                    changed = true;
                }
            }

            // 2) Propagar grupo al composite raíz si está vacío y todas sus parts comparten uno
            for (int i = 0; i < bindingsProp.arraySize; i++)
            {
                var root = bindingsProp.GetArrayElementAtIndex(i);
                var pathProp   = root.FindPropertyRelative("m_Path");
                var groupsProp = root.FindPropertyRelative("m_Groups");
                var flagsProp  = root.FindPropertyRelative("m_Flags"); // bitmask interna (composite/part)

                if (pathProp == null || groupsProp == null || flagsProp == null)
                    continue;

                // Heurística: composites típicos de movimiento
                bool looksLikeCompositeRoot =
                    IsCompositeFlag(flagsProp) &&
                    (pathProp.stringValue == "2DVector" || pathProp.stringValue == "Dpad");

                if (!looksLikeCompositeRoot) continue;

                if (!string.IsNullOrEmpty(groupsProp.stringValue)) continue; // ya tiene grupo

                // Recolectar groups de las parts siguientes (hasta que dejen de ser parts)
                var partGroups = new List<string>();
                int j = i + 1;
                while (j < bindingsProp.arraySize)
                {
                    var maybePart = bindingsProp.GetArrayElementAtIndex(j);
                    var partFlags = maybePart.FindPropertyRelative("m_Flags");
                    if (partFlags == null || !IsPartFlag(partFlags))
                        break;

                    var pg = maybePart.FindPropertyRelative("m_Groups")?.stringValue ?? "";
                    pg = NormalizeGroups(pg);
                    if (!string.IsNullOrEmpty(pg))
                        partGroups.Add(pg);
                    j++;
                }

                if (partGroups.Count == 0) continue;

                // Aplanar y deduplicar tokens
                var distinct = partGroups
                    .SelectMany(g => g.Split(';').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)))
                    .Distinct()
                    .ToList();

                if (distinct.Count == 1)
                {
                    groupsProp.stringValue = distinct[0];
                    changed = true;
                }

                // Saltar las parts ya inspeccionadas
                i = j - 1;
            }
        }

        if (changed) so.ApplyModifiedProperties();
        return changed;
    }

    // m_Flags es un bitmask interno; empíricamente:
    // 1 = Composite, 2 = PartOfComposite (pueden cambiar según versión, pero suele funcionar)
    private static bool IsCompositeFlag(SerializedProperty flagsProp)
    {
        try { return (flagsProp.intValue & 1) != 0; } catch { return false; }
    }

    private static bool IsPartFlag(SerializedProperty flagsProp)
    {
        try { return (flagsProp.intValue & 2) != 0; } catch { return false; }
    }

    private static string NormalizeGroups(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var tokens = raw.Split(';')
                        .Select(t => t.Trim())
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Distinct()
                        .ToArray();
        return string.Join(";", tokens);
    }
}
#endif