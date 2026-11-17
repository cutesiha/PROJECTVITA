using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectVita.Menus;
using ProjectVita.Story;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Yarn.Unity;
using Yarn.Unity.Editor;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// 2단계(문서 4-2) 대사 시스템의 에셋과 씬을 메뉴 하나로 만든다.
    /// - Yarn Project가 없으면 만든다 (Dialogue 폴더 아래 모든 .yarn 포함).
    /// - Title, Story 씬을 처음부터 다시 만든다. 씬을 손으로 고친 내용은 사라진다. 바꿀 것은 이 파일에서 고친다.
    /// - Build Settings에 Title, Story를 맨 앞에 넣는다.
    /// - 끝나면 Episodes.csv를 검사한다.
    /// 여러 번 실행해도 안전하다. 씬 파일은 지우지 않고 내용만 바꾸므로 GUID가 유지된다.
    /// </summary>
    public static class DialogueSetup
    {
        const string MenuRoot = "Tools/Project Vita/";

        const string DialogueDir = "Assets/_Project/Dialogue";
        const string YarnProjectPath = DialogueDir + "/Dialogue.yarnproject";
        const string EpisodesCsvPath = "Assets/_Project/Data/Episodes.csv";
        const string ScenesDir = "Assets/_Project/Scenes";
        const string TitleScenePath = ScenesDir + "/" + EpisodeFlow.TitleSceneName + ".unity";
        const string StoryScenePath = ScenesDir + "/" + EpisodeFlow.StorySceneName + ".unity";

        // 기준 해상도. Player Settings(1단계)와 같다.
        static readonly Vector2 RefResolution = new Vector2(1920, 1080);

        // 임시 색. 디자인은 4-4단계에서 입힌다.
        static readonly Color ColBackground = Hex("#1B1E24");
        static readonly Color ColTitleBackground = Hex("#0E1013");
        static readonly Color ColBox = new Color(0f, 0f, 0f, 0.82f);
        static readonly Color ColAccent = Hex("#B3261E");
        static readonly Color ColButton = Hex("#262A33");
        static readonly Color ColText = Hex("#ECEDEF");
        static readonly Color ColSubText = Hex("#9AA0A6");
        static readonly Color ColDev = new Color(0.63f, 1f, 0.63f, 0.9f);

        [MenuItem(MenuRoot + "2단계 대사 씬 생성", priority = 10)]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var yarnProject = EnsureYarnProject();
            var episodesCsv = AssetDatabase.LoadAssetAtPath<TextAsset>(EpisodesCsvPath);
            if (yarnProject == null || episodesCsv == null)
            {
                Debug.LogError($"[Setup] Yarn Project({YarnProjectPath}) 또는 Episodes.csv({EpisodesCsvPath})가 없어서 씬을 만들 수 없다.");
                return;
            }

            BuildStoryScene(yarnProject, episodesCsv);
            BuildTitleScene(episodesCsv);
            ApplyBuildScenes();

            EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
            Debug.Log("[Setup] 2단계 대사 씬 생성 완료: Title, Story. Title 씬에서 플레이한다.");

            ValidateEpisodes();
        }

        // ================================================================ Yarn Project

        static YarnProject EnsureYarnProject()
        {
            if (!File.Exists(YarnProjectPath))
            {
                // Yarn Spinner의 기본 프로젝트와 같은 설정에 언어만 한국어로 바꾼다.
                // sourceFiles 기본값이 "**/*.yarn"이라 Dialogue 폴더 아래의 .yarn이 전부 자동으로 들어간다.
                var project = new Yarn.Compiler.Project();
                project.BaseLanguage = "ko";
                project.ExcludeFilePatterns = new[] { "**/*~/*", "./Samples/Yarn Spinner*/*" };
                // [YarnCommand]에서 생성되는 명령 정의 파일(.ysls.json)을 참조한다. 명령 인자 검사에 쓰인다.
                project.Definitions ??= new();
                project.Definitions.Add("${workspaceRoot}/ProjectSettings/Packages/dev.yarnspinner/**/*.ysls.json");

                YarnEditorUtility.CreateYarnProject(YarnProjectPath, project);
                // 새 프로젝트는 .yarn 파일과 서로 의존해서 한 번 더 임포트된다. 그 전에 잡은 객체는 무효가 되므로
                // 임포트를 끝까지 마친 뒤에 다시 로드한다.
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[Setup] Yarn Project 생성: " + YarnProjectPath);
            }
            return AssetDatabase.LoadAssetAtPath<YarnProject>(YarnProjectPath);
        }

        // ================================================================ Story 씬

        static void BuildStoryScene(YarnProject yarnProject, TextAsset episodesCsv)
        {
            var scene = OpenClean(StoryScenePath);
            CreateCameraAndLight();
            CreateEventSystem();

            var canvas = CreateCanvas("StoryCanvas", 0);

            // 배경 자리. TODO(4-4): <<bg>>가 이 자리에 그림을 띄운다.
            Stretch(CreateImage("Background", canvas.transform, ColBackground).rectTransform);
            var bgHint = CreateText("BackgroundHint", canvas.transform, "배경 자리 (4-4단계에서 <<bg>>로 바뀐다)", 24, new Color(1, 1, 1, 0.12f), TextAlignmentOptions.Center);
            bgHint.richText = false;
            SetRect(bgHint.rectTransform, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 60));

            // ---- 대사창
            var box = CreateImage("DialogueBox", canvas.transform, ColBox);
            var boxRt = box.rectTransform;
            boxRt.anchorMin = new Vector2(0, 0);
            boxRt.anchorMax = new Vector2(1, 0);
            boxRt.pivot = new Vector2(0.5f, 0);
            boxRt.offsetMin = new Vector2(80, 40);
            boxRt.offsetMax = new Vector2(-80, 40 + 300);

            var namePlate = CreateImage("NamePlate", boxRt, ColAccent);
            SetRect(namePlate.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(340, 64));
            var nameText = CreateText("NameText", namePlate.transform, "이름", 34, ColText, TextAlignmentOptions.Center);
            nameText.fontStyle = FontStyles.Bold;
            Stretch(nameText.rectTransform, 12, 4);

            var bodyText = CreateText("BodyText", boxRt, "", 36, ColText, TextAlignmentOptions.TopLeft);
            bodyText.lineSpacing = 12;
            Stretch(bodyText.rectTransform);
            bodyText.rectTransform.offsetMin = new Vector2(56, 40);
            bodyText.rectTransform.offsetMax = new Vector2(-56, -60);

            var next = CreateImage("NextIndicator", boxRt, ColText);
            SetRect(next.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-44, 40), new Vector2(18, 18));
            next.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);

            // ---- 선택지
            var options = CreateRect("Options", canvas.transform);
            SetRect(options, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 110), new Vector2(960, 0));
            var layout = options.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = options.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var optionTemplate = CreateButton("OptionTemplate", options, "선택지", 32, 84);

            // ---- 개발용 표시
            var devCanvas = CreateCanvas("DevCanvas", 100);
            var devText = CreateText("DevOverlay", devCanvas.transform, "", 20, ColDev, TextAlignmentOptions.TopLeft);
            SetRect(devText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -16), new Vector2(1100, 420));
            var overlay = devText.gameObject.AddComponent<DevOverlay>();
            SetField(overlay, "text", devText);

            // ---- 대화 시스템 (Runner + Presenter + Director)
            var system = new GameObject("DialogueSystem");
            var presenter = system.AddComponent<StoryPresenter>();
            SetField(presenter, "dialogueBox", box.gameObject);
            SetField(presenter, "namePlate", namePlate.gameObject);
            SetField(presenter, "nameText", nameText);
            SetField(presenter, "bodyText", bodyText);
            SetField(presenter, "nextIndicator", next.gameObject);
            SetField(presenter, "optionsRoot", options);
            SetField(presenter, "optionTemplate", optionTemplate);

            var runner = system.AddComponent<DialogueRunner>();
            var so = new SerializedObject(runner);
            // 앞 단계의 임포트로 객체가 바뀌었을 수 있으니 연결 직전에 다시 로드한다.
            yarnProject = AssetDatabase.LoadAssetAtPath<YarnProject>(YarnProjectPath);
            if (yarnProject == null) Debug.LogError("[Setup] Yarn Project를 로드하지 못했다: " + YarnProjectPath);
            so.FindProperty("yarnProject").objectReferenceValue = yarnProject;
            so.FindProperty("autoStart").boolValue = false; // StoryDirector가 Episodes.csv의 start_node로 시작한다.
            var presenters = so.FindProperty("dialoguePresenters");
            presenters.arraySize = 1;
            presenters.GetArrayElementAtIndex(0).objectReferenceValue = presenter;
            so.ApplyModifiedPropertiesWithoutUndo();

            var director = system.AddComponent<StoryDirector>();
            SetField(director, "runner", runner);
            SetField(director, "episodesCsv", episodesCsv);

            EditorSceneManager.SaveScene(scene, StoryScenePath);
            Debug.Log("[Setup] Story 씬 생성: " + StoryScenePath);
        }

        // ================================================================ Title 씬

        static void BuildTitleScene(TextAsset episodesCsv)
        {
            var scene = OpenClean(TitleScenePath);
            CreateCameraAndLight();
            CreateEventSystem();

            var canvas = CreateCanvas("TitleCanvas", 0);
            Stretch(CreateImage("Background", canvas.transform, ColTitleBackground).rectTransform);

            // 가제 미정. 세계관 문서명을 임시로 쓴다.
            var logo = CreateText("Logo", canvas.transform, "PROJECT : VITA", 104, ColText, TextAlignmentOptions.Center);
            logo.fontStyle = FontStyles.Bold;
            SetRect(logo.rectTransform, new Vector2(0.38f, 0.68f), new Vector2(0.38f, 0.68f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100, 140));
            var sub = CreateText("Subtitle", canvas.transform, "가제 · 대사 시스템 테스트 (4-2)", 28, ColSubText, TextAlignmentOptions.Center);
            SetRect(sub.rectTransform, new Vector2(0.38f, 0.68f), new Vector2(0.38f, 0.68f), new Vector2(0.5f, 0.5f), new Vector2(0, -100), new Vector2(1100, 50));

            var menu = CreateRect("Menu", canvas.transform);
            SetRect(menu, new Vector2(0.38f, 0.36f), new Vector2(0.38f, 0.36f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440, 0));
            var menuLayout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            menuLayout.spacing = 18;
            menuLayout.childControlWidth = true;
            menuLayout.childControlHeight = true;
            menuLayout.childForceExpandHeight = false;
            menu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var startButton = CreateButton("StartButton", menu, "처음부터", 36, 88);
            var quitButton = CreateButton("QuitButton", menu, "종료", 36, 88);

            var hint = CreateText("ControlsHint", canvas.transform,
                "클릭 / Space / Enter: 대사 넘기기    숫자 키: 선택지    F1: 개발 정보 숨기기", 22, ColSubText, TextAlignmentOptions.BottomLeft);
            SetRect(hint.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 32), new Vector2(1200, 40));

            // ---- 에피소드 선택 (개발용)
            var panel = CreateImage("EpisodePanel", canvas.transform, new Color(1, 1, 1, 0.04f));
            var panelRt = panel.rectTransform;
            panelRt.anchorMin = new Vector2(1, 0);
            panelRt.anchorMax = new Vector2(1, 1);
            panelRt.pivot = new Vector2(1, 0.5f);
            panelRt.offsetMin = new Vector2(-560, 40);
            panelRt.offsetMax = new Vector2(-40, -40);

            var header = CreateText("Header", panelRt, "에피소드 선택 (개발용)\n<size=70%><color=#9AA0A6>Episodes.csv의 행마다 버튼이 생긴다</color></size>", 30, ColText, TextAlignmentOptions.TopLeft);
            SetRect(header.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(-48, 90));

            var list = CreateRect("List", panelRt);
            list.anchorMin = new Vector2(0, 0);
            list.anchorMax = new Vector2(1, 1);
            list.offsetMin = new Vector2(24, 24);
            list.offsetMax = new Vector2(-24, -130);
            var listLayout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 12;
            listLayout.childAlignment = TextAnchor.UpperCenter;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandHeight = false;
            var episodeTemplate = CreateButton("EpisodeButtonTemplate", list, "에피소드", 30, 96);

            var menuComp = canvas.gameObject.AddComponent<TitleMenu>();
            SetField(menuComp, "episodesCsv", episodesCsv);
            SetField(menuComp, "startButton", startButton);
            SetField(menuComp, "quitButton", quitButton);
            SetField(menuComp, "episodePanel", panel.gameObject);
            SetField(menuComp, "episodeListRoot", list);
            SetField(menuComp, "episodeButtonTemplate", episodeTemplate);

            EditorSceneManager.SaveScene(scene, TitleScenePath);
            Debug.Log("[Setup] Title 씬 생성: " + TitleScenePath);
        }

        // ================================================================ Build Settings

        static void ApplyBuildScenes()
        {
            // Title, Story를 맨 앞에 두고, 그 밖에 이미 등록된 씬(에피소드 전용 씬 등)은 뒤에 유지한다.
            var first = new[] { TitleScenePath, StoryScenePath };
            var scenes = first.Select(p => new EditorBuildSettingsScene(p, true)).ToList();
            scenes.AddRange(EditorBuildSettings.scenes.Where(s => !first.Contains(s.path)));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Setup] 빌드 씬: " + string.Join(", ", scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))));
        }

        // ================================================================ 에피소드 표 검사

        [MenuItem(MenuRoot + "에피소드 표 검사", priority = 11)]
        public static void ValidateEpisodes()
        {
            var csv = AssetDatabase.LoadAssetAtPath<TextAsset>(EpisodesCsvPath);
            var project = AssetDatabase.LoadAssetAtPath<YarnProject>(YarnProjectPath);
            if (csv == null || project == null)
            {
                Debug.LogError($"[검사] {EpisodesCsvPath} 또는 {YarnProjectPath}가 없다.");
                return;
            }

            HashSet<string> nodes;
            try
            {
                nodes = new HashSet<string>(project.NodeNames);
            }
            catch (System.Exception)
            {
                Debug.LogError("[검사] Yarn Project가 컴파일되지 않았다. .yarn 파일의 오류를 먼저 고친다 (Console 또는 Dialogue.yarnproject 인스펙터).");
                return;
            }

            var buildScenes = new HashSet<string>(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)));
            var table = EpisodeTable.Parse(csv);
            int problems = 0;

            foreach (var ep in table.Episodes)
            {
                string where = $"Episodes.csv {ep.LineNumber}행 ({ep.Id})";
                if (!nodes.Contains(ep.StartNode))
                {
                    Debug.LogError($"[검사] {where}: start_node '{ep.StartNode}' 노드가 Yarn에 없다.");
                    problems++;
                }
                if (!string.IsNullOrEmpty(ep.NextEpisode) && table.Find(ep.NextEpisode) == null)
                {
                    Debug.LogError($"[검사] {where}: next_episode '{ep.NextEpisode}'가 표에 없다.");
                    problems++;
                }
                if (!buildScenes.Contains(ep.SceneName))
                {
                    Debug.LogError($"[검사] {where}: 씬 '{ep.SceneName}'이 Build Settings에 없다.");
                    problems++;
                }
            }

            if (problems == 0)
                Debug.Log($"[검사] Episodes.csv 이상 없음. 에피소드 {table.Episodes.Count}개, Yarn 노드 {nodes.Count}개.");
            else
                Debug.LogError($"[검사] Episodes.csv 문제 {problems}개.");
        }

        // ================================================================ 씬 공통

        /// <summary>씬을 열고 루트 오브젝트를 전부 지운다. 없으면 새로 만든다. 파일을 지우지 않으니 GUID가 유지된다.</summary>
        static Scene OpenClean(string path)
        {
            Scene scene;
            if (File.Exists(path))
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects())
                    Object.DestroyImmediate(root);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            return scene;
        }

        static void CreateCameraAndLight()
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0, 0, -10);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.AddComponent<AudioListener>();

            // URP 2D에서 Lit 스프라이트가 검게 나오지 않도록 전역 조명을 둔다 (4-4에서 스프라이트를 쓸 때 대비).
            var lightGo = new GameObject("Global Light 2D");
            lightGo.AddComponent<Light2D>().lightType = Light2D.LightType.Global;
        }

        static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = RefResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>단색 이미지. 클릭을 막지 않도록 raycastTarget은 기본으로 끈다 (대사 넘기기 클릭 판정 때문).</summary>
        static Image CreateImage(string name, Transform parent, Color color, bool raycast = false)
        {
            var img = CreateRect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            var tmp = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        static Button CreateButton(string name, Transform parent, string label, float fontSize, float height)
        {
            var img = CreateImage(name, parent, ColButton, raycast: true);
            var button = img.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            button.colors = colors;
            img.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

            var text = CreateText("Label", img.transform, label, fontSize, ColText, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 24, 6);
            return button;
        }

        static void Stretch(RectTransform rt, float padX = 0, float padY = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }

        static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>private [SerializeField]에 값을 넣는다. 필드 이름이 틀리면 바로 알 수 있게 에러를 낸다.</summary>
        static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[Setup] {target.GetType().Name}에 '{field}' 필드가 없다.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }
}
