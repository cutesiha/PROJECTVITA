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

            var backlog = CreatePauseCanvas(menuButton, withBacklog: true);
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
            p.nameText = K.Text("NameText", namePanel.transform, "이름", 26, UITheme.TextBright, TextAlignmentOptions.Left, spacing: 6f);
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

        /// <summary>일시정지 메뉴와 그 위에 뜨는 창들(대사 기록, 세이브 슬롯, 설정, 확인창, 알림). 대사 기록을 만들었으면 돌려준다.</summary>
        static BacklogPanel CreatePauseCanvas(Button menuButton, bool withBacklog)
        {
            var canvas = CreateCanvas("PauseCanvas", 50);

            int items = withBacklog ? 7 : 6;
            var win = K.CreateWindow(canvas.transform, "PausePanel", new Vector2(640, 124 + 20 + items * 66 + 26), "SIGNAL INTERRUPTED", "PAUSED", "일시정지", closeButton: false);
            var list = K.Rect("Items", win.body);
            K.Stretch(list, 22, 26, 22, 20);
            var listLayout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.childControlWidth = listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            var resume = PauseItem(list, "계속하기", "RESUME");
            var backlogButton = withBacklog ? PauseItem(list, "대사 기록", "BACKLOG") : null;
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
            var backlog = withBacklog ? CreateBacklogPanel(canvas) : null;
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
            return backlog;
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

        static BacklogPanel CreateBacklogPanel(Canvas canvas)
        {
            var win = K.CreateWindow(canvas.transform, "BacklogPanel", new Vector2(1220, 840), "RECOVERED TRANSMISSION", "BACKLOG", "기록", closeButton: true);

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

            var empty = K.Text("Empty", win.body, "아직 기록된 대사가 없습니다.", 24, UITheme.TextDim, TextAlignmentOptions.Center);
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
            var plateText = K.Text("LocationName", plate.transform, "장소", 28, UITheme.TextBright, TextAlignmentOptions.Center, spacing: 10f);
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

            // 적 줄 (위쪽 가운데), 아군 줄 (아래 왼쪽)
            var enemyRow = K.Rect("EnemyRow", shake);
            K.SetRect(enemyRow, K.Top, K.Top, K.Top, new Vector2(-120, -100), new Vector2(1300, 560));
            var enemyLayout = enemyRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            enemyLayout.spacing = 40;
            enemyLayout.childAlignment = TextAnchor.MiddleCenter;
            enemyLayout.childControlWidth = false;
            enemyLayout.childControlHeight = false;

            var allyRow = K.Rect("AllyRow", shake);
            K.SetRect(allyRow, K.BottomLeft, K.BottomLeft, K.BottomLeft, new Vector2(40, 30), new Vector2(1180, 300));
            var allyLayout = allyRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            allyLayout.spacing = 20;
            allyLayout.childAlignment = TextAnchor.LowerLeft;
            allyLayout.childControlWidth = false;
            allyLayout.childControlHeight = false;

            var enemyTemplate = CreateUnitView("EnemyTemplate", enemyRow, new Vector2(360, 540), enemy: true);
            var allyTemplate = CreateUnitView("AllyTemplate", allyRow, new Vector2(380, 290), enemy: false);

            // 분위기 효과 (전투 화면 위, 명령 창 아래)
            var fxLayer = K.Rect("ScreenFx", canvas.transform);
            K.Stretch(fxLayer);
            K.CreateScreenFx(ambient, fxLayer);

            // 명령 창 (아래 오른쪽)
            var panel = K.CutPanel("CommandPanel", canvas.transform, 16, UITheme.Panel, UITheme.Line, raycast: true);
            K.Place(panel.rectTransform, K.BottomRight, new Vector2(-40, 30), new Vector2(640, 380));
            var panelLine = K.Sprite("TopLine", panel.transform, "ui_grad_h", UITheme.Periwinkle);
            K.SetRect(panelLine.rectTransform, K.TopLeft, K.TopRight, K.Top, new Vector2(-100, 0), new Vector2(-232, 3));
            var actor = K.Text("Actor", panel.transform, "행동", 30, UITheme.TextBright, TextAlignmentOptions.Left, spacing: 6f);
            K.SetRect(actor.rectTransform, K.TopLeft, K.TopRight, K.Top, new Vector2(0, -22), new Vector2(-60, 46));
            var skillButtons = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                var b = K.Button("Skill" + (i + 1), panel.transform, "스킬", 24, 0, cut: 10, accent: UITheme.Cyan);
                int col = i % 2, row = i / 2;
                K.Place((RectTransform)b.transform, K.TopLeft, new Vector2(30 + col * 295, -84 - row * 82), new Vector2(285, 72));
                skillButtons[i] = b;
            }
            var info = K.Text("SkillInfo", panel.transform, "", 21, UITheme.TextDim, TextAlignmentOptions.TopLeft, spacing: 2f);
            K.SetRect(info.rectTransform, K.BottomLeft, K.BottomRight, K.Bottom, new Vector2(0, 20), new Vector2(-60, 90));
            var back = K.Button("BackButton", panel.transform, "뒤로", 21, 0, cut: 6);
            K.Place((RectTransform)back.transform, K.TopRight, new Vector2(-24, -18), new Vector2(110, 46));

            // 정보
            var round = K.Display("Round", canvas.transform, "ROUND 1", 30, UITheme.Hex("#DFE2EC"), TextAlignmentOptions.Left, 20f);
            K.Place(round.rectTransform, K.TopLeft, new Vector2(40, -30), new Vector2(400, 46));
            var logText = K.Text("Log", canvas.transform, "", 21, UITheme.TextDim, TextAlignmentOptions.TopLeft, spacing: 2f);
            K.Place(logText.rectTransform, K.TopLeft, new Vector2(40, -88), new Vector2(520, 220));

            var bar = K.Rect("QuickBar", canvas.transform);
            K.Place(bar, K.TopRight, new Vector2(-30, -26), new Vector2(68, 58));
            var menuButton = K.IconButton("MenuButton", bar, "icon_menu", "MENU", new Vector2(68, 58));
            K.Stretch((RectTransform)menuButton.transform);
            var autoButton = K.Button("AutoButton", canvas.transform, "자동 전투: 끔", 21, 0, cut: 6);
            K.Place((RectTransform)autoButton.transform, K.TopRight, new Vector2(-30, -98), new Vector2(220, 50));

            // 결과
            var result = K.Img("ResultPanel", canvas.transform, UITheme.Overlay, raycast: true);
            K.Stretch(result.rectTransform);
            var resultText = K.Display("ResultText", result.transform, "승리", 110, UITheme.TextBright, TextAlignmentOptions.Center, 30f);
            K.Place(resultText.rectTransform, K.Center, new Vector2(0, 80), new Vector2(1200, 180));
            var resultButton = K.Button("ResultButton", result.transform, "계속", 30, 0, cut: 10, accent: UITheme.Cyan);
            K.Place((RectTransform)resultButton.transform, K.Center, new Vector2(0, -90), new Vector2(360, 80));

            var flash = K.Img("Flash", canvas.transform, Color.white);
            K.Stretch(flash.rectTransform);

            CreatePauseCanvas(menuButton, withBacklog: false);

            var controller = canvas.gameObject.AddComponent<BattleController>();
            K.SetField(controller, "episodesCsv", episodesCsv);
            K.SetField(controller, "background", bg);
            K.SetField(controller, "shakeRoot", shake);
            K.SetField(controller, "enemyRow", enemyRow);
            K.SetField(controller, "allyRow", allyRow);
            K.SetField(controller, "enemyTemplate", enemyTemplate);
            K.SetField(controller, "allyTemplate", allyTemplate);
            K.SetField(controller, "flash", flash);
            K.SetField(controller, "commandPanel", panel.gameObject);
            K.SetField(controller, "actorText", actor);
            K.SetArray(controller, "skillButtons", skillButtons);
            K.SetField(controller, "skillInfo", info);
            K.SetField(controller, "backButton", back);
            K.SetField(controller, "autoButton", autoButton);
            K.SetField(controller, "roundText", round);
            K.SetField(controller, "logText", logText);
            K.SetField(controller, "resultPanel", result.gameObject);
            K.SetField(controller, "resultText", resultText);
            K.SetField(controller, "resultButton", resultButton);

            EditorSceneManager.SaveScene(scene, BattleScenePath);
            Debug.Log("[Setup] Battle 씬 생성: " + BattleScenePath);
        }

        /// <summary>전투 유닛 카드. 적은 큰 그림 위주, 아군은 작은 그림과 정보 위주.</summary>
        static BattleUnitView CreateUnitView(string name, Transform parent, Vector2 size, bool enemy)
        {
            // 바탕은 불투명해야 뒤의 Frame이 테두리로만 보인다.
            var root = K.Img(name, parent, UITheme.Hex("#10131B"), raycast: true);
            root.rectTransform.sizeDelta = size;
            var frame = root.gameObject.AddComponent<Outline>();
            frame.effectDistance = new Vector2(4, -4);
            frame.effectColor = Color.clear;
            var line = K.Sprite("Line", root.transform, "ui_cut10_line", UITheme.Line, sliced: true);
            K.Stretch(line.rectTransform);
            var body = K.Rect("Body", root.transform);
            K.Stretch(body);

            var portrait = K.Img("Portrait", body, Color.white);
            var portraitLabel = K.Mono("PortraitLabel", portrait.transform, "", 18, UITheme.TextDim, TextAlignmentOptions.Center, 10f);
            K.Stretch(portraitLabel.rectTransform, 8, 8, 8, 8);
            var nameText = K.Text("Name", body, "이름", enemy ? 28 : 26, UITheme.TextBright, TextAlignmentOptions.Left, spacing: 4f);
            var hpBar = K.Img("HpBar", body, UITheme.Rail);
            var hpFill = K.Img("HpFill", hpBar.transform, new Color(0.35f, 0.8f, 0.4f));
            hpFill.rectTransform.anchorMin = Vector2.zero;
            hpFill.rectTransform.anchorMax = Vector2.one;
            hpFill.rectTransform.offsetMin = hpFill.rectTransform.offsetMax = Vector2.zero;
            var hpText = K.Mono("HpText", body, "0 / 0", 18, UITheme.Text, TextAlignmentOptions.Right, 4f);
            var infoText = K.Text("Info", body, "", 19, UITheme.TextDim, TextAlignmentOptions.TopLeft);
            var popup = K.Text("Popup", body, "0", 56, Color.white, TextAlignmentOptions.Center);
            popup.fontStyle = FontStyles.Bold;
            popup.outlineWidth = 0.25f;
            popup.outlineColor = new Color32(0, 0, 0, 255);

            if (enemy)
            {
                K.SetRect(portrait.rectTransform, K.BottomLeft, K.TopRight, K.Center, new Vector2(0, 30), new Vector2(-20, -140));
                K.SetRect(nameText.rectTransform, K.BottomLeft, K.BottomRight, K.Bottom, new Vector2(0, 62), new Vector2(-24, 40));
                K.SetRect(hpBar.rectTransform, K.BottomLeft, K.BottomRight, K.Bottom, new Vector2(0, 42), new Vector2(-24, 12));
                K.SetRect(hpText.rectTransform, K.BottomLeft, K.BottomRight, K.Bottom, new Vector2(0, 62), new Vector2(-24, 40));
                K.SetRect(infoText.rectTransform, K.BottomLeft, K.BottomRight, K.Bottom, new Vector2(0, 6), new Vector2(-24, 30));
                K.SetRect(popup.rectTransform, K.Center, K.Center, K.Center, new Vector2(0, 60), new Vector2(300, 90));
            }
            else
            {
                K.SetRect(portrait.rectTransform, K.BottomLeft, K.TopLeft, K.Left, new Vector2(10, 0), new Vector2(150, -20));
                K.SetRect(nameText.rectTransform, K.TopLeft, K.TopRight, K.TopLeft, new Vector2(172, -14), new Vector2(-184, 40));
                K.SetRect(hpBar.rectTransform, K.TopLeft, K.TopRight, K.TopLeft, new Vector2(172, -66), new Vector2(-190, 12));
                K.SetRect(hpText.rectTransform, K.TopLeft, K.TopRight, K.TopLeft, new Vector2(172, -84), new Vector2(-190, 34));
                K.SetRect(infoText.rectTransform, K.BottomLeft, K.TopRight, K.TopLeft, new Vector2(172, -130), new Vector2(-190, -140));
                K.SetRect(popup.rectTransform, K.Center, K.Center, K.Center, new Vector2(0, 40), new Vector2(300, 90));
            }

            var view = root.gameObject.AddComponent<BattleUnitView>();
            K.SetField(view, "body", body);
            K.SetField(view, "portrait", portrait);
            K.SetField(view, "portraitLabel", portraitLabel);
            K.SetField(view, "nameText", nameText);
            K.SetField(view, "hpFill", hpFill.rectTransform);
            K.SetField(view, "hpText", hpText);
            K.SetField(view, "infoText", infoText);
            K.SetField(view, "frame", frame);
            K.SetField(view, "popupTemplate", popup);
            return view;
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
