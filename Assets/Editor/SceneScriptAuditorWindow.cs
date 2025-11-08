using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneScriptAuditorWindow : EditorWindow
{
    private class Entry
    {
        public GameObject go;
        public Component component;
        public string scriptName;
        public bool goActive;
        public bool componentEnabled;
    }

    // UI state
    private Vector2 _scroll;
    private string _search = string.Empty;
    private bool _includeInactive = true;
    private bool _includeDisabledComponents = true;
    private bool _showMissingScripts = true;

    // Data
    private readonly List<Entry> _entries = new List<Entry>();
    private readonly Dictionary<string, List<Entry>> _byScript =
        new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
    
    [MenuItem("Tools/Scene Script Auditor")]
    public static void ShowWindow()
    {
        var win = GetWindow<SceneScriptAuditorWindow>("Scene Script Auditor");
        win.minSize = new Vector2(600, 320);
        win.Refresh();
    }

    private void OnEnable()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        Undo.undoRedoPerformed += Refresh;
    }

    private void OnDisable()
    {
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        Undo.undoRedoPerformed -= Refresh;
    }

    private void OnSceneOpened(Scene scene, OpenSceneMode mode) => Refresh();
    private void OnFocus() => Repaint();

    private GUIStyle SmallGrayLabel
    {
        get
        {
            var s = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };
            return s;
        }
    }

    private void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.Space(4);

        if (_entries.Count == 0)
        {
            EditorGUILayout.HelpBox("No data yet. Click Refresh to scan the active scene.", MessageType.Info);
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("GameObject", GUILayout.Width(260));
            GUILayout.Label("Script", GUILayout.Width(240));
            GUILayout.Label("Active", GUILayout.Width(48));
            GUILayout.Label("Enabled", GUILayout.Width(60));
            GUILayout.FlexibleSpace();
            GUILayout.Button("Ping", EditorStyles.toolbarButton, GUILayout.Width(60));
        }

        try
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var e in FilteredEntries())
                DrawRow(e);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"SceneScriptAuditor IMGUI glitch: {ex.Message}");
        }
        finally
        {
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(
            $"Total: {_entries.Count} | Shown: {FilteredEntries().Count()}",
            SmallGrayLabel
        );
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Search", GUILayout.Width(48));
            var newSearch = GUILayout.TextField(_search ?? "", EditorStyles.toolbarTextField, GUILayout.MinWidth(140));
            if (newSearch != _search)
                _search = newSearch;

            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(24)))
                _search = "";

            GUILayout.Space(8);

            _includeInactive = GUILayout.Toggle(_includeInactive, "Include Inactive GOs", EditorStyles.toolbarButton);
            _includeDisabledComponents = GUILayout.Toggle(_includeDisabledComponents, "Include Disabled Components", EditorStyles.toolbarButton);
            _showMissingScripts = GUILayout.Toggle(_showMissingScripts, "Show Missing Scripts", EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
                Refresh();

            if (GUILayout.Button("Export CSV", EditorStyles.toolbarButton, GUILayout.Width(90)))
                ExportCsv();
        }
    }

    private IEnumerable<Entry> FilteredEntries()
    {
        IEnumerable<Entry> q = _entries;

        if (!_includeInactive)
            q = q.Where(e => e.goActive);

        if (!_includeDisabledComponents)
            q = q.Where(e => e.component == null || e.componentEnabled);

        if (!_showMissingScripts)
            q = q.Where(e => e.component != null);

        if (!string.IsNullOrEmpty(_search))
        {
            var s = _search.Trim();
            q = q.Where(e =>
                (e.go && e.go.name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(e.scriptName) && e.scriptName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        return q;
    }

    private void DrawRow(Entry e)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.ObjectField(e.go, typeof(GameObject), true, GUILayout.Width(260));

            var label = e.component == null
                ? $"[Missing] {e.scriptName}"
                : e.scriptName;

            EditorGUILayout.LabelField(label, GUILayout.Width(240));
            GUILayout.Label(e.goActive ? "Yes" : "No", GUILayout.Width(48));
            GUILayout.Label(e.componentEnabled ? "Yes" : "No", GUILayout.Width(60));

            if (GUILayout.Button("Ping", GUILayout.Width(60)))
            {
                Selection.activeObject = e.go;
                EditorGUIUtility.PingObject(e.go);
            }
        }
    }

    private void Refresh()
    {
        _entries.Clear();
        _byScript.Clear();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            foreach (var c in components)
            {
                if (c == null)
                {
                    if (_showMissingScripts)
                    {
                        _entries.Add(new Entry
                        {
                            go = root,
                            component = null,
                            scriptName = "[Missing Script]",
                            goActive = root.activeInHierarchy,
                            componentEnabled = false
                        });
                    }
                    continue;
                }

                if (!(c is MonoBehaviour) && !(c is Behaviour))
                    continue;

                var beh = c as Behaviour;
                _entries.Add(new Entry
                {
                    go = c.gameObject,
                    component = c,
                    scriptName = c.GetType().Name,
                    goActive = c.gameObject.activeInHierarchy,
                    componentEnabled = beh ? beh.enabled : true
                });
            }
        }

        Repaint();
    }

    private void ExportCsv()
    {
        var path = EditorUtility.SaveFilePanel("Export Scene Script List", Application.dataPath, "SceneScriptAudit.csv", "csv");
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            using (var sw = new StreamWriter(path))
            {
                sw.WriteLine("GameObject,Script,Active,Enabled,Path");
                foreach (var e in FilteredEntries())
                {
                    var goPath = GetHierarchyPath(e.go);
                    sw.WriteLine($"{Escape(e.go?.name)},{Escape(e.scriptName)},{(e.goActive ? "true" : "false")},{(e.componentEnabled ? "true" : "false")},{Escape(goPath)}");
                }
            }
            EditorUtility.RevealInFinder(path);
        }
        catch (Exception ex)
        {
            Debug.LogError($"CSV export failed: {ex.Message}");
        }
    }

    private string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(",") || s.Contains("\""))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    private string GetHierarchyPath(GameObject go)
    {
        if (go == null) return "";
        var stack = new Stack<string>();
        var t = go.transform;
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack);
    }
}