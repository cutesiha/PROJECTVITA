using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ProjectVita.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yarn.Unity;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// 대사 테스트 러너 (문서 4-7): Dialogue 폴더의 노드를 파일별로 보여 주고, 누르면 그 노드부터 바로 플레이한다.
    /// - 플레이 중이 아니면 Story 씬을 열고 플레이를 시작한다. 끝나면 원래 열려 있던 씬으로 돌아온다.
    /// - 플레이 중이면 바로 그 노드로 이동한다.
    /// - 변수는 새 게임처럼 비우고 "시작 변수"를 넣는다 (예: $keycard_seen = true).
    /// - 플레이 중에 .yarn을 저장하면 지금 노드를 처음부터 다시 연다 (YarnProjectAutoRefresh). F5도 같다.
    /// </summary>
    [InitializeOnLoad]
    public sealed class DialogueTestRunnerWindow : EditorWindow
    {
        const string DialogueDir = "Assets/_Project/Dialogue";
        const string YarnProjectPath = DialogueDir + "/Dialogue.yarnproject";
        const string StoryScenePath = "Assets/_Project/Scenes/" + EpisodeFlow.StorySceneName + ".unity";
        const string VarsPrefKey = "ProjectVita.TestRunner.Vars";
        const string ReturnSceneKey = "ProjectVita.TestRunner.ReturnScene";

        static readonly Regex TitleLine = new Regex(@"^\s*title\s*:\s*(\S+)\s*$", RegexOptions.Compiled);

        sealed class FileGroup
        {
            public string path;
            public string label;
            public List<string> nodes = new List<string>();
            public bool open = true;
        }

        readonly List<FileGroup> groups = new List<FileGroup>();
        HashSet<string> compiledNodes;
        string compileError;
        string search = "";
        string variables = "";
        Vector2 scroll;

        static DialogueTestRunnerWindow()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tools/Project Vita/대사 테스트 러너", priority = 12)]
        public static void ShowWindow()
        {
            var w = GetWindow<DialogueTestRunnerWindow>();
            w.titleContent = new GUIContent("대사 테스트");
            w.minSize = new Vector2(360, 420);
        }

        void OnEnable()
        {
            variables = EditorPrefs.GetString(VarsPrefKey, "");
            Rescan();
        }

        void OnFocus() => Rescan();

        void OnProjectChange() => Rescan();

        // ------------------------------------------------------------ 노드 목록

        void Rescan()
        {
            var openState = groups.ToDictionary(g => g.path, g => g.open);
            groups.Clear();
            if (Directory.Exists(DialogueDir))
            {
                foreach (var file in Directory.GetFiles(DialogueDir, "*.yarn", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    string path = file.Replace('\\', '/');
                    var group = new FileGroup { path = path, label = path.Substring(DialogueDir.Length + 1) };
                    foreach (var line in File.ReadLines(path))
                    {
                        var m = TitleLine.Match(line);
                        if (m.Success) group.nodes.Add(m.Groups[1].Value);
                    }
                    if (group.nodes.Count == 0) continue;
                    if (openState.TryGetValue(path, out bool open)) group.open = open;
                    groups.Add(group);
                }
            }

            compiledNodes = null;
            compileError = null;
            var project = AssetDatabase.LoadAssetAtPath<YarnProject>(YarnProjectPath);
            if (project == null)
            {
                compileError = "Yarn Project가 없다. 씬 생성 메뉴를 먼저 실행한다.";
                return;
            }
            try
            {
                compiledNodes = new HashSet<string>(project.NodeNames);
                if (compiledNodes.Count == 0) compileError = "Yarn 스크립트에 오류가 있어 컴파일되지 않았다. Console에서 .yarn 오류를 확인한다.";
            }
            catch (Exception)
            {
                compileError = "Yarn 스크립트에 오류가 있어 컴파일되지 않았다. Console에서 .yarn 오류를 확인한다.";
            }
            Repaint();
        }

        // ------------------------------------------------------------ 화면

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "노드를 누르면 그 노드부터 플레이한다. 변수는 새 게임처럼 비우고 아래 '시작 변수'를 넣는다.\n" +
                "플레이 중에 .yarn을 저장하면 지금 노드를 처음부터 다시 연다 (게임 안에서 F5도 같다).",
                MessageType.Info);

            EditorGUILayout.LabelField("시작 변수 (한 줄에 하나, 예: $keycard_seen = true)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            variables = EditorGUILayout.TextArea(variables, GUILayout.MinHeight(48));
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(VarsPrefKey, variables);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (EditorApplication.isPlaying && GUILayout.Button("지금 노드 다시 시작 (F5)")) DialogueTest.ReloadCurrent("테스트 러너");
                if (GUILayout.Button("읽은 대사 기록 지우기", GUILayout.Width(150)))
                {
                    ReadLines.ClearAll();
                    Debug.Log("[테스트] 읽은 대사 기록을 지웠다. 스킵이 모든 대사에서 멈춘다.");
                }
                if (GUILayout.Button("새로 읽기", GUILayout.Width(80))) Rescan();
            }

            if (compileError != null) EditorGUILayout.HelpBox(compileError, MessageType.Error);

            search = EditorGUILayout.TextField("검색", search);
            EditorGUILayout.Space(4);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var g in groups)
            {
                var nodes = string.IsNullOrEmpty(search)
                    ? g.nodes
                    : g.nodes.Where(n => n.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (nodes.Count == 0) continue;

                string header = $"{g.label}  ({nodes.Count})";
                bool searching = !string.IsNullOrEmpty(search);
                if (searching) EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
                else g.open = EditorGUILayout.Foldout(g.open, header, true);
                if (!searching && !g.open) continue;

                EditorGUI.indentLevel++;
                foreach (var node in nodes)
                {
                    bool compiled = compiledNodes != null && compiledNodes.Contains(node);
                    using (new EditorGUI.DisabledScope(!compiled))
                    {
                        var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                        var content = new GUIContent(node, compiled ? "이 노드부터 플레이" : "컴파일된 Yarn에 없다 (저장 전이거나 오류)");
                        if (GUI.Button(rect, content, EditorStyles.miniButtonLeft)) Play(node);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------ 실행

        void Play(string node)
        {
            EditorPrefs.SetString(VarsPrefKey, variables);

            if (EditorApplication.isPlaying)
            {
                if (!DialogueTest.Run(node, variables))
                    Debug.LogWarning("[테스트] 지금은 이동할 수 없다 (씬을 바꾸는 중이거나 에피소드 표를 아직 읽지 않았다).");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(StoryScenePath))
            {
                Debug.LogError("[테스트] Story 씬이 없다. 씬 생성 메뉴를 먼저 실행한다.");
                return;
            }

            // Story 씬의 StoryDirector가 이 값을 보고 노드로 이동한다 (DialogueTest.TryRunEditorRequest).
            SessionState.SetString(DialogueTest.EditorNodeKey, node);
            SessionState.SetString(DialogueTest.EditorVarsKey, variables);
            string current = EditorSceneManager.GetActiveScene().path;
            if (current != StoryScenePath) SessionState.SetString(ReturnSceneKey, current);
            EditorSceneManager.OpenScene(StoryScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            // 플레이가 시작되기 전에 멈췄으면 요청이 남는다. 다음 플레이에 끼어들지 않게 지운다.
            SessionState.EraseString(DialogueTest.EditorNodeKey);
            SessionState.EraseString(DialogueTest.EditorVarsKey);

            string back = SessionState.GetString(ReturnSceneKey, "");
            SessionState.EraseString(ReturnSceneKey);
            if (!string.IsNullOrEmpty(back) && File.Exists(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
        }
    }
}
