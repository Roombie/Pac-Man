using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ComponentMoveContextMenu
{
    private const int MENU_PRIORITY = 2100;

    [MenuItem("CONTEXT/Component/Move To Top", false, MENU_PRIORITY)]
    private static void MoveToTop(MenuCommand command)
    {
        if (command.context is not Component comp || comp is Transform)
            return;

        var go = comp.gameObject;

        Undo.SetCurrentGroupName("Move Component To Top");
        int group = Undo.GetCurrentGroup();

        int guard = go.GetComponents<Component>().Length + 5;

        while (guard-- > 0)
        {
            var list = go.GetComponents<Component>();
            int idx = Array.IndexOf(list, comp);

            // index 0 = Transform, so top usable slot is 1
            if (idx <= 1)
                break;

            Undo.RecordObject(go, "Move Component");
            Undo.RecordObject(comp, "Move Component");

            bool moved = ComponentUtility.MoveComponentUp(comp);
            if (!moved)
                break;
        }

        EditorUtility.SetDirty(go);
        if (!Application.isPlaying && go.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(go.scene);

        Undo.CollapseUndoOperations(group);
        ActiveEditorTracker.sharedTracker.ForceRebuild();
        EditorApplication.RepaintHierarchyWindow();
        SceneView.RepaintAll();
    }

    [MenuItem("CONTEXT/Component/Move To Bottom", false, MENU_PRIORITY + 1)]
    private static void MoveToBottom(MenuCommand command)
    {
        if (command.context is not Component comp || comp is Transform)
            return;

        var go = comp.gameObject;

        Undo.SetCurrentGroupName("Move Component To Bottom");
        int group = Undo.GetCurrentGroup();

        int guard = go.GetComponents<Component>().Length + 5;

        while (guard-- > 0)
        {
            var list = go.GetComponents<Component>();
            int idx = Array.IndexOf(list, comp);

            if (idx < 0 || idx >= list.Length - 1)
                break;

            Undo.RecordObject(go, "Move Component");
            Undo.RecordObject(comp, "Move Component");

            bool moved = ComponentUtility.MoveComponentDown(comp);
            if (!moved)
                break;
        }

        EditorUtility.SetDirty(go);
        if (!Application.isPlaying && go.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(go.scene);

        Undo.CollapseUndoOperations(group);
        ActiveEditorTracker.sharedTracker.ForceRebuild();
        EditorApplication.RepaintHierarchyWindow();
        SceneView.RepaintAll();
    }

    [MenuItem("CONTEXT/Component/Move To Top", true)]
    private static bool ValidateMoveToTop(MenuCommand command)
    {
        if (command.context is not Component comp || comp is Transform)
            return false;

        var comps = comp.gameObject.GetComponents<Component>();
        int index = Array.IndexOf(comps, comp);
        return index > 1;
    }

    [MenuItem("CONTEXT/Component/Move To Bottom", true)]
    private static bool ValidateMoveToBottom(MenuCommand command)
    {
        if (command.context is not Component comp || comp is Transform)
            return false;

        var comps = comp.gameObject.GetComponents<Component>();
        int index = Array.IndexOf(comps, comp);
        return index >= 0 && index < comps.Length - 1;
    }
}