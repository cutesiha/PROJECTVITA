using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectVita.Battle;
using ProjectVita.Core;
using ProjectVita.Exploration;
using ProjectVita.Menus;
using ProjectVita.Story;
using ProjectVita.UI;
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
using K = ProjectVita.EditorTools.UIKit;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// 씬 4개(Title, Story, Exploration, Battle)를 메뉴 하나로 만든다.
    /// - Yarn Project가 없으면 만든다 (Dialogue 폴더 아래 모든 .yarn 포함).
    /// - 임시 UI 그림과 UI 폰트, 리소스 목록, CSV 데이터를 먼저 맞춘다.
    /// - 씬을 처음부터 다시 만든다. 씬을 손으로 고친 내용은 사라진다. 바꿀 것은 이 파일(배치)과 UIKit(부품), UITheme(색)에서 고친다.
    /// - UI는 임시 UI 시안(dystopia-vn-ui.html)을 옮긴 것이다: 어두운 패널, 깎인 모서리, 페리윙클 테두리, 시안 강조, 모노 라벨, 스캔라인.
    /// - Build Settings에 네 씬을 맨 앞에 넣고, Episodes.csv를 검사한다.
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
        const string ExplorationScenePath = ScenesDir + "/" + EpisodeFlow.ExplorationSceneName + ".unity";
        const string BattleScenePath = ScenesDir + "/" + EpisodeFlow.BattleSceneName + ".unity";

        // 기준 해상도. Player Settings(1단계)와 같다.
        static readonly Vector2 RefResolution = new Vector2(1920, 1080);

        static readonly Color ColDev = new Color(0.63f, 1f, 0.63f, 0.9f);

        [MenuItem(MenuRoot + "씬 생성 (Title, Story, Exploration, Battle)", priority = 10)]
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
            UIArtGenerator.Generate();
            UIArtGenerator.EnsureFonts();
            AssetCatalogBuilder.Rebuild(log: false);
            DataImporter.ImportAll();

            BuildStoryScene(episodesCsv);
            BuildExplorationScene(episodesCsv);
            BuildBattleScene(episodesCsv);
            BuildTitleScene(episodesCsv);
            ApplyBuildScenes();

            EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
            Debug.Log("[Setup] 씬 생성 완료: Title, Story, Exploration, Battle. Title 씬에서 플레이한다.");

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

        static void BuildStoryScene(TextAsset episodesCsv)
        {
            var d = BuildDialogueScene(StoryScenePath);
            var director = d.system.AddComponent<StoryDirector>();
            K.SetField(director, "runner", d.runner);
            K.SetField(director, "stage", d.stage);
            K.SetField(director, "episodesCsv", episodesCsv);

            EditorSceneManager.SaveScene(d.scene, StoryScenePath);
            Debug.Log("[Setup] Story 씬 생성: " + StoryScenePath);
        }

        sealed class DialogueScene
        {
            public Scene scene;
            public Canvas canvas;
            public StoryStage stage;
            public DialogueRunner runner;
            public GameObject system;
            public RectTransform dialogueBox;
        }

        /// <summary>
        /// 대사가 나오는 씬의 공통 부분: 무대, 분위기 효과, 대사창, 선택지, HUD, 오른쪽 위 버튼, 일시정지, 개발용 표시, DialogueRunner.
        /// 그리는 순서(아래부터): 배경 → 육각형/먼지 → 인물 → CG → 비네트/와이프 → 노이즈/스캔라인 → 대사창 → HUD → 플래시/페이드
        /// </summary>
        static DialogueScene BuildDialogueScene(string path)
        {
            var scene = OpenClean(path);
            CreateCameraAndLight();
            CreateEventSystem();

            var canvas = CreateCanvas("StoryCanvas", 0);
            var stage = canvas.gameObject.AddComponent<StoryStage>();

            // ---- 무대
            K.Stretch(K.Img("StageBase", canvas.transform, UITheme.Bg).rectTransform);
            var shakeRoot = K.Rect("ShakeRoot", canvas.transform);
            K.Stretch(shakeRoot);
            var bgBack = K.Img("BackgroundBack", shakeRoot, Color.white);
            K.Stretch(bgBack.rectTransform);
            var bgFront = K.Img("BackgroundFront", shakeRoot, Color.white);
            K.Stretch(bgFront.rectTransform);
            var ambient = K.Rect("Ambient", shakeRoot);
            K.Stretch(ambient);
            var characters = K.Rect("Characters", shakeRoot);
            K.Stretch(characters);
            var cg = K.Img("CG", shakeRoot, Color.white);
            K.Stretch(cg.rectTransform);

            var vignette = K.Img("Vignette", canvas.transform, Color.white);
            K.Stretch(vignette.rectTransform);
            var wipe = K.Img("Wipe", canvas.transform, Color.black);
            K.Stretch(wipe.rectTransform);

            // ---- 분위기 효과 (육각형, 먼지는 무대 안 / 노이즈, 스캔라인, 비네트는 무대 위)
            var fxLayer = K.Rect("ScreenFx", canvas.transform);
            K.Stretch(fxLayer);
            K.CreateScreenFx(ambient, fxLayer);

            // ---- 대사창
            var box = BuildDialogueBox(canvas.transform, out var parts);

            // ---- 선택지
            var options = K.Rect("Options", canvas.transform);
            K.SetRect(options, K.Center, K.Center, K.Center, new Vector2(0, 150), new Vector2(1000, 0));
            var layout = options.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            options.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var optionTemplate = OptionButton(options);

            // ---- HUD (왼쪽 위)와 버튼 (오른쪽 위)
            CreateHud(canvas.transform);
            var quickBar = CreateQuickBar(canvas.transform, box.gameObject, out var menuButton, out var quickMenu);

            // ---- 화면 효과 (대사창 위)
            var flash = K.Img("Flash", canvas.transform, Color.white);
            K.Stretch(flash.rectTransform);
            var fade = K.Img("Fade", canvas.transform, Color.black);
            K.Stretch(fade.rectTransform);

            K.SetField(stage, "shakeRoot", shakeRoot);
            K.SetField(stage, "backgroundBack", bgBack);
            K.SetField(stage, "backgroundFront", bgFront);
            K.SetField(stage, "characterRoot", characters);
            K.SetField(stage, "cgImage", cg);
            K.SetField(stage, "vignette", vignette);
            K.SetField(stage, "wipeOverlay", wipe.rectTransform);
            K.SetField(stage, "flash", flash);
            K.SetField(stage, "fadeOverlay", fade);

            var backlog = CreatePauseCanvas(menuButton, LogKind.Dialogue).backlog;
            K.SetField(quickMenu, "backlog", backlog);
            CreateDevCanvas();

            // ---- 대화 시스템 (Runner + Presenter)
            var system = new GameObject("DialogueSystem");
            var presenter = system.AddComponent<StoryPresenter>();
            K.SetField(presenter, "dialogueBox", box.gameObject);
            K.SetField(presenter, "boxGroup", parts.group);
            K.SetField(presenter, "namePlate", parts.namePlate);
            K.SetField(presenter, "namePlateImage", parts.chip);
            K.SetField(presenter, "nameTagChip", parts.chip.gameObject);
            K.SetField(presenter, "nameTagText", parts.chipText);
            K.SetField(presenter, "nameDot", parts.dot);
            K.SetField(presenter, "nameText", parts.nameText);
            K.SetField(presenter, "accentBar", parts.accent);
            K.SetField(presenter, "metaText", parts.meta);
            K.SetField(presenter, "bodyText", parts.body);
            K.SetField(presenter, "typingCursor", parts.cursor);
            K.SetField(presenter, "nextIndicator", parts.next);
            K.SetField(presenter, "nextArrow", parts.nextArrow);
            K.SetField(presenter, "optionsRoot", options);
            K.SetField(presenter, "optionTemplate", optionTemplate);
            K.SetValue(presenter, "speechColor", p => p.colorValue = UITheme.Text);
            K.SetValue(presenter, "narrationColor", p => p.colorValue = UITheme.Narration);
            K.SetValue(presenter, "defaultNameColor", p => p.colorValue = UITheme.Periwinkle);

            var runner = system.AddComponent<DialogueRunner>();
            var so = new SerializedObject(runner);
            // 앞 단계의 임포트로 객체가 바뀌었을 수 있으니 연결 직전에 다시 로드한다.
            var yarnProject = AssetDatabase.LoadAssetAtPath<YarnProject>(YarnProjectPath);
            if (yarnProject == null) Debug.LogError("[Setup] Yarn Project를 로드하지 못했다: " + YarnProjectPath);
            so.FindProperty("yarnProject").objectReferenceValue = yarnProject;
            so.FindProperty("autoStart").boolValue = false; // StoryDirector나 ExplorationController가 노드를 골라 시작한다.
            var presenters = so.FindProperty("dialoguePresenters");
            presenters.arraySize = 1;
            presenters.GetArrayElementAtIndex(0).objectReferenceValue = presenter;
            so.ApplyModifiedPropertiesWithoutUndo();

            return new DialogueScene { scene = scene, canvas = canvas, stage = stage, runner = runner, system = system, dialogueBox = box };
        }

        sealed class BoxParts
        {
            public CanvasGroup group;
            public GameObject namePlate;
            public Image chip;
            public TMP_Text chipText;
            public Image dot;
            public TMP_Text nameText;
            public Image accent;
            public TMP_Text meta;
            public TMP_Text body;
            public RectTransform cursor;
            public CanvasGroup next;
            public RectTransform nextArrow;
        }

        /// <summary>
        /// 대사창: 깎인 패널 + 테두리, 왼쪽 강조 막대(화자 색), 모서리 꺾쇠, LOG 번호, 이름표([칩] + 육각 점 + 이름), 본문, 타이핑 커서, CLICK ▸.
        /// </summary>
        static RectTransform BuildDialogueBox(Transform canvas, out BoxParts p)
        {
            p = new BoxParts();
            var box = K.CutPanel("DialogueBox", canvas, 16, UITheme.Panel, UITheme.Line);
            var rt = box.rectTransform;
            K.SetRect(rt, K.Bottom, K.Bottom, K.Bottom, new Vector2(0, 44), new Vector2(1560, 226));
            p.group = box.gameObject.AddComponent<CanvasGroup>();
            p.group.blocksRaycasts = false;

            p.accent = K.Sprite("AccentBar", rt, "ui_grad_v", UITheme.Periwinkle);
            p.accent.rectTransform.anchorMin = new Vector2(0, 0);
            p.accent.rectTransform.anchorMax = new Vector2(0, 1);
            p.accent.rectTransform.pivot = new Vector2(0, 1);
            p.accent.rectTransform.anchoredPosition = new Vector2(1, -15);
            p.accent.rectTransform.sizeDelta = new Vector2(4, -16);

            // 모서리 꺾쇠: 깎인 모서리 안쪽에. 오른쪽 아래는 가운데를 축으로 180도 돌린다.
            var tl = K.Sprite("CornerTL", rt, "ui_corner", UITheme.PeriwinkleBright.WithAlpha(0.85f));
            K.Place(tl.rectTransform, K.TopLeft, new Vector2(12, -12), new Vector2(13, 13));
            var br = K.Sprite("CornerBR", rt, "ui_corner", UITheme.PeriwinkleBright.WithAlpha(0.85f));
            K.SetRect(br.rectTransform, K.BottomRight, K.BottomRight, K.Center, new Vector2(-18.5f, 18.5f), new Vector2(13, 13));
            br.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);

            p.meta = K.Mono("Meta", rt, "LOG.001", 13, UITheme.TextDim.WithAlpha(0.6f), TextAlignmentOptions.Right, 24f);
            K.Place(p.meta.rectTransform, K.TopRight, new Vector2(-28, -16), new Vector2(220, 20));

            // 이름표: 위쪽 테두리에 걸쳐 놓인다. [칩 SR-04][◆ 이름]
            var plate = K.Rect("NamePlate", rt);
            plate.anchorMin = plate.anchorMax = new Vector2(0, 1);
            plate.pivot = new Vector2(0, 0.5f);
            plate.anchoredPosition = new Vector2(40, 2);
            var plateLayout = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            plateLayout.childAlignment = TextAnchor.MiddleLeft;
            plateLayout.childControlWidth = plateLayout.childControlHeight = true;
            plateLayout.childForceExpandWidth = plateLayout.childForceExpandHeight = false;
            var fitter = plate.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            p.namePlate = plate.gameObject;

            p.chip = K.Img("Chip", plate, UITheme.Periwinkle);
            p.chip.gameObject.AddComponent<UISkew>().Skew = 13f;
            var chipLayout = p.chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipLayout.padding = new RectOffset(20, 16, 7, 7);
            chipLayout.childAlignment = TextAnchor.MiddleCenter;
            chipLayout.childControlWidth = chipLayout.childControlHeight = true;
            p.chipText = K.Mono("ChipText", p.chip.transform, "ID", 16, UITheme.Hex("#0A0B10"), TextAlignmentOptions.Center, 10f, medium: true);

            var namePanel = K.Img("NamePanel", plate, UITheme.Hex("#14161F"));
            namePanel.gameObject.AddComponent<UISkew>().Skew = 13f;
            var nameOutline = namePanel.gameObject.AddComponent<Outline>();
            nameOutline.effectColor = UITheme.Line;
            nameOutline.effectDistance = new Vector2(1.5f, -1.5f);
            var nameLayout = namePanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            nameLayout.padding = new RectOffset(22, 32, 7, 7);
            nameLayout.spacing = 12;
            nameLayout.childAlignment = TextAnchor.MiddleLeft;
            nameLayout.childControlWidth = nameLayout.childControlHeight = true;
            nameLayout.childForceExpandWidth = nameLayout.childForceExpandHeight = false;
            p.dot = K.Sprite("Dot", namePanel.transform, "ui_hex_fill", UITheme.Periwinkle);
            var dotLe = p.dot.gameObject.AddComponent<LayoutElement>();
            dotLe.preferredWidth = 13;
            dotLe.preferredHeight = 15;
            p.nameText = K.Text("NameText", namePanel.transform, "이름", 26, UITheme.TextBright, TextAlignmentOptions.Left, spacing: 6f, bold: true);
            p.nameText.textWrappingMode = TextWrappingModes.NoWrap;

            // 본문
            p.body = K.Text("BodyText", rt, "", 32, UITheme.Text, TextAlignmentOptions.TopLeft, spacing: 1f);
            p.body.lineSpacing = 22;
            K.Stretch(p.body.rectTransform, 62, 34, 76, 44);
            var cursor = K.Img("TypingCursor", p.body.transform, UITheme.Cyan);
            cursor.rectTransform.anchorMin = cursor.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            cursor.rectTransform.pivot = Vector2.zero;
            cursor.rectTransform.sizeDelta = new Vector2(13, 34);
            p.cursor = cursor.rectTransform;

            // CLICK ▸
            var next = K.Rect("NextIndicator", rt);
            K.Place(next, K.BottomRight, new Vector2(-30, 20), new Vector2(160, 24));
            p.next = next.gameObject.AddComponent<CanvasGroup>();
            var nextLabel = K.Mono("Label", next, "CLICK", 13, UITheme.TextDim, TextAlignmentOptions.Right, 22f);
            K.Place(nextLabel.rectTransform, K.Right, new Vector2(-24, 0), new Vector2(120, 20));
            var arrow = K.Sprite("Arrow", next, "ui_tri", UITheme.Periwinkle);
            K.Place(arrow.rectTransform, K.Right, Vector2.zero, new Vector2(12, 14));
            p.nextArrow = arrow.rectTransform;
            return rt;
        }

        /// <summary>선택지 버튼: 깎인 패널, 왼쪽에 번호. 마우스를 올리면 시안 막대가 나타나고 글자가 밀린다.</summary>
        static Button OptionButton(Transform parent)
        {
            var fill = K.CutPanel("OptionTemplate", parent, 10, Color.white, UITheme.Line, raycast: true);
            fill.gameObject.AddComponent<LayoutElement>().preferredHeight = 78;
            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            K.Colors(button, UITheme.Panel, UITheme.PanelHover, new Color32(28, 34, 48, 250));
            var bar = K.Img("Bar", fill.transform, UITheme.Cyan);
            bar.rectTransform.anchorMin = new Vector2(0, 0);
            bar.rectTransform.anchorMax = new Vector2(0, 1);
            bar.rectTransform.pivot = new Vector2(0, 0.5f);
            bar.rectTransform.anchoredPosition = new Vector2(1, -5);
            bar.rectTransform.sizeDelta = new Vector2(3, -20);
            var label = K.Text("Label", fill.transform, "선택지", 29, UITheme.Text, TextAlignmentOptions.Left, spacing: 4f);
            K.Stretch(label.rectTransform, 34, 4, 30, 4);
            var hover = fill.gameObject.AddComponent<UIHoverFx>();
            K.SetArray(hover, "reveal", new Object[] { bar });
            K.SetArray(hover, "shift", new Object[] { label.rectTransform });
            K.SetValue(hover, "shiftBy", sp => sp.vector2Value = new Vector2(8, 0));
            K.SetArray(hover, "tint", new Object[] { label });
            K.SetValue(hover, "hoverColor", sp => sp.colorValue = UITheme.TextBright);
            K.SetArray(hover, "lineTint", new Object[] { fill.transform.Find("Line").GetComponent<Image>() });
            K.SetValue(hover, "lineHoverColor", sp => sp.colorValue = UITheme.Cyan.WithAlpha(0.45f));
            return button;
        }

        /// <summary>왼쪽 위 HUD: ● REC, 지금 에피소드.</summary>
        static void CreateHud(Transform canvas)
        {
            var hud = K.Rect("Hud", canvas);
            K.Place(hud, K.TopLeft, new Vector2(34, -26), new Vector2(700, 70));
            var dot = K.Sprite("RecDot", hud, "ui_dot", UITheme.Danger);
            K.Place(dot.rectTransform, K.TopLeft, new Vector2(-2, -2), new Vector2(18, 18));
            var rec = K.Mono("Rec", hud, "REC", 14, UITheme.TextDim, TextAlignmentOptions.Left, 26f);
            K.Place(rec.rectTransform, K.TopLeft, new Vector2(24, -1), new Vector2(200, 20));
            var ep = K.Mono("Episode", hud, "", 17, UITheme.PeriwinkleBright, TextAlignmentOptions.Left, 14f);
            K.Place(ep.rectTransform, K.TopLeft, new Vector2(0, -30), new Vector2(700, 26));
            var panel = hud.gameObject.AddComponent<HudPanel>();
            K.SetField(panel, "recDot", dot);
            K.SetField(panel, "episodeText", ep);
        }

        /// <summary>오른쪽 위 버튼: [AUTO][SKIP][LOG] (대사 중에만) [MENU].</summary>
        static RectTransform CreateQuickBar(Transform canvas, GameObject dialogueBox, out Button menuButton, out DialogueQuickMenu quickMenu)
        {
            var bar = K.Rect("QuickBar", canvas);
            K.Place(bar, K.TopRight, new Vector2(-30, -26), new Vector2(320, 58));
            var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var group = K.Rect("DialogueButtons", bar);
            var groupLayout = group.gameObject.AddComponent<HorizontalLayoutGroup>();
            groupLayout.spacing = 10;
            groupLayout.childControlWidth = groupLayout.childControlHeight = true;
            groupLayout.childForceExpandWidth = groupLayout.childForceExpandHeight = false;
            var groupCanvas = group.gameObject.AddComponent<CanvasGroup>();
            var size = new Vector2(62, 58);
            var auto = K.IconButton("AutoButton", group, "icon_auto", "AUTO", size);
            var skip = K.IconButton("SkipButton", group, "icon_skip", "SKIP", size);
            var log = K.IconButton("LogButton", group, "icon_log", "LOG", size);
            menuButton = K.IconButton("MenuButton", bar, "icon_menu", "MENU", new Vector2(68, 58));

            quickMenu = bar.gameObject.AddComponent<DialogueQuickMenu>();
            K.SetField(quickMenu, "logButton", log);
            K.SetField(quickMenu, "autoButton", auto);
            K.SetField(quickMenu, "skipButton", skip);
            K.SetField(quickMenu, "dialogueBox", dialogueBox);
            K.SetField(quickMenu, "dialogueGroup", groupCanvas);
            return bar;
        }

        // ================================================================ 일시정지와 그 위의 창들

        enum LogKind { None, Dialogue, Battle }

        sealed class PauseParts
        {
            public BacklogPanel backlog;
            public BattleStatusPanel status;
        }

        /// <summary>
        /// 일시정지 메뉴와 그 위에 뜨는 창들(기록, 세이브 슬롯, 설정, 확인창, 알림). 전투 씬이면 전투 기록과 능력치 창도.
        /// </summary>
        static PauseParts CreatePauseCanvas(Button menuButton, LogKind log)
        {
            var canvas = CreateCanvas("PauseCanvas", 50);
            bool withBacklog = log != LogKind.None;
            bool battle = log == LogKind.Battle;

            int items = withBacklog ? 7 : 6;
            var win = K.CreateWindow(canvas.transform, "PausePanel", new Vector2(640, 124 + 20 + items * 66 + 26), "SIGNAL INTERRUPTED", "PAUSED", "일시정지", closeButton: false);
            var list = K.Rect("Items", win.body);
            K.Stretch(list, 22, 26, 22, 20);
            var listLayout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.childControlWidth = listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            var resume = PauseItem(list, "계속하기", "RESUME");
            var backlogButton = withBacklog ? PauseItem(list, battle ? "전투 기록" : "대사 기록", battle ? "COMBAT LOG" : "BACKLOG") : null;
            var save = PauseItem(list, "저장", "SAVE");
            var load = PauseItem(list, "불러오기", "LOAD");
            var settingsButton = PauseItem(list, "설정", "SETTINGS");
            var title = PauseItem(list, "타이틀로", "TO TITLE");
            var quit = PauseItem(list, "게임 종료", "QUIT", danger: true);
            K.SetValue(win.anim, "showScale", sp => sp.floatValue = 0.94f);
            K.SetValue(win.anim, "showOffset", sp => sp.vector2Value = new Vector2(0, -26));
            K.SetValue(win.anim, "popEase", sp => sp.boolValue = true);
            K.SetValue(win.anim, "showTime", sp => sp.floatValue = 0.32f);
            K.SetField(win.anim, "staggerRoot", list);

            // 그리는 순서 = 만든 순서. 메뉴 위에 대사 기록, 슬롯, 설정, 확인창, 알림이 뜬다.
            var backlog = withBacklog ? CreateBacklogPanel(canvas, battle) : null;
            var status = battle ? CreateStatusPanel(canvas) : null;
            var confirm = CreateConfirmDialog(canvas);
            var slots = CreateSaveSlotPanel(canvas, confirm);
            var settings = CreateSettingsPanel(canvas);
            CreateToast(canvas);

            var pause = canvas.gameObject.AddComponent<PauseMenu>();
            K.SetField(pause, "panel", win.anim);
            K.SetField(pause, "openButton", menuButton);
            K.SetField(pause, "resumeButton", resume);
            K.SetField(pause, "backlogButton", backlogButton);
            K.SetField(pause, "saveButton", save);
            K.SetField(pause, "loadButton", load);
            K.SetField(pause, "settingsButton", settingsButton);
            K.SetField(pause, "titleButton", title);
            K.SetField(pause, "quitButton", quit);
            K.SetField(pause, "settings", settings);
            K.SetField(pause, "slots", slots);
            K.SetField(pause, "backlog", backlog);
            K.SetField(pause, "confirm", confirm);
            K.SetArray(pause, "extraPanels", status != null ? new Object[] { status } : new Object[0]);
            return new PauseParts { backlog = backlog, status = status };
        }

        /// <summary>
        /// 일시정지 메뉴 항목: 한글 + 오른쪽 영문. 마우스를 올리면 왼쪽 막대, 옅은 강조 띠, ▸가 나타나고 글자가 오른쪽으로 밀린다.
        /// </summary>
        static Button PauseItem(Transform list, string kr, string en, bool danger = false)
        {
            var root = K.Img("Item_" + en.Replace(' ', '_'), list, new Color(0, 0, 0, 0), raycast: true);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = 66;
            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = root;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var accent = danger ? UITheme.Danger : UITheme.Periwinkle;
            var highlight = K.Sprite("Highlight", root.transform, "ui_grad_h", accent.WithAlpha(0.09f));
            K.Stretch(highlight.rectTransform);
            var bar = K.Img("Bar", root.transform, accent);
            bar.rectTransform.anchorMin = new Vector2(0, 0);
            bar.rectTransform.anchorMax = new Vector2(0, 1);
            bar.rectTransform.pivot = new Vector2(0, 0.5f);
            bar.rectTransform.sizeDelta = new Vector2(3, 0);
            var arrow = K.Sprite("Arrow", root.transform, "ui_tri", accent);
            K.Place(arrow.rectTransform, K.Left, new Vector2(16, 0), new Vector2(11, 13));
            var label = K.Text("Label", root.transform, kr, 29, danger ? UITheme.Hex("#D9A5A5") : UITheme.Text, TextAlignmentOptions.Left, spacing: 16f);
            K.Place(label.rectTransform, K.Left, new Vector2(22, 0), new Vector2(380, 56));
            var enText = K.Mono("En", root.transform, en, 13, UITheme.TextDim, TextAlignmentOptions.Right, 24f);
            K.Place(enText.rectTransform, K.Right, new Vector2(-20, 0), new Vector2(220, 24));

            // 마우스를 올리면 ▸가 나타나고 글자는 그만큼 오른쪽으로 비켜 준다.
            var hover = root.gameObject.AddComponent<UIHoverFx>();
            K.SetArray(hover, "reveal", new Object[] { highlight, bar, arrow });
            K.SetArray(hover, "shift", new Object[] { label.rectTransform });
            K.SetValue(hover, "shiftBy", sp => sp.vector2Value = new Vector2(18, 0));
            K.SetArray(hover, "tint", new Object[] { label });
            K.SetValue(hover, "hoverColor", sp => sp.colorValue = danger ? UITheme.Hex("#F2B7B7") : UITheme.TextBright);
            K.SetArray(hover, "lineTint", new Object[] { enText });
            K.SetValue(hover, "lineHoverColor", sp => sp.colorValue = danger ? UITheme.Hex("#F2B7B7") : UITheme.PeriwinkleBright);
            return button;
        }

        // ================================================================ 대사 기록

        /// <summary>대사 기록 창. battle이면 같은 창으로 전투 기록(BattleLog)을 보여 준다.</summary>
        static BacklogPanel CreateBacklogPanel(Canvas canvas, bool battle)
        {
            var win = battle
                ? K.CreateWindow(canvas.transform, "BattleLogPanel", new Vector2(1220, 840), "COMBAT RECORD", "LOG", "전투 기록", closeButton: true)
                : K.CreateWindow(canvas.transform, "BacklogPanel", new Vector2(1220, 840), "RECOVERED TRANSMISSION", "BACKLOG", "기록", closeButton: true);

            var area = K.Rect("Scroll", win.body);
            K.Stretch(area, 40, 22, 30, 14);
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            var viewport = K.Img("Viewport", area, Color.clear, raycast: true); // 휠과 드래그를 받으려면 레이캐스트 대상이 있어야 한다
            K.Stretch(viewport.rectTransform, 0, 0, 22, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = K.Rect("Content", viewport.transform);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 10, 0, 20);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 기록 한 줄: ▸ 이름 // 칩 / 대사 / 아래 구분선
            var entry = K.Rect("EntryTemplate", content);
            entry.gameObject.AddComponent<CanvasGroup>();
            var entryLayout = entry.gameObject.AddComponent<VerticalLayoutGroup>();
            entryLayout.padding = new RectOffset(6, 6, 18, 22);
            entryLayout.spacing = 8;
            entryLayout.childControlWidth = entryLayout.childControlHeight = true;
            entryLayout.childForceExpandWidth = true;
            entryLayout.childForceExpandHeight = false;
            var nameRow = K.Rect("NameRow", entry);
            nameRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;
            var marker = K.Sprite("Marker", nameRow, "ui_tri", UITheme.Periwinkle);
            K.Place(marker.rectTransform, K.Left, new Vector2(0, 0), new Vector2(9, 11));
            var nameText = K.Mono("Name", nameRow, "이름", 16, UITheme.PeriwinkleBright, TextAlignmentOptions.Left, 16f);
            K.SetRect(nameText.rectTransform, K.BottomLeft, K.TopRight, K.Left, new Vector2(20, 0), new Vector2(-20, 0));
            var body = K.Text("Body", entry, "대사", 26, UITheme.Text, TextAlignmentOptions.TopLeft, spacing: 1f);
            body.lineSpacing = 14;
            var sep = K.Img("Separator", entry, UITheme.LineDim);
            sep.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            K.SetRect(sep.rectTransform, K.BottomLeft, K.BottomRight, K.Bottom, Vector2.zero, new Vector2(0, 2));
            var entryView = entry.gameObject.AddComponent<BacklogEntry>();
            K.SetField(entryView, "nameRow", nameRow.gameObject);
            K.SetField(entryView, "marker", marker);
            K.SetField(entryView, "nameText", nameText);
            K.SetField(entryView, "bodyText", body);

            var empty = K.Text("Empty", win.body, battle ? "아직 교전 기록이 없습니다." : "아직 기록된 대사가 없습니다.", 24, UITheme.TextDim, TextAlignmentOptions.Center);
            K.Stretch(empty.rectTransform);

            var barGo = DefaultControls.CreateScrollbar(new DefaultControls.Resources());
            barGo.name = "Scrollbar";
            SetLayerRecursive(barGo, LayerMask.NameToLayer("UI"));
            barGo.transform.SetParent(area, false);
            K.SetRect((RectTransform)barGo.transform, K.BottomRight, K.TopRight, K.Right, Vector2.zero, new Vector2(5, 0));
            var bar = barGo.GetComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            barGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            bar.targetGraphic.color = UITheme.Periwinkle;
            bar.navigation = new Navigation { mode = Navigation.Mode.None };

            scroll.content = content;
            scroll.viewport = viewport.rectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            var panel = canvas.gameObject.AddComponent<BacklogPanel>();
            K.SetField(panel, "panel", win.anim);
            K.SetField(panel, "scroll", scroll);
            K.SetField(panel, "content", content);
            K.SetField(panel, "entryTemplate", entryView);
            K.SetField(panel, "emptyLabel", empty.gameObject);
            K.SetField(panel, "closeButton", win.close);
            K.SetValue(panel, "battleSource", sp => sp.boolValue = battle);
            return panel;
        }

        // ================================================================ 세이브 슬롯 (타이틀과 일시정지 공용)

        static SaveSlotPanel CreateSaveSlotPanel(Canvas canvas, ConfirmDialog confirm)
        {
            var win = K.CreateWindow(canvas.transform, "SaveSlotPanel", new Vector2(1420, 930), "DATA ARCHIVE", "SAVE", "저장", closeButton: true);

            // 2열 x 4행 = 자동 저장 + 슬롯 7개
            var grid = K.Rect("Grid", win.body);
            K.SetRect(grid, K.Top, K.Top, K.Top, new Vector2(0, -26), new Vector2(1324, 734));
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(650, 170);
            gridLayout.spacing = new Vector2(24, 18);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 2;
            K.SetField(win.anim, "staggerRoot", grid);
            K.SetValue(win.anim, "staggerStep", sp => sp.floatValue = 0.04f);

            var fill = K.CutPanel("SlotTemplate", grid, 10, Color.white, UITheme.LineDim, raycast: true);
            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            K.Colors(button, UITheme.SlotFill, UITheme.SlotHover, new Color32(28, 33, 46, 250));
            var line = fill.transform.Find("Line").GetComponent<Image>();
            var content = K.Rect("Content", fill.transform);
            K.Stretch(content);
            // 흐리게 하기는 안쪽 묶음에. 카드 자체의 알파는 창이 열릴 때 차례로 나타나는 애니메이션이 쓴다.
            var group = content.gameObject.AddComponent<CanvasGroup>();

            var thumb = K.Rect("Thumbnail", content).gameObject.AddComponent<RawImage>();
            thumb.raycastTarget = false;
            K.Place(thumb.rectTransform, K.Left, new Vector2(16, 0), new Vector2(240, 135));
            var thumbLine = thumb.gameObject.AddComponent<Outline>();
            thumbLine.effectColor = UITheme.LineDim;
            thumbLine.effectDistance = new Vector2(1, -1);
            var thumbLabel = K.Mono("ThumbnailLabel", thumb.transform, "", 13, UITheme.TextDim, TextAlignmentOptions.Center, 20f);
            K.Stretch(thumbLabel.rectTransform);

            float x = 276;
            var slotText = K.Mono("Slot", content, "SLOT 01", 14, UITheme.Amber, TextAlignmentOptions.Left, 26f);
            K.Place(slotText.rectTransform, K.TopLeft, new Vector2(x, -16), new Vector2(240, 20));
            var status = K.Mono("Status", content, "EMPTY", 14, UITheme.Amber, TextAlignmentOptions.Right, 26f);
            K.Place(status.rectTransform, K.TopRight, new Vector2(-22, -16), new Vector2(140, 20));
            var chapter = K.Text("Chapter", content, "—", 24, UITheme.Text, TextAlignmentOptions.Left, spacing: 5f);
            chapter.textWrappingMode = TextWrappingModes.NoWrap;
            chapter.overflowMode = TextOverflowModes.Ellipsis;
            K.Place(chapter.rectTransform, K.TopLeft, new Vector2(x, -44), new Vector2(352, 34));
            var preview = K.Text("Preview", content, "", 19, UITheme.TextDim, TextAlignmentOptions.TopLeft, spacing: 1f);
            preview.overflowMode = TextOverflowModes.Ellipsis;
            preview.lineSpacing = 8;
            K.Place(preview.rectTransform, K.TopLeft, new Vector2(x, -82), new Vector2(352, 54));
            var time = K.Mono("Time", content, "", 13, UITheme.Periwinkle, TextAlignmentOptions.Left, 12f);
            K.Place(time.rectTransform, K.BottomLeft, new Vector2(x, 14), new Vector2(260, 20));
            var badge = K.Mono("Badge", content, "OVERWRITE", 12, UITheme.Cyan, TextAlignmentOptions.Right, 20f);
            K.Place(badge.rectTransform, K.BottomRight, new Vector2(-22, 14), new Vector2(160, 20));

            var hover = fill.gameObject.AddComponent<UIHoverFx>();
            K.SetArray(hover, "shift", new Object[] { content });
            K.SetValue(hover, "shiftBy", sp => sp.vector2Value = new Vector2(0, 3));
            K.SetArray(hover, "lineTint", new Object[] { line });
            K.SetValue(hover, "lineHoverColor", sp => sp.colorValue = UITheme.Cyan.WithAlpha(0.42f));

            var card = fill.gameObject.AddComponent<SaveSlotCard>();
            K.SetField(card, "button", button);
            K.SetField(card, "group", group);
            K.SetField(card, "thumbnail", thumb);
            K.SetField(card, "thumbnailLabel", thumbLabel);
            K.SetField(card, "slotText", slotText);
            K.SetField(card, "statusText", status);
            K.SetField(card, "chapterText", chapter);
            K.SetField(card, "previewText", preview);
            K.SetField(card, "timeText", time);
            K.SetField(card, "badgeText", badge);

            var panel = canvas.gameObject.AddComponent<SaveSlotPanel>();
            K.SetField(panel, "panel", win.anim);
            K.SetField(panel, "eyebrowText", win.eyebrow);
            K.SetField(panel, "titleText", win.title);
            K.SetField(panel, "grid", grid);
            K.SetField(panel, "cardTemplate", card);
            K.SetField(panel, "closeButton", win.close);
            K.SetField(panel, "confirm", confirm);
            return panel;
        }

        // ================================================================ 설정 (타이틀과 일시정지 공용)

        static SettingsPanel CreateSettingsPanel(Canvas canvas)
        {
            var win = K.CreateWindow(canvas.transform, "SettingsPanel", new Vector2(900, 940), "SYSTEM CONFIG", "SETTINGS", "설정", closeButton: true);
            K.SetValue(win.anim, "showScale", sp => sp.floatValue = 1f);
            K.SetValue(win.anim, "showOffset", sp => sp.vector2Value = new Vector2(0, -12));
            K.SetValue(win.anim, "showTime", sp => sp.floatValue = 0.3f);

            var list = K.Rect("Rows", win.body);
            K.Stretch(list, 52, 20, 52, 8);
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            K.GroupLabel(list, "TEXT / 텍스트");
            var (speed, speedValue) = K.Slider(K.SettingRow(list, "텍스트 속도", "TYPE SPEED"));
            var (auto, autoValue) = K.Slider(K.SettingRow(list, "자동 진행 속도", "AUTO SPEED"));
            var skipRead = K.Toggle(K.SettingRow(list, "읽은 대사만 건너뛰기", "SKIP READ ONLY"));
            K.GroupLabel(list, "DISPLAY / 화면");
            var scan = K.Toggle(K.SettingRow(list, "CRT 스캔라인", "SCANLINES"));
            var grain = K.Toggle(K.SettingRow(list, "필름 노이즈", "FILM GRAIN"));
            var shake = K.Toggle(K.SettingRow(list, "화면 흔들림", "SCREEN SHAKE"));
            var full = K.Toggle(K.SettingRow(list, "전체 화면", "FULLSCREEN"));
            K.GroupLabel(list, "AUDIO / 사운드");
            var (bgm, bgmValue) = K.Slider(K.SettingRow(list, "음량 — 배경 음악", "BGM"));
            var (sfx, sfxValue) = K.Slider(K.SettingRow(list, "음량 — 효과음", "SFX"));
            var noteRow = K.Rect("Note", list);
            noteRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            var note = K.Mono("Text", noteRow, "* 설정은 바로 적용되고 자동으로 저장됩니다.", 13, UITheme.TextFaint, TextAlignmentOptions.BottomLeft, 10f);
            K.Stretch(note.rectTransform);
            K.SetField(win.anim, "staggerRoot", list);
            K.SetValue(win.anim, "staggerStep", sp => sp.floatValue = 0.025f);

            var panel = canvas.gameObject.AddComponent<SettingsPanel>();
            K.SetField(panel, "panel", win.anim);
            K.SetField(panel, "textSpeedSlider", speed);
            K.SetField(panel, "textSpeedValue", speedValue);
            K.SetField(panel, "autoSpeedSlider", auto);
            K.SetField(panel, "autoSpeedValue", autoValue);
            K.SetField(panel, "skipReadOnlyToggle", skipRead);
            K.SetField(panel, "scanlinesToggle", scan);
            K.SetField(panel, "filmGrainToggle", grain);
            K.SetField(panel, "screenShakeToggle", shake);
            K.SetField(panel, "fullscreenToggle", full);
            K.SetField(panel, "bgmSlider", bgm);
            K.SetField(panel, "bgmValue", bgmValue);
            K.SetField(panel, "sfxSlider", sfx);
            K.SetField(panel, "sfxValue", sfxValue);
            K.SetField(panel, "closeButton", win.close);
            return panel;
        }

        // ================================================================ 확인창, 알림

        static ConfirmDialog CreateConfirmDialog(Canvas canvas)
        {
            var win = K.CreateWindow(canvas.transform, "ConfirmPanel", new Vector2(660, 360), "CONFIRM", "CONFIRM", "확인", closeButton: false);
            K.SetValue(win.anim, "showScale", sp => sp.floatValue = 0.95f);
            K.SetValue(win.anim, "popEase", sp => sp.boolValue = true);
            var message = K.Text("Message", win.body, "메시지", 26, UITheme.Text, TextAlignmentOptions.TopLeft, spacing: 4f);
            message.lineSpacing = 18;
            K.Stretch(message.rectTransform, 48, 100, 48, 30);

            var no = K.Button("NoButton", win.body, "아니오", 24, 0, cut: 6);
            K.Place((RectTransform)no.transform, K.BottomRight, new Vector2(-218, 30), new Vector2(160, 56));
            var yesFill = K.Sprite("YesButton", win.body, "ui_cut6_fill", UITheme.Periwinkle, sliced: true, raycast: true);
            K.Place(yesFill.rectTransform, K.BottomRight, new Vector2(-40, 30), new Vector2(160, 56));
            var yes = yesFill.gameObject.AddComponent<Button>();
            yes.targetGraphic = yesFill;
            K.Colors(yes, Color.white, new Color(1.0f, 1.0f, 1.0f, 0.82f), new Color(0.85f, 0.85f, 0.9f, 1f));
            var yesLabel = K.Text("Label", yesFill.transform, "예", 24, UITheme.Hex("#0A0B10"), TextAlignmentOptions.Center, spacing: 10f);
            yesLabel.fontStyle = FontStyles.Bold;
            K.Stretch(yesLabel.rectTransform);

            var dialog = canvas.gameObject.AddComponent<ConfirmDialog>();
            K.SetField(dialog, "panel", win.anim);
            K.SetField(dialog, "eyebrowText", win.eyebrow);
            K.SetField(dialog, "titleText", win.title);
            K.SetField(dialog, "messageText", message);
            K.SetField(dialog, "yesButton", yes);
            K.SetField(dialog, "yesLabel", yesLabel);
            K.SetField(dialog, "yesFill", yesFill);
            K.SetField(dialog, "noButton", no);
            return dialog;
        }

        static void CreateToast(Canvas canvas)
        {
            var root = K.Rect("Toast", canvas.transform);
            K.Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            var box = K.CutPanel("Box", root, 6, UITheme.Panel2, UITheme.Line);
            K.Place(box.rectTransform, K.Bottom, new Vector2(0, 300), new Vector2(560, 62));
            var text = K.Text("Text", box.transform, "", 22, UITheme.PeriwinkleBright, TextAlignmentOptions.Center, spacing: 12f);
            K.Stretch(text.rectTransform, 16, 4, 16, 4);
            var toast = root.gameObject.AddComponent<UIToast>();
            K.SetField(toast, "group", group);
            K.SetField(toast, "box", box.rectTransform);
            K.SetField(toast, "text", text);
        }

        static void CreateDevCanvas()
        {
            var devCanvas = CreateCanvas("DevCanvas", 100);
            var devText = K.Text("DevOverlay", devCanvas.transform, "", 18, ColDev, TextAlignmentOptions.TopLeft);
            K.SetRect(devText.rectTransform, K.TopLeft, K.TopLeft, K.TopLeft, new Vector2(34, -112), new Vector2(1000, 400));
            var overlay = devText.gameObject.AddComponent<DevOverlay>();
            K.SetField(overlay, "text", devText);
        }

        // ================================================================ Exploration 씬

        static void BuildExplorationScene(TextAsset episodesCsv)
        {
            var d = BuildDialogueScene(ExplorationScenePath);

            // 탐색 UI는 무대 위, 대사창 아래에 둔다.
            var layer = K.Rect("ExplorationLayer", d.canvas.transform);
            K.Stretch(layer);
            layer.SetSiblingIndex(d.dialogueBox.GetSiblingIndex());

            var hotspots = K.Rect("Hotspots", layer);
            K.Stretch(hotspots);
            var hotspotTemplate = K.Img("HotspotTemplate", hotspots, Color.clear, raycast: true);
            var hotspotLabel = K.Text("Label", hotspotTemplate.transform, "이름", 26, UITheme.TextBright, TextAlignmentOptions.Center, spacing: 6f);
            hotspotLabel.outlineWidth = 0.2f;
            hotspotLabel.outlineColor = new Color32(0, 0, 0, 255);
            K.SetRect(hotspotLabel.rectTransform, K.TopLeft, K.TopRight, K.Bottom, new Vector2(0, 6), new Vector2(120, 80));
            var hotspot = hotspotTemplate.gameObject.AddComponent<ExplorationHotspot>();
            K.SetField(hotspot, "fill", hotspotTemplate);
            K.SetField(hotspot, "label", hotspotLabel);

            // 장소 이름 (위쪽 가운데)
            var plate = K.CutPanel("LocationPlate", layer, 10, UITheme.Panel, UITheme.Line);
            K.Place(plate.rectTransform, K.Top, new Vector2(0, -26), new Vector2(460, 78));
            var plateEyebrow = K.Mono("Eyebrow", plate.transform, "LOCATION", 12, UITheme.Amber, TextAlignmentOptions.Center, 32f);
            K.Place(plateEyebrow.rectTransform, K.Top, new Vector2(0, -10), new Vector2(400, 18));
            var plateText = K.Text("LocationName", plate.transform, "장소", 28, UITheme.TextBright, TextAlignmentOptions.Center, spacing: 10f, bold: true);
            K.Place(plateText.rectTransform, K.Top, new Vector2(0, -32), new Vector2(420, 38));

            // 지도 버튼 (MENU 아래)
            var mapButton = K.Button("MapButton", layer, "지도  <size=70%><color=#6C7387>MAP</color></size>", 22, 0, cut: 6);
            K.Place((RectTransform)mapButton.transform, K.TopRight, new Vector2(-30, -98), new Vector2(180, 50));

            // 지도 화면
            var map = K.Img("MapView", layer, UITheme.Bg.WithAlpha(0.97f), raycast: true);
            K.Stretch(map.rectTransform);
            // 왼쪽 위는 HUD와 개발용 표시 자리라서 지도 내용은 그 아래부터 둔다.
            var mapBar = K.Sprite("AccentBar", map.transform, "ui_grad_v", UITheme.Periwinkle);
            mapBar.rectTransform.anchorMin = new Vector2(0, 0);
            mapBar.rectTransform.anchorMax = new Vector2(0, 1);
            mapBar.rectTransform.pivot = new Vector2(0, 1);
            mapBar.rectTransform.anchoredPosition = new Vector2(170, -250);
            mapBar.rectTransform.sizeDelta = new Vector2(3, -310);
            var mapEyebrow = K.Mono("Eyebrow", map.transform, "AREA MAP", 15, UITheme.Amber, TextAlignmentOptions.Left, 40f);
            K.Place(mapEyebrow.rectTransform, K.TopLeft, new Vector2(204, -256), new Vector2(600, 24));
            var mapTitle = K.Display("Title", map.transform, ConfirmDialog.WindowTitle("MAP", "지도"), 44, UITheme.Hex("#DFE2EC"), TextAlignmentOptions.Left, 20f);
            K.Place(mapTitle.rectTransform, K.TopLeft, new Vector2(204, -286), new Vector2(900, 64));
            var mapHint = K.Text("Hint", map.transform, "갈 곳을 고른다. 조건을 채우면 새 장소가 나타난다.", 24, UITheme.TextDim, TextAlignmentOptions.Left, spacing: 4f);
            K.Place(mapHint.rectTransform, K.TopLeft, new Vector2(206, -360), new Vector2(1200, 40));
            var mapList = K.Rect("List", map.transform);
            K.SetRect(mapList, K.TopLeft, K.TopLeft, K.TopLeft, new Vector2(204, -430), new Vector2(560, 0));
            var mapLayout = mapList.gameObject.AddComponent<VerticalLayoutGroup>();
            mapLayout.spacing = 14;
            mapLayout.childControlWidth = mapLayout.childControlHeight = true;
            mapLayout.childForceExpandHeight = false;
            mapList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var mapItem = K.Button("MapItemTemplate", mapList, "장소", 30, 84, accent: UITheme.Cyan);

            var controller = d.system.AddComponent<ExplorationController>();
            K.SetField(controller, "runner", d.runner);
            K.SetField(controller, "stage", d.stage);
            K.SetField(controller, "episodesCsv", episodesCsv);
            K.SetField(controller, "hotspotRoot", hotspots);
            K.SetField(controller, "hotspotTemplate", hotspot);
            K.SetField(controller, "locationPlate", plate.gameObject);
            K.SetField(controller, "locationName", plateText);
            K.SetField(controller, "mapButton", mapButton);
            K.SetField(controller, "mapView", map.gameObject);
            K.SetField(controller, "mapListRoot", mapList);
            K.SetField(controller, "mapItemTemplate", mapItem);

            EditorSceneManager.SaveScene(d.scene, ExplorationScenePath);
            Debug.Log("[Setup] Exploration 씬 생성: " + ExplorationScenePath);
        }

        // ================================================================ Battle 씬

        /// <summary>
        /// 전투 화면 (임시 UI 시안 battle.html을 우리 전투 뼈대(3 대 3, 속도순)에 맞게 옮긴 것).
        /// 위: HUD(REC, 에피소드, ROUND 칩, 조우 칩), 행동 순서 줄, LOG/MENU. 가운데: 적 카드 줄, 아군 카드 줄.
        /// 아래: 전투 기록 한 줄 + 명령 창(스킬 4칸, 능력치/기록/뒤로/자동 버튼, 잠금 덮개). 그 위에 배너, 결과 창.
        /// </summary>
        static void BuildBattleScene(TextAsset episodesCsv)
        {
            var scene = OpenClean(BattleScenePath);
            CreateCameraAndLight();
            CreateEventSystem();

            var canvas = CreateCanvas("BattleCanvas", 0);
            var bg = K.Img("Background", canvas.transform, UITheme.Hex("#0E1015"));
            K.Stretch(bg.rectTransform);
            var ambient = K.Rect("Ambient", canvas.transform);
            K.Stretch(ambient);
            var shake = K.Rect("ShakeRoot", canvas.transform);
            K.Stretch(shake);

            // 적 줄 (위), 아군 줄 (아래, 명령 창 위)
            var enemyRow = K.Rect("EnemyRow", shake);
            K.SetRect(enemyRow, K.Top, K.Top, K.Top, new Vector2(0, -112), new Vector2(1600, 420));
            var enemyLayout = enemyRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            enemyLayout.spacing = 44;
            enemyLayout.childAlignment = TextAnchor.UpperCenter;
            enemyLayout.childControlWidth = enemyLayout.childControlHeight = false;
            enemyLayout.childForceExpandWidth = enemyLayout.childForceExpandHeight = false;
            var allyRow = K.Rect("AllyRow", shake);
            K.SetRect(allyRow, K.Bottom, K.Bottom, K.Bottom, new Vector2(0, 360), new Vector2(1640, 172));
            var allyLayout = allyRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            allyLayout.spacing = 22;
            allyLayout.childAlignment = TextAnchor.LowerCenter;
            allyLayout.childControlWidth = allyLayout.childControlHeight = false;
            allyLayout.childForceExpandWidth = allyLayout.childForceExpandHeight = false;

            var enemyTemplate = CreateUnitView("EnemyTemplate", enemyRow, new Vector2(330, 420), enemy: true);
            var allyTemplate = CreateUnitView("AllyTemplate", allyRow, new Vector2(420, 172), enemy: false);

            // 분위기 효과, 아군 피격 시 붉은 가장자리
            var fxLayer = K.Rect("ScreenFx", canvas.transform);
            K.Stretch(fxLayer);
            K.CreateScreenFx(ambient, fxLayer);
            var hitVignette = K.Sprite("HitVignette", canvas.transform, "ui_vignette", UITheme.Danger.WithAlpha(0f));
            K.Stretch(hitVignette.rectTransform);

            // ---- 위쪽: HUD + 칩, 행동 순서, LOG/MENU
            CreateHud(canvas.transform);
            var tags = K.Rect("BattleTags", canvas.transform);
            K.Place(tags, K.TopLeft, new Vector2(34, -96), new Vector2(600, 30));
            var tagsLayout = tags.gameObject.AddComponent<HorizontalLayoutGroup>();
            tagsLayout.spacing = 8;
            tagsLayout.childControlWidth = tagsLayout.childControlHeight = true;
            tagsLayout.childForceExpandWidth = tagsLayout.childForceExpandHeight = false;
            var roundTag = TagChip(tags, "ROUND 01", on: true);
            var encounterTag = TagChip(tags, "ENCOUNTER", on: false);

            var order = K.CutPanel("OrderBar", canvas.transform, 6, UITheme.Panel, UITheme.LineDim);
            K.Place(order.rectTransform, K.Top, new Vector2(0, -26), new Vector2(200, 50));
            var orderLayout = order.gameObject.AddComponent<HorizontalLayoutGroup>();
            orderLayout.padding = new RectOffset(18, 14, 7, 7);
            orderLayout.spacing = 6;
            orderLayout.childAlignment = TextAnchor.MiddleCenter;
            orderLayout.childControlWidth = orderLayout.childControlHeight = true;
            orderLayout.childForceExpandWidth = orderLayout.childForceExpandHeight = false;
            order.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            order.transform.Find("Line").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            K.Mono("Label", order.transform, "ORDER", 12, UITheme.TextDim, TextAlignmentOptions.Center, 26f);
            var chipTemplate = OrderChip(order.transform);

            var bar = K.Rect("QuickBar", canvas.transform);
            K.Place(bar, K.TopRight, new Vector2(-30, -26), new Vector2(150, 58));
            var barLayout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 10;
            barLayout.childAlignment = TextAnchor.UpperRight;
            barLayout.childControlWidth = barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = barLayout.childForceExpandHeight = false;
            var logButton = K.IconButton("LogButton", bar, "icon_log", "LOG", new Vector2(62, 58));
            var menuButton = K.IconButton("MenuButton", bar, "icon_menu", "MENU", new Vector2(68, 58));

            // ---- 아래: 전투 기록 한 줄 + 명령 창
            var zone = K.Rect("CommandZone", canvas.transform);
            K.Place(zone, K.Bottom, new Vector2(0, 28), new Vector2(1640, 318));
            var logBar = K.CutPanel("LogBar", zone, 6, UITheme.Panel, UITheme.LineDim);
            K.Place(logBar.rectTransform, K.Top, Vector2.zero, new Vector2(1640, 52));
            var logTag = K.Mono("Tag", logBar.transform, "ENGAGE", 13, UITheme.Amber, TextAlignmentOptions.Left, 24f);
            K.Place(logTag.rectTransform, K.Left, new Vector2(24, 0), new Vector2(130, 24));
            var logLine = K.Text("Text", logBar.transform, "", 21, UITheme.Text, TextAlignmentOptions.Left, spacing: 2f);
            logLine.textWrappingMode = TextWrappingModes.NoWrap;
            logLine.overflowMode = TextOverflowModes.Ellipsis;
            K.Place(logLine.rectTransform, K.Left, new Vector2(164, 0), new Vector2(1450, 40));

            var cmd = K.CutPanel("CommandPanel", zone, 16, UITheme.Panel, UITheme.Line, raycast: true);
            K.Place(cmd.rectTransform, K.Bottom, Vector2.zero, new Vector2(1640, 256));
            var cmdAccent = K.Sprite("AccentBar", cmd.transform, "ui_grad_v", UITheme.Periwinkle);
            K.SetRect(cmdAccent.rectTransform, K.BottomLeft, K.TopLeft, K.Left, new Vector2(1, -8), new Vector2(3, -16));
            var head = K.Mono("Head", cmd.transform, "COMMAND", 14, UITheme.TextDim, TextAlignmentOptions.Left, 22f);
            K.Place(head.rectTransform, K.TopLeft, new Vector2(28, -16), new Vector2(1560, 22));

            var grid = K.Rect("Skills", cmd.transform);
            K.Place(grid, K.TopLeft, new Vector2(24, -50), new Vector2(1592, 120));
            var skillButtons = new BattleSkillButton[4];
            for (int i = 0; i < 4; i++)
            {
                skillButtons[i] = SkillCard("Skill" + (i + 1), grid, interactive: true);
                K.Place((RectTransform)skillButtons[i].transform, K.TopLeft, new Vector2(i * 400, 0), new Vector2(392, 120));
            }

            var utils = new Button[4];
            string[] utilIcons = { "icon_stats", "icon_log", "icon_back", "icon_auto" };
            string[] utilLabels = { "능력치  <size=70%><color=#6C7387>STATUS · I</color></size>", "전투 기록  <size=70%><color=#6C7387>LOG</color></size>", "뒤로  <size=70%><color=#6C7387>BACK</color></size>", "자동 OFF" };
            for (int i = 0; i < 4; i++)
            {
                utils[i] = UtilButton("Util" + i, cmd.transform, utilIcons[i], utilLabels[i]);
                K.Place((RectTransform)utils[i].transform, K.BottomLeft, new Vector2(24 + i * 400, 18), new Vector2(392, 52));
            }

            // 명령 잠금 덮개
            var lockImg = K.Img("Lock", cmd.transform, new Color32(8, 9, 12, 251), raycast: true); // 선형 색공간이라 거의 불투명해야 아래 글자가 안 비친다
            K.Stretch(lockImg.rectTransform, 2, 2, 2, 2);
            var lockGroup = lockImg.gameObject.AddComponent<CanvasGroup>();
            var lockText = K.Mono("Text", lockImg.transform, "COMMAND LOCK", 16, UITheme.TextDim, TextAlignmentOptions.Center, 24f);
            K.Stretch(lockText.rectTransform);
            lockText.gameObject.AddComponent<UIBlink>();

            // ---- 배너 (ENGAGE, ROUND)
            var bannerRoot = K.Rect("Banner", canvas.transform);
            K.Stretch(bannerRoot);
            var bannerGroup = bannerRoot.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.blocksRaycasts = false;
            var band = K.Img("Band", bannerRoot, new Color32(8, 9, 12, 215));
            K.SetRect(band.rectTransform, K.Left, K.Right, K.Center, new Vector2(0, 60), new Vector2(0, 170));
            var bandTop = K.Sprite("LineTop", band.transform, "ui_grad_hc", UITheme.Amber.WithAlpha(0.8f));
            K.SetRect(bandTop.rectTransform, K.TopLeft, K.TopRight, K.Top, Vector2.zero, new Vector2(0, 2));
            var bandBottom = K.Sprite("LineBottom", band.transform, "ui_grad_hc", UITheme.Periwinkle.WithAlpha(0.8f));
            K.SetRect(bandBottom.rectTransform, K.BottomLeft, K.BottomRight, K.Bottom, Vector2.zero, new Vector2(0, 2));
            var bannerTitle = K.Display("Title", band.transform, "ENGAGE", 62, UITheme.TextBright, TextAlignmentOptions.Center, 30f);
            K.Place(bannerTitle.rectTransform, K.Center, new Vector2(0, 14), new Vector2(1600, 90));
            var bannerSub = K.Mono("Sub", band.transform, "", 15, UITheme.Amber, TextAlignmentOptions.Center, 40f);
            K.Place(bannerSub.rectTransform, K.Center, new Vector2(0, -46), new Vector2(1400, 24));

            // ---- 결과 창
            var result = K.Img("ResultPanel", canvas.transform, UITheme.Overlay, raycast: true);
            K.Stretch(result.rectTransform);
            result.gameObject.AddComponent<CanvasGroup>();
            var resultWin = K.CutPanel("Window", result.transform, 16, UITheme.Panel2, UITheme.Line, raycast: true);
            K.Place(resultWin.rectTransform, K.Center, Vector2.zero, new Vector2(760, 500));
            var resultLine = K.Sprite("TopLine", resultWin.transform, "ui_grad_hc", UITheme.Periwinkle);
            K.SetRect(resultLine.rectTransform, K.TopLeft, K.TopRight, K.Top, Vector2.zero, new Vector2(-32, 3));
            var resultEyebrow = K.Mono("Eyebrow", resultWin.transform, "COMBAT TERMINATED", 14, UITheme.TextDim, TextAlignmentOptions.Center, 50f);
            K.Place(resultEyebrow.rectTransform, K.Top, new Vector2(0, -46), new Vector2(700, 22));
            var resultTitle = K.Display("Title", resultWin.transform, "VICTORY", 58, UITheme.Cyan, TextAlignmentOptions.Center, 30f);
            K.Place(resultTitle.rectTransform, K.Top, new Vector2(0, -80), new Vector2(740, 84));
            var resultSub = K.Text("Sub", resultWin.transform, "", 22, UITheme.TextDim, TextAlignmentOptions.Center, spacing: 12f);
            K.Place(resultSub.rectTransform, K.Top, new Vector2(0, -170), new Vector2(700, 32));
            var stats = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                stats[i] = K.Display("Stat" + i, resultWin.transform, "0", 34, UITheme.Text, TextAlignmentOptions.Center, 6f);
                stats[i].textWrappingMode = TextWrappingModes.Normal;
                K.Place(stats[i].rectTransform, K.Top, new Vector2((i - 1) * 200, -226), new Vector2(190, 90));
            }
            var resultButton = K.Button("ResultButton", resultWin.transform, "계속", 28, 0, cut: 10, accent: UITheme.Cyan);
            K.Place((RectTransform)resultButton.transform, K.Bottom, new Vector2(0, 44), new Vector2(300, 66));
            var resultAnim = result.gameObject.AddComponent<UIPanelAnim>();
            K.SetField(resultAnim, "window", resultWin.rectTransform);
            K.SetValue(resultAnim, "showScale", sp => sp.floatValue = 0.9f);
            K.SetValue(resultAnim, "popEase", sp => sp.boolValue = true);
            K.SetValue(resultAnim, "showTime", sp => sp.floatValue = 0.45f);

            var flash = K.Img("Flash", canvas.transform, Color.white);
            K.Stretch(flash.rectTransform);

            var pause = CreatePauseCanvas(menuButton, LogKind.Battle);

            var controller = canvas.gameObject.AddComponent<BattleController>();
            K.SetField(controller, "episodesCsv", episodesCsv);
            K.SetField(controller, "background", bg);
            K.SetField(controller, "shakeRoot", shake);
            K.SetField(controller, "enemyRow", enemyRow);
            K.SetField(controller, "allyRow", allyRow);
            K.SetField(controller, "enemyTemplate", enemyTemplate);
            K.SetField(controller, "allyTemplate", allyTemplate);
            K.SetField(controller, "flash", flash);
            K.SetField(controller, "hitVignette", hitVignette);
            K.SetField(controller, "roundTag", roundTag);
            K.SetField(controller, "encounterTag", encounterTag);
            K.SetField(controller, "orderRoot", order.rectTransform);
            K.SetField(controller, "orderChipTemplate", chipTemplate);
            K.SetField(controller, "banner", bannerGroup);
            K.SetField(controller, "bannerTitle", bannerTitle);
            K.SetField(controller, "bannerSub", bannerSub);
            K.SetField(controller, "commandZone", zone);
            K.SetField(controller, "commandLock", lockGroup);
            K.SetField(controller, "commandHead", head);
            K.SetArray(controller, "skillButtons", skillButtons);
            K.SetField(controller, "statusButton", utils[0]);
            K.SetField(controller, "logButton", utils[1]);
            K.SetField(controller, "backButton", utils[2]);
            K.SetField(controller, "autoButton", utils[3]);
            K.SetField(controller, "logTag", logTag);
            K.SetField(controller, "logLine", logLine);
            K.SetField(controller, "statusPanel", pause.status);
            K.SetField(controller, "battleLogPanel", pause.backlog);
            K.SetField(controller, "resultPanel", resultAnim);
            K.SetField(controller, "resultEyebrow", resultEyebrow);
            K.SetField(controller, "resultTitle", resultTitle);
            K.SetField(controller, "resultSub", resultSub);
            K.SetArray(controller, "resultStats", stats);
            K.SetField(controller, "resultButton", resultButton);

            // 위쪽 LOG 버튼도 전투 기록을 연다
            UnityEditor.Events.UnityEventTools.AddPersistentListener(logButton.onClick, pause.backlog.Open);

            EditorSceneManager.SaveScene(scene, BattleScenePath);
            Debug.Log("[Setup] Battle 씬 생성: " + BattleScenePath);
        }

        /// <summary>HUD 아래의 작은 칩 ("ROUND 01" 켜짐 = 앰버 바탕, 꺼짐 = 테두리만).</summary>
        static TMP_Text TagChip(Transform parent, string text, bool on)
        {
            var chip = K.Sprite("Tag", parent, "ui_cut6_fill", UITheme.Amber.WithAlpha(on ? 0.14f : 0f), sliced: true);
            var line = K.Sprite("Line", chip.transform, "ui_cut6_line", UITheme.Amber.WithAlpha(0.45f), sliced: true);
            K.Stretch(line.rectTransform);
            line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var layout = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 4, 4);
            layout.childControlWidth = layout.childControlHeight = true;
            chip.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0; // 글자 길이만큼만
            var t = K.Mono("Text", chip.transform, text, 13, UITheme.Amber, TextAlignmentOptions.Center, 20f);
            return t;
        }

        /// <summary>행동 순서 칩: › [육각형] 이름.</summary>
        static BattleOrderChip OrderChip(Transform parent)
        {
            var fill = K.Sprite("ChipTemplate", parent, "ui_cut6_fill", new Color32(20, 23, 32, 200), sliced: true);
            var line = K.Sprite("Line", fill.transform, "ui_cut6_line", new Color(0, 0, 0, 0), sliced: true);
            K.Stretch(line.rectTransform);
            line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var layout = fill.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 14, 5, 5);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var arrow = K.Mono("Arrow", fill.transform, "›", 16, UITheme.TextDim.WithAlpha(0.6f), TextAlignmentOptions.Center, 0f);
            var hex = K.Sprite("Hex", fill.transform, "ui_hex_fill", UITheme.Cyan);
            var hexLe = hex.gameObject.AddComponent<LayoutElement>();
            hexLe.preferredWidth = 15;
            hexLe.preferredHeight = 17;
            var name = K.Text("Name", fill.transform, "이름", 18, UITheme.TextDim, TextAlignmentOptions.Left, spacing: 4f);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            var chip = fill.gameObject.AddComponent<BattleOrderChip>();
            K.SetField(chip, "fill", fill);
            K.SetField(chip, "line", line);
            K.SetField(chip, "hex", hex);
            K.SetField(chip, "nameText", name);
            K.SetField(chip, "arrow", arrow.gameObject);
            return chip;
        }

        /// <summary>
        /// 스킬 카드 (battle.html의 .skill): [타입 칩] 이름 / 비용, 설명, 숫자 키. 마우스를 올리면 살짝 떠오르고 테두리가 밝아진다.
        /// interactive가 아니면 능력치 창의 스킬 목록용 (누를 수 없고 숫자 키 없음).
        /// </summary>
        static BattleSkillButton SkillCard(string name, Transform parent, bool interactive)
        {
            var fill = K.CutPanel(name, parent, 10, interactive ? Color.white : UITheme.SlotFill, UITheme.LineDim, raycast: interactive);
            var line = fill.transform.Find("Line").GetComponent<Image>();
            Button button = null;
            if (interactive)
            {
                button = fill.gameObject.AddComponent<Button>();
                button.targetGraphic = fill;
                K.Colors(button, UITheme.SlotFill, UITheme.SlotHover, new Color32(30, 36, 50, 250));
                fill.gameObject.AddComponent<CanvasGroup>(); // 차례로 나타나기용
            }
            var content = K.Rect("Content", fill.transform);
            K.Stretch(content);
            var contentGroup = content.gameObject.AddComponent<CanvasGroup>();
            contentGroup.blocksRaycasts = false;

            var chip = K.Sprite("TypeChip", content, "ui_cut6_fill", UITheme.Cyan.WithAlpha(0.14f), sliced: true);
            K.Place(chip.rectTransform, K.TopLeft, new Vector2(14, -14), new Vector2(104, 26));
            var typeText = K.Mono("Type", chip.transform, "NORMAL", 12, UITheme.Cyan, TextAlignmentOptions.Center, 16f);
            K.Stretch(typeText.rectTransform);
            var nameText = K.Text("Name", content, "스킬", 24, UITheme.Hex("#E2E5EE"), TextAlignmentOptions.Left, spacing: 4f, bold: true);
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            // 이름과 설명은 카드 폭에 맞춰 늘어난다 (명령 창 392px, 능력치 창 614px)
            K.SetRect(nameText.rectTransform, K.TopLeft, K.TopRight, K.TopLeft, new Vector2(128, -10), new Vector2(-238, 34));
            var cost = K.Mono("Cost", content, "", 13, UITheme.PeriwinkleBright, TextAlignmentOptions.Right, 8f);
            K.Place(cost.rectTransform, K.TopRight, new Vector2(-30, -18), new Vector2(90, 20));
            var costDot = K.Sprite("CostDot", content, "ui_diamond", UITheme.Periwinkle);
            K.Place(costDot.rectTransform, K.TopRight, new Vector2(-14, -22), new Vector2(10, 11));
            var desc = K.Text("Desc", content, "", 17, UITheme.TextDim, TextAlignmentOptions.TopLeft, spacing: 1f);
            desc.overflowMode = TextOverflowModes.Ellipsis;
            desc.lineSpacing = 6;
            K.Stretch(desc.rectTransform, 16, 10, 40, 48);
            TMP_Text key = null;
            if (interactive)
            {
                key = K.Mono("Key", content, "1", 13, UITheme.TextDim.WithAlpha(0.7f), TextAlignmentOptions.Right, 0f);
                K.Place(key.rectTransform, K.BottomRight, new Vector2(-14, 10), new Vector2(30, 18));
            }

            if (interactive)
            {
                var hover = fill.gameObject.AddComponent<UIHoverFx>();
                K.SetArray(hover, "shift", new Object[] { content });
                K.SetValue(hover, "shiftBy", sp => sp.vector2Value = new Vector2(0, 3));
                K.SetArray(hover, "tint", new Object[] { nameText });
                K.SetValue(hover, "hoverColor", sp => sp.colorValue = Color.white);
                K.SetArray(hover, "lineTint", new Object[] { line });
                K.SetValue(hover, "lineHoverColor", sp => sp.colorValue = UITheme.Periwinkle.WithAlpha(0.5f));
            }

            var view = fill.gameObject.AddComponent<BattleSkillButton>();
            K.SetField(view, "button", button);
            K.SetField(view, "content", contentGroup);
            K.SetField(view, "typeChip", chip);
            K.SetField(view, "typeText", typeText);
            K.SetField(view, "nameText", nameText);
            K.SetField(view, "descText", desc);
            K.SetField(view, "costText", cost);
            K.SetField(view, "keyText", key);
            return view;
        }

        /// <summary>명령 창 아래 줄의 버튼 (battle.html의 .ubtn): 아이콘 + 글자.</summary>
        static Button UtilButton(string name, Transform parent, string icon, string label)
        {
            var fill = K.CutPanel(name, parent, 6, Color.white, UITheme.LineDim, raycast: true);
            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            K.Colors(button, new Color32(16, 19, 27, 200), new Color32(26, 30, 42, 230), new Color32(34, 40, 56, 240));
            var iconImg = K.Sprite("Icon", fill.transform, icon, UITheme.TextDim);
            K.Place(iconImg.rectTransform, K.Center, new Vector2(-96, 0), new Vector2(22, 22));
            var text = K.Text("Label", fill.transform, label, 21, UITheme.TextDim, TextAlignmentOptions.Center, spacing: 8f);
            K.Stretch(text.rectTransform, 40, 2, 10, 2);
            var hover = fill.gameObject.AddComponent<UIHoverFx>();
            K.SetArray(hover, "tint", new Object[] { iconImg, text });
            K.SetValue(hover, "hoverColor", sp => sp.colorValue = UITheme.Text);
            K.SetArray(hover, "lineTint", new Object[] { fill.transform.Find("Line").GetComponent<Image>() });
            K.SetValue(hover, "lineHoverColor", sp => sp.colorValue = UITheme.Periwinkle.WithAlpha(0.45f));
            return button;
        }

        /// <summary>
        /// 전투 유닛 카드 (battle.html의 유닛 정보창). 적은 그림이 크고(위), 아군은 그림이 작다(왼쪽).
        /// 카드 전체가 Body 안에 있어서 피격, 돌진, 쓰러짐 때 통째로 움직인다.
        /// </summary>
        static BattleUnitView CreateUnitView(string name, Transform parent, Vector2 size, bool enemy)
        {
            var root = K.Img(name, parent, new Color(0, 0, 0, 0), raycast: true);
            root.rectTransform.sizeDelta = size;
            var group = root.gameObject.AddComponent<CanvasGroup>();
            var body = K.Rect("Body", root.transform);
            K.Stretch(body);

            var panel = K.CutPanel("Panel", body, 10, UITheme.Panel, UITheme.Line);
            K.Stretch(panel.rectTransform);
            // 선택/행동 테두리: 카드 바깥에 두꺼운 깎인 테두리 그림 (Outline 효과는 반투명 판 뒤로 색이 비쳐서 쓰지 않는다)
            var frame = K.Sprite("Frame", body, "ui_cut10_line", new Color(0, 0, 0, 0), sliced: true);
            frame.pixelsPerUnitMultiplier = 0.55f;
            K.Stretch(frame.rectTransform, -5, -5, -5, -5);
            var accent = K.Sprite("AccentBar", body, "ui_grad_v", enemy ? UITheme.Amber : UITheme.Cyan);
            K.SetRect(accent.rectTransform, K.BottomLeft, K.TopLeft, K.Left, new Vector2(1, -6), new Vector2(3, -12));

            var portrait = K.Img("Portrait", body, Color.white);
            var portraitLabel = K.Mono("PortraitLabel", portrait.transform, "", 15, UITheme.TextDim, TextAlignmentOptions.Center, 8f);
            K.Stretch(portraitLabel.rectTransform, 6, 6, 6, 6);
            var hitFlash = K.Img("HitFlash", portrait.transform, new Color(1, 1, 1, 0));
            K.Stretch(hitFlash.rectTransform);

            var hex = K.Sprite("Hex", body, "ui_hex_fill", enemy ? UITheme.Amber : UITheme.Cyan);
            var nameText = K.Text("Name", body, "이름", enemy ? 26 : 24, UITheme.Hex("#E8EAF0"), TextAlignmentOptions.Left, spacing: 4f, bold: true);
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            var idText = K.Mono("Id", body, "ID", 11, UITheme.TextDim, TextAlignmentOptions.Left, 14f);
            var typeChip = K.Sprite("TypeChip", body, "ui_cut6_line", UITheme.Periwinkle.WithAlpha(0.5f), sliced: true);
            var typeText = K.Mono("Type", typeChip.transform, "TYPE", 11, UITheme.PeriwinkleBright, TextAlignmentOptions.Center, 14f);
            K.Stretch(typeText.rectTransform);

            var hpLabel = K.Mono("HpLabel", body, "HP", 11, UITheme.TextDim, TextAlignmentOptions.Left, 20f);
            var hpBar = K.Sprite("HpBar", body, "ui_cut6_fill", UITheme.Hex("#151823"), sliced: true);
            var hpBarLine = K.Sprite("Line", hpBar.transform, "ui_cut6_line", UITheme.LineDim, sliced: true);
            K.Stretch(hpBarLine.rectTransform);
            var hpGhost = K.Img("Ghost", hpBar.transform, UITheme.Danger.WithAlpha(0.6f));
            var hpFill = K.Img("Fill", hpBar.transform, UITheme.Cyan);
            foreach (var r in new[] { hpGhost.rectTransform, hpFill.rectTransform })
            {
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(2, 2);
                r.offsetMax = new Vector2(-2, -2);
            }
            for (int i = 1; i < 4; i++)
            {
                var tick = K.Img("Tick", hpBar.transform, UITheme.Bg.WithAlpha(0.85f));
                K.SetRect(tick.rectTransform, new Vector2(i * 0.25f, 0), new Vector2(i * 0.25f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 0));
            }
            var hpText = K.Mono("HpText", body, "0 / 0", 15, UITheme.Text, TextAlignmentOptions.Right, 4f);

            var gaugeText = K.Mono("Gauge", body, "SP 0 / 0", 12, UITheme.TextDim, TextAlignmentOptions.Left, 12f);
            var gaugeBar = K.Img("GaugeBar", body, UITheme.Rail);
            var gaugeFill = K.Img("Fill", gaugeBar.transform, UITheme.Periwinkle);
            gaugeFill.rectTransform.anchorMin = Vector2.zero;
            gaugeFill.rectTransform.anchorMax = Vector2.one;
            gaugeFill.rectTransform.offsetMin = gaugeFill.rectTransform.offsetMax = Vector2.zero;

            var statusRoot = K.Rect("Status", body);
            var statusLayout = statusRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            statusLayout.spacing = 6;
            statusLayout.childAlignment = TextAnchor.MiddleLeft;
            statusLayout.childControlWidth = statusLayout.childControlHeight = true;
            statusLayout.childForceExpandWidth = statusLayout.childForceExpandHeight = false;
            var chipText = K.Mono("ChipTemplate", statusRoot, "STATE", 12, UITheme.Amber, TextAlignmentOptions.Center, 12f);
            chipText.margin = new Vector4(9, 3, 9, 3);
            var chipBorder = K.Sprite("Border", chipText.transform, "ui_cut6_line", UITheme.Amber.WithAlpha(0.5f), sliced: true);
            K.Stretch(chipBorder.rectTransform);
            chipBorder.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            chipText.gameObject.AddComponent<UIBlink>();

            var tint = K.Img("DamageTint", body, UITheme.Danger.WithAlpha(0f));
            K.Stretch(tint.rectTransform);
            var popup = K.Display("Popup", body, "0", 58, Color.white, TextAlignmentOptions.Center, 4f);
            popup.outlineWidth = 0.22f;
            popup.outlineColor = new Color32(0, 0, 0, 255);

            if (enemy)
            {
                K.SetRect(portrait.rectTransform, K.TopLeft, K.TopRight, K.Top, new Vector2(0, -12), new Vector2(-24, 236));
                K.Place(hex.rectTransform, K.TopLeft, new Vector2(16, -266), new Vector2(20, 23));
                K.Place(nameText.rectTransform, K.TopLeft, new Vector2(44, -258), new Vector2(170, 34));
                K.Place(idText.rectTransform, K.TopLeft, new Vector2(46, -292), new Vector2(200, 16));
                K.Place(typeChip.rectTransform, K.TopRight, new Vector2(-14, -264), new Vector2(84, 24));
                K.SetRect(hpLabel.rectTransform, K.TopLeft, K.TopLeft, K.TopLeft, new Vector2(16, -320), new Vector2(34, 16));
                K.SetRect(hpBar.rectTransform, K.TopLeft, K.TopRight, K.TopLeft, new Vector2(50, -319), new Vector2(-170, 16));
                K.Place(hpText.rectTransform, K.TopRight, new Vector2(-14, -316), new Vector2(110, 22));
                K.Place(gaugeText.rectTransform, K.TopLeft, new Vector2(16, -348), new Vector2(160, 18));
                K.SetRect(gaugeBar.rectTransform, K.TopLeft, K.TopRight, K.TopLeft, new Vector2(180, -355), new Vector2(-194, 4));
                K.SetRect(statusRoot, K.TopLeft, K.TopRight, K.TopLeft, new Vector2(16, -378), new Vector2(-32, 26));
                K.Place(popup.rectTransform, K.Top, new Vector2(0, -110), new Vector2(320, 100));
            }
            else
            {
                K.Place(portrait.rectTransform, K.Left, new Vector2(12, 0), new Vector2(128, 148));
                K.Place(hex.rectTransform, K.TopLeft, new Vector2(156, -18), new Vector2(18, 21));
                K.Place(nameText.rectTransform, K.TopLeft, new Vector2(182, -10), new Vector2(150, 34));
                K.Place(idText.rectTransform, K.TopLeft, new Vector2(184, -44), new Vector2(160, 16));
                K.Place(typeChip.rectTransform, K.TopRight, new Vector2(-14, -16), new Vector2(76, 24));
                K.SetRect(hpLabel.rectTransform, K.TopLeft, K.TopLeft, K.TopLeft, new Vector2(156, -74), new Vector2(34, 16));
                K.SetRect(hpBar.rectTransform, K.TopLeft, K.TopRight, K.TopLeft, new Vector2(190, -73), new Vector2(-306, 16));
                K.Place(hpText.rectTransform, K.TopRight, new Vector2(-14, -70), new Vector2(104, 22));
                K.Place(gaugeText.rectTransform, K.TopLeft, new Vector2(156, -102), new Vector2(150, 18));
                K.SetRect(gaugeBar.rectTransform, K.TopLeft, K.TopRight, K.TopLeft, new Vector2(310, -109), new Vector2(-324, 4));
                K.SetRect(statusRoot, K.TopLeft, K.TopRight, K.TopLeft, new Vector2(156, -130), new Vector2(-170, 26));
                K.Place(popup.rectTransform, K.Left, new Vector2(20, 10), new Vector2(300, 100));
            }

            var view = root.gameObject.AddComponent<BattleUnitView>();
            K.SetField(view, "group", group);
            K.SetField(view, "body", body);
            K.SetField(view, "portrait", portrait);
            K.SetField(view, "portraitLabel", portraitLabel);
            K.SetField(view, "hitFlash", hitFlash);
            K.SetField(view, "damageTint", tint);
            K.SetField(view, "accentBar", accent);
            K.SetField(view, "hexIcon", hex);
            K.SetField(view, "nameText", nameText);
            K.SetField(view, "idText", idText);
            K.SetField(view, "typeText", typeText);
            K.SetField(view, "hpFill", hpFill.rectTransform);
            K.SetField(view, "hpGhost", hpGhost.rectTransform);
            K.SetField(view, "hpText", hpText);
            K.SetField(view, "gaugeFill", gaugeFill.rectTransform);
            K.SetField(view, "gaugeText", gaugeText);
            K.SetField(view, "statusRoot", statusRoot);
            K.SetField(view, "statusTemplate", chipText);
            K.SetField(view, "frame", frame);
            K.SetField(view, "popupTemplate", popup);
            K.SetField(view, "numberFont", UIArtGenerator.Display);
            K.SetField(view, "labelFont", UIArtGenerator.MonoMedium);
            return view;
        }

        // ================================================================ 능력치 창

        /// <summary>
        /// 능력치 창 (UNIT STATUS). 왼쪽: 그림, 진영, 타입, 이름, id, HP. 오른쪽: 능력치 줄(Battle_Stat.csv), 상태, 상성. 아래: 스킬 4칸.
        /// </summary>
        static BattleStatusPanel CreateStatusPanel(Canvas canvas)
        {
            var win = K.CreateWindow(canvas.transform, "StatusPanel", new Vector2(1320, 960), "UNIT STATUS", "STATUS", "능력치", closeButton: true);
            K.SetValue(win.anim, "showScale", sp => sp.floatValue = 0.96f);
            var body = win.body;

            // 왼쪽
            var frame = K.CutPanel("PortraitFrame", body, 10, UITheme.Hex("#0B0D12"), UITheme.LineDim);
            K.Place(frame.rectTransform, K.TopLeft, new Vector2(40, -26), new Vector2(400, 380));
            var portrait = K.Img("Portrait", frame.transform, Color.white);
            K.Stretch(portrait.rectTransform, 14, 14, 14, 14);
            var portraitLabel = K.Mono("PortraitLabel", portrait.transform, "", 15, UITheme.TextDim, TextAlignmentOptions.Center, 8f);
            K.Stretch(portraitLabel.rectTransform);
            var accent = K.Sprite("AccentBar", frame.transform, "ui_grad_v", UITheme.Cyan);
            K.SetRect(accent.rectTransform, K.BottomLeft, K.TopLeft, K.Left, new Vector2(1, -6), new Vector2(3, -12));
            var side = K.Mono("Side", body, "ALLY", 14, UITheme.Cyan, TextAlignmentOptions.Left, 30f);
            K.Place(side.rectTransform, K.TopLeft, new Vector2(42, -424), new Vector2(200, 20));
            var type = K.Mono("Type", body, "TYPE", 14, UITheme.PeriwinkleBright, TextAlignmentOptions.Right, 20f);
            K.Place(type.rectTransform, K.TopLeft, new Vector2(240, -424), new Vector2(200, 20));
            var name = K.Text("Name", body, "이름", 36, UITheme.TextBright, TextAlignmentOptions.Left, spacing: 6f, bold: true);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            K.Place(name.rectTransform, K.TopLeft, new Vector2(40, -450), new Vector2(400, 48));
            var sub = K.Mono("Sub", body, "ID", 13, UITheme.TextDim, TextAlignmentOptions.Left, 16f);
            K.Place(sub.rectTransform, K.TopLeft, new Vector2(42, -500), new Vector2(400, 20));
            var hpLabel = K.Mono("HpLabel", body, "HP", 13, UITheme.TextDim, TextAlignmentOptions.Left, 20f);
            K.Place(hpLabel.rectTransform, K.TopLeft, new Vector2(42, -540), new Vector2(60, 20));
            var hpText = K.Mono("HpText", body, "0 / 0", 17, UITheme.Text, TextAlignmentOptions.Right, 4f);
            K.Place(hpText.rectTransform, K.TopLeft, new Vector2(240, -538), new Vector2(200, 22));
            var hpBar = K.Sprite("HpBar", body, "ui_cut6_fill", UITheme.Hex("#151823"), sliced: true);
            K.Place(hpBar.rectTransform, K.TopLeft, new Vector2(40, -566), new Vector2(400, 18));
            var hpFill = K.Img("Fill", hpBar.transform, UITheme.Cyan);
            hpFill.rectTransform.anchorMin = Vector2.zero;
            hpFill.rectTransform.anchorMax = Vector2.one;
            hpFill.rectTransform.offsetMin = new Vector2(2, 2);
            hpFill.rectTransform.offsetMax = new Vector2(-2, -2);

            // 오른쪽: 능력치
            SectionLabel(body, "STATS / 능력치", new Vector2(490, -26), 790);
            var statRoot = K.Rect("Stats", body);
            K.SetRect(statRoot, K.TopLeft, K.TopLeft, K.TopLeft, new Vector2(490, -74), new Vector2(790, 0));
            var statLayout = statRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            statLayout.childControlWidth = statLayout.childControlHeight = true;
            statLayout.childForceExpandHeight = false;
            statRoot.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var row = K.Rect("StatTemplate", statRoot);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            var rowName = K.Text("Name", row, "스탯", 22, UITheme.Text, TextAlignmentOptions.Left, spacing: 4f);
            K.Place(rowName.rectTransform, K.Left, new Vector2(0, 0), new Vector2(150, 32));
            var rowId = K.Mono("Id", row, "ID", 12, UITheme.TextDim, TextAlignmentOptions.Left, 16f);
            K.Place(rowId.rectTransform, K.Left, new Vector2(150, 0), new Vector2(80, 18));
            var rowBar = K.Img("Bar", row, UITheme.Rail);
            K.Place(rowBar.rectTransform, K.Left, new Vector2(236, 0), new Vector2(330, 8));
            var rowFill = K.Img("Fill", rowBar.transform, UITheme.Periwinkle);
            rowFill.rectTransform.anchorMin = Vector2.zero;
            rowFill.rectTransform.anchorMax = Vector2.one;
            rowFill.rectTransform.offsetMin = rowFill.rectTransform.offsetMax = Vector2.zero;
            var rowValue = K.Mono("Value", row, "0", 18, UITheme.TextBright, TextAlignmentOptions.Right, 4f);
            K.Place(rowValue.rectTransform, K.Left, new Vector2(580, 0), new Vector2(160, 26));
            var rowStage = K.Mono("Stage", row, "", 15, UITheme.Cyan, TextAlignmentOptions.Right, 0f);
            K.Place(rowStage.rectTransform, K.Left, new Vector2(744, 0), new Vector2(44, 22));
            var rowView = row.gameObject.AddComponent<BattleStatRow>();
            K.SetField(rowView, "nameText", rowName);
            K.SetField(rowView, "idText", rowId);
            K.SetField(rowView, "barFill", rowFill.rectTransform);
            K.SetField(rowView, "barImage", rowFill);
            K.SetField(rowView, "valueText", rowValue);
            K.SetField(rowView, "stageText", rowStage);

            // 오른쪽: 상태, 상성 (능력치 6줄까지 겹치지 않는 위치)
            SectionLabel(body, "STATE / 상태", new Vector2(490, -366), 790);
            var statusText = K.Text("StatusText", body, "없음", 21, UITheme.Text, TextAlignmentOptions.Left, spacing: 2f);
            K.Place(statusText.rectTransform, K.TopLeft, new Vector2(490, -410), new Vector2(790, 32));
            SectionLabel(body, "MATCHUP / 상성", new Vector2(490, -450), 790);
            var matchup = K.Text("Matchup", body, "", 20, UITheme.Text, TextAlignmentOptions.TopLeft, spacing: 2f);
            matchup.lineSpacing = 10;
            K.Place(matchup.rectTransform, K.TopLeft, new Vector2(490, -494), new Vector2(790, 70));

            // 아래: 스킬
            // 아래: 스킬 2열 x 2행
            SectionLabel(body, "SKILLS / 스킬", new Vector2(40, -600), 1240);
            var skillRoot = K.Rect("Skills", body);
            K.Place(skillRoot, K.TopLeft, new Vector2(40, -644), new Vector2(1240, 190));
            var skillGrid = skillRoot.gameObject.AddComponent<GridLayoutGroup>();
            skillGrid.cellSize = new Vector2(614, 84);
            skillGrid.spacing = new Vector2(12, 8);
            skillGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            skillGrid.constraintCount = 2;
            var skillTemplate = SkillCard("SkillTemplate", skillRoot, interactive: false);

            var panel = canvas.gameObject.AddComponent<BattleStatusPanel>();
            K.SetField(panel, "panel", win.anim);
            K.SetField(panel, "closeButton", win.close);
            K.SetField(panel, "portrait", portrait);
            K.SetField(panel, "portraitLabel", portraitLabel);
            K.SetField(panel, "accentBar", accent);
            K.SetField(panel, "nameText", name);
            K.SetField(panel, "subText", sub);
            K.SetField(panel, "sideText", side);
            K.SetField(panel, "typeText", type);
            K.SetField(panel, "hpFill", hpFill.rectTransform);
            K.SetField(panel, "hpText", hpText);
            K.SetField(panel, "statRoot", statRoot);
            K.SetField(panel, "statTemplate", rowView);
            K.SetField(panel, "statusText", statusText);
            K.SetField(panel, "matchupText", matchup);
            K.SetField(panel, "skillRoot", skillRoot);
            K.SetField(panel, "skillTemplate", skillTemplate);
            return panel;
        }

        static void SectionLabel(Transform parent, string text, Vector2 pos, float width)
        {
            var label = K.Mono("Section", parent, text, 13, UITheme.TextDim, TextAlignmentOptions.BottomLeft, 30f);
            K.Place(label.rectTransform, K.TopLeft, pos, new Vector2(width, 26));
            var line = K.Img("SectionLine", parent, UITheme.LineDim);
            K.Place(line.rectTransform, K.TopLeft, pos + new Vector2(0, -34), new Vector2(width, 2));
        }

        // ================================================================ Title 씬

        static void BuildTitleScene(TextAsset episodesCsv)
        {
            var scene = OpenClean(TitleScenePath);
            CreateCameraAndLight();
            CreateEventSystem();

            var canvas = CreateCanvas("TitleCanvas", 0);
            K.Stretch(K.Img("Background", canvas.transform, UITheme.Bg).rectTransform);
            var ambient = K.Rect("Ambient", canvas.transform);
            K.Stretch(ambient);

            var intro = K.Rect("Intro", canvas.transform);
            K.Stretch(intro);
            var introGroup = intro.gameObject.AddComponent<CanvasGroup>();

            // 가운데: eyebrow, 로고, 부제, 선, 메뉴 (개발 빌드에서는 TitleMenu가 왼쪽으로 민다)
            var center = K.Rect("Center", intro);
            K.Place(center, K.Center, Vector2.zero, new Vector2(1320, 1080));
            var eyebrow = K.Mono("Eyebrow", center, "STORY  ·  EXPLORATION  ·  TURN-BASED BATTLE", 15, UITheme.Amber, TextAlignmentOptions.Center, 50f);
            K.Place(eyebrow.rectTransform, K.Center, new Vector2(0, 300), new Vector2(1200, 24));
            // 가제 미정. 세계관 문서명을 임시로 쓴다.
            var logo = K.Display("Logo", center, "PROJECT : VITA", 62, UITheme.Hex("#E4E7F0"), TextAlignmentOptions.Center, 30f);
            K.Place(logo.rectTransform, K.Center, new Vector2(0, 218), new Vector2(1200, 100));
            var sub = K.Text("Subtitle", center, "가제 · 데모 빌드", 22, UITheme.TextDim, TextAlignmentOptions.Center, spacing: 60f);
            K.Place(sub.rectTransform, K.Center, new Vector2(0, 140), new Vector2(900, 36));
            var line = K.Sprite("Line", center, "ui_grad_hc", UITheme.Periwinkle);
            K.Place(line.rectTransform, K.Center, new Vector2(0, 92), new Vector2(320, 2));

            var menu = K.Rect("Menu", center);
            K.SetRect(menu, K.Center, K.Center, K.Top, new Vector2(0, 50), new Vector2(440, 0));
            var menuLayout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            menuLayout.spacing = 6;
            menuLayout.childControlWidth = menuLayout.childControlHeight = true;
            menuLayout.childForceExpandHeight = false;
            menu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var startButton = TitleButton(menu, "처음부터");
            var continueButton = TitleButton(menu, "이어하기");
            var loadButton = TitleButton(menu, "불러오기");
            var settingsButton = TitleButton(menu, "설정");
            var quitButton = TitleButton(menu, "종료");

            var hint = K.Text("ControlsHint", center,
                "클릭 / Space / Enter: 넘기기   숫자 키: 선택지   L, 휠 위로: 대사 기록   A: 오토   Tab: 스킵   Ctrl: 빨리감기   Esc: 메뉴\n" +
                "<size=88%>개발용   F1: 개발 정보 숨기기   F5: 지금 노드 다시 시작</size>", 17, UITheme.TextDim.WithAlpha(0.7f), TextAlignmentOptions.Bottom, spacing: 2f);
            K.Place(hint.rectTransform, K.Bottom, new Vector2(0, 26), new Vector2(1320, 60));

            // ---- 에피소드 선택 (개발용)
            var panel = K.CutPanel("EpisodePanel", intro, 16, UITheme.Panel.WithAlpha(0.9f), UITheme.Line);
            K.SetRect(panel.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(430, -60));
            var header = K.Mono("Header", panel.transform, "EPISODE SELECT // DEV", 14, UITheme.Amber, TextAlignmentOptions.TopLeft, 26f);
            K.SetRect(header.rectTransform, K.TopLeft, K.TopRight, K.Top, new Vector2(0, -30), new Vector2(-60, 22));
            var headerSub = K.Text("Sub", panel.transform, "Episodes.csv의 행마다 버튼이 생긴다", 19, UITheme.TextDim, TextAlignmentOptions.TopLeft);
            K.SetRect(headerSub.rectTransform, K.TopLeft, K.TopRight, K.Top, new Vector2(0, -58), new Vector2(-60, 28));
            var list = K.Rect("List", panel.transform);
            K.Stretch(list, 24, 24, 24, 110);
            var listLayout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 10;
            listLayout.childAlignment = TextAnchor.UpperCenter;
            listLayout.childControlWidth = listLayout.childControlHeight = true;
            listLayout.childForceExpandHeight = false;
            var episodeTemplate = K.Button("EpisodeButtonTemplate", list, "에피소드", 25, 84);

            // ---- 분위기 효과 (맨 위)
            var fxLayer = K.Rect("ScreenFx", canvas.transform);
            K.Stretch(fxLayer);
            K.CreateScreenFx(ambient, fxLayer);

            // ---- 설정, 불러오기, 확인창, 알림
            var overlayCanvas = CreateCanvas("OverlayCanvas", 50);
            var confirm = CreateConfirmDialog(overlayCanvas);
            var slots = CreateSaveSlotPanel(overlayCanvas, confirm);
            var settings = CreateSettingsPanel(overlayCanvas);
            CreateToast(overlayCanvas);

            var menuComp = canvas.gameObject.AddComponent<TitleMenu>();
            K.SetField(menuComp, "episodesCsv", episodesCsv);
            K.SetField(menuComp, "intro", introGroup);
            K.SetField(menuComp, "centerGroup", center);
            K.SetField(menuComp, "menuRoot", menu);
            K.SetField(menuComp, "startButton", startButton);
            K.SetField(menuComp, "continueButton", continueButton);
            K.SetField(menuComp, "loadButton", loadButton);
            K.SetField(menuComp, "settingsButton", settingsButton);
            K.SetField(menuComp, "quitButton", quitButton);
            K.SetField(menuComp, "settings", settings);
            K.SetField(menuComp, "slots", slots);
            K.SetField(menuComp, "confirm", confirm);
            K.SetField(menuComp, "episodePanel", panel.gameObject);
            K.SetField(menuComp, "episodeListRoot", list);
            K.SetField(menuComp, "episodeButtonTemplate", episodeTemplate);

            EditorSceneManager.SaveScene(scene, TitleScenePath);
            Debug.Log("[Setup] Title 씬 생성: " + TitleScenePath);
        }

        /// <summary>타이틀 메뉴 글자 버튼: 마우스를 올리면 밝아지고 자간이 넓어지며 왼쪽에 시안 ▸가 나타난다.</summary>
        static Button TitleButton(Transform menu, string label)
        {
            var root = K.Img("Title_" + label, menu, new Color(0, 0, 0, 0), raycast: true);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = 62;
            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = root;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var text = K.Text("Label", root.transform, label, 29, UITheme.TextDim, TextAlignmentOptions.Center, spacing: 30f);
            K.Stretch(text.rectTransform);
            var marker = K.Sprite("Marker", root.transform, "ui_tri", UITheme.Cyan);
            K.Place(marker.rectTransform, K.Left, new Vector2(46, 0), new Vector2(10, 12));
            var hover = root.gameObject.AddComponent<UIHoverFx>();
            K.SetArray(hover, "reveal", new Object[] { marker });
            K.SetArray(hover, "tint", new Object[] { text });
            K.SetValue(hover, "hoverColor", sp => sp.colorValue = UITheme.TextBright);
            K.SetField(hover, "spacing", text);
            K.SetValue(hover, "spacingAdd", sp => sp.floatValue = 8f);
            return button;
        }

        // ================================================================ Build Settings

        static void ApplyBuildScenes()
        {
            // 네 씬을 맨 앞에 두고, 그 밖에 이미 등록된 씬(에피소드 전용 씬 등)은 뒤에 유지한다.
            var first = new[] { TitleScenePath, StoryScenePath, ExplorationScenePath, BattleScenePath };
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

            // URP 2D에서 Lit 스프라이트가 검게 나오지 않도록 전역 조명을 둔다.
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

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }
    }
}
