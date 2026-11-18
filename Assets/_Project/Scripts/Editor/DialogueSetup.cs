using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectVita.Battle;
using ProjectVita.Core;
using ProjectVita.Exploration;
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
    /// 대사 시스템(문서 4-2, 4-4)의 에셋과 씬을 메뉴 하나로 만든다.
    /// - Yarn Project가 없으면 만든다 (Dialogue 폴더 아래 모든 .yarn 포함).
    /// - 리소스 목록(AssetCatalog)을 갱신한다.
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
        const string ExplorationScenePath = ScenesDir + "/" + EpisodeFlow.ExplorationSceneName + ".unity";
        const string BattleScenePath = ScenesDir + "/" + EpisodeFlow.BattleSceneName + ".unity";

        // 기준 해상도. Player Settings(1단계)와 같다.
        static readonly Vector2 RefResolution = new Vector2(1920, 1080);

        // 임시 UI 색. 두부님의 UI 아트가 오면 바꾼다.
        // 감각: 어두운 바탕에 강한 포인트 색, 각진 모양, 굵은 글자 (참고 게임의 에셋이나 레이아웃은 쓰지 않는다).
        static readonly Color ColTitleBackground = Hex("#0B0C0F");
        static readonly Color ColBox = new Color(0.03f, 0.03f, 0.04f, 0.88f);
        static readonly Color ColAccent = Hex("#B3261E");
        static readonly Color ColButton = Hex("#22262E");
        static readonly Color ColPanel = Hex("#14171C");
        static readonly Color ColText = Hex("#ECEDEF");
        static readonly Color ColSubText = Hex("#9AA0A6");
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
            SetField(director, "runner", d.runner);
            SetField(director, "stage", d.stage);
            SetField(director, "episodesCsv", episodesCsv);

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

        /// <summary>대사가 나오는 씬의 공통 부분: 무대, 대사창, 선택지, 화면 효과, 일시정지, 개발용 표시, DialogueRunner.</summary>
        static DialogueScene BuildDialogueScene(string path)
        {
            var scene = OpenClean(path);
            CreateCameraAndLight();
            CreateEventSystem();

            var canvas = CreateCanvas("StoryCanvas", 0);
            var stage = canvas.gameObject.AddComponent<StoryStage>();

            // ---- 무대 (아래부터 그려진다)
            Stretch(CreateImage("StageBase", canvas.transform, Color.black).rectTransform);
            var shakeRoot = CreateRect("ShakeRoot", canvas.transform);
            Stretch(shakeRoot);
            var bgBack = CreateImage("BackgroundBack", shakeRoot, Color.white);
            Stretch(bgBack.rectTransform);
            var bgFront = CreateImage("BackgroundFront", shakeRoot, Color.white);
            Stretch(bgFront.rectTransform);
            var characters = CreateRect("Characters", shakeRoot);
            Stretch(characters);
            var cg = CreateImage("CG", shakeRoot, Color.white);
            Stretch(cg.rectTransform);

            var vignette = CreateImage("Vignette", canvas.transform, Color.white);
            Stretch(vignette.rectTransform);
            var wipe = CreateImage("Wipe", canvas.transform, Color.black);
            Stretch(wipe.rectTransform);

            // ---- 대사창
            var box = CreateImage("DialogueBox", canvas.transform, ColBox);
            var boxRt = box.rectTransform;
            boxRt.anchorMin = new Vector2(0, 0);
            boxRt.anchorMax = new Vector2(1, 0);
            boxRt.pivot = new Vector2(0.5f, 0);
            boxRt.offsetMin = new Vector2(80, 36);
            boxRt.offsetMax = new Vector2(-80, 36 + 290);

            var accent = CreateImage("AccentLine", boxRt, ColAccent);
            SetRect(accent.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 4));

            var namePlate = CreateImage("NamePlate", boxRt, ColAccent);
            namePlate.gameObject.AddComponent<UISkew>().Skew = 26f;
            SetRect(namePlate.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(36, 2), new Vector2(330, 62));
            var nameText = CreateText("NameText", namePlate.transform, "이름", 34, ColText, TextAlignmentOptions.Center);
            nameText.fontStyle = FontStyles.Bold;
            Stretch(nameText.rectTransform, 16, 4);

            var bodyText = CreateText("BodyText", boxRt, "", 36, ColText, TextAlignmentOptions.TopLeft);
            bodyText.lineSpacing = 14;
            Stretch(bodyText.rectTransform);
            bodyText.rectTransform.offsetMin = new Vector2(60, 40);
            bodyText.rectTransform.offsetMax = new Vector2(-60, -62);

            var next = CreateImage("NextIndicator", boxRt, ColAccent);
            SetRect(next.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-46, 40), new Vector2(18, 18));
            next.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);

            // ---- 로그 / 오토 / 스킵 (대사창 오른쪽 위, 대사창의 자식이라 대사창과 같이 보이고 숨는다)
            var quick = CreateRect("QuickMenu", boxRt);
            SetRect(quick, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(-36, 10), new Vector2(384, 44));
            var quickLayout = quick.gameObject.AddComponent<HorizontalLayoutGroup>();
            quickLayout.spacing = 10;
            quickLayout.childControlWidth = true;
            quickLayout.childControlHeight = true;
            quickLayout.childForceExpandWidth = true;
            quickLayout.childForceExpandHeight = true;
            var logButton = CreateButton("LogButton", quick, "로그", 20, 0, skew: 12f);
            var autoButton = CreateButton("AutoButton", quick, "오토", 20, 0, skew: 12f);
            var skipButton = CreateButton("SkipButton", quick, "스킵", 20, 0, skew: 12f);
            var quickMenu = quick.gameObject.AddComponent<DialogueQuickMenu>();
            SetField(quickMenu, "logButton", logButton);
            SetField(quickMenu, "autoButton", autoButton);
            SetField(quickMenu, "skipButton", skipButton);

            // ---- 선택지
            var options = CreateRect("Options", canvas.transform);
            SetRect(options, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(960, 0));
            var layout = options.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            options.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var optionTemplate = CreateButton("OptionTemplate", options, "선택지", 32, 84, skew: 22f);

            // ---- 메뉴 버튼 (오른쪽 위)
            var menuButton = CreateButton("MenuButton", canvas.transform, "메뉴 (Esc)", 24, 0, skew: 14f);
            SetRect((RectTransform)menuButton.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -22), new Vector2(190, 52));

            // ---- 화면 효과 (대사창 위)
            var flash = CreateImage("Flash", canvas.transform, Color.white);
            Stretch(flash.rectTransform);
            var fade = CreateImage("Fade", canvas.transform, Color.black);
            Stretch(fade.rectTransform);

            SetField(stage, "shakeRoot", shakeRoot);
            SetField(stage, "backgroundBack", bgBack);
            SetField(stage, "backgroundFront", bgFront);
            SetField(stage, "characterRoot", characters);
            SetField(stage, "cgImage", cg);
            SetField(stage, "vignette", vignette);
            SetField(stage, "wipeOverlay", wipe.rectTransform);
            SetField(stage, "flash", flash);
            SetField(stage, "fadeOverlay", fade);

            var backlog = CreatePauseCanvas(menuButton, withBacklog: true);
            SetField(quickMenu, "backlog", backlog);
            CreateDevCanvas();

            // ---- 대화 시스템 (Runner + Presenter)
            var system = new GameObject("DialogueSystem");
            var presenter = system.AddComponent<StoryPresenter>();
            SetField(presenter, "dialogueBox", box.gameObject);
            SetField(presenter, "namePlate", namePlate.gameObject);
            SetField(presenter, "namePlateImage", namePlate);
            SetField(presenter, "nameText", nameText);
            SetField(presenter, "bodyText", bodyText);
            SetField(presenter, "nextIndicator", next.rectTransform);
            SetField(presenter, "optionsRoot", options);
            SetField(presenter, "optionTemplate", optionTemplate);

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

            return new DialogueScene { scene = scene, canvas = canvas, stage = stage, runner = runner, system = system, dialogueBox = boxRt };
        }

        /// <summary>일시정지 메뉴와 그 위에 뜨는 화면들(대사 기록, 세이브 슬롯, 설정). 대사 기록을 만들었으면 돌려준다.</summary>
        static BacklogPanel CreatePauseCanvas(Button menuButton, bool withBacklog)
        {
            var pauseCanvas = CreateCanvas("PauseCanvas", 50);
            var pausePanel = CreateImage("PausePanel", pauseCanvas.transform, new Color(0, 0, 0, 0.78f), raycast: true);
            Stretch(pausePanel.rectTransform);
            var pauseTitle = CreateText("Title", pausePanel.transform, "일시정지", 60, ColText, TextAlignmentOptions.Center);
            pauseTitle.fontStyle = FontStyles.Bold;
            SetRect(pauseTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 370), new Vector2(800, 90));
            var pauseList = CreateVerticalList("Buttons", pausePanel.transform, new Vector2(0, -30), 440);
            var resumeButton = CreateButton("ResumeButton", pauseList, "계속하기", 32, 72);
            var backlogButton = withBacklog ? CreateButton("BacklogButton", pauseList, "대사 기록", 32, 72) : null;
            var saveButton = CreateButton("SaveButton", pauseList, "저장", 32, 72);
            var loadButton = CreateButton("LoadButton", pauseList, "불러오기", 32, 72);
            var settingsButton = CreateButton("SettingsButton", pauseList, "설정", 32, 72);
            var titleButton = CreateButton("TitleButton", pauseList, "타이틀로", 32, 72);
            var quitButton = CreateButton("QuitButton", pauseList, "게임 종료", 32, 72);

            // 그리는 순서 = 만든 순서. 메뉴 위에 대사 기록, 슬롯, 설정이 뜬다.
            var backlog = withBacklog ? CreateBacklogPanel(pauseCanvas) : null;
            var slots = CreateSaveSlotPanel(pauseCanvas);
            var settings = CreateSettingsPanel(pauseCanvas);

            var pause = pauseCanvas.gameObject.AddComponent<PauseMenu>();
            SetField(pause, "panel", pausePanel.gameObject);
            SetField(pause, "openButton", menuButton);
            SetField(pause, "resumeButton", resumeButton);
            SetField(pause, "backlogButton", backlogButton);
            SetField(pause, "saveButton", saveButton);
            SetField(pause, "loadButton", loadButton);
            SetField(pause, "settingsButton", settingsButton);
            SetField(pause, "titleButton", titleButton);
            SetField(pause, "quitButton", quitButton);
            SetField(pause, "settings", settings);
            SetField(pause, "slots", slots);
            SetField(pause, "backlog", backlog);
            return backlog;
        }

        // ================================================================ 대사 기록 (4-7)

        static BacklogPanel CreateBacklogPanel(Canvas canvas)
        {
            var root = CreateImage("BacklogPanel", canvas.transform, new Color(0.02f, 0.02f, 0.03f, 0.975f), raycast: true);
            Stretch(root.rectTransform);
            var band = CreateImage("AccentBand", root.transform, ColAccent);
            band.gameObject.AddComponent<UISkew>().Skew = 120f;
            SetRect(band.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(60, 0), new Vector2(26, 0));
            var title = CreateText("Title", root.transform, "대사 기록", 52, ColText, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            SetRect(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(170, -48), new Vector2(600, 80));
            var hint = CreateText("Hint", root.transform, "휠 · 드래그: 스크롤     오른쪽 클릭 · L · Esc · 맨 아래에서 휠 아래로: 닫기", 24, ColSubText, TextAlignmentOptions.Left);
            SetRect(hint.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(176, -128), new Vector2(1300, 40));
            var close = CreateButton("CloseButton", root.transform, "닫기", 28, 0, skew: 16f);
            SetRect((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -50), new Vector2(200, 64));

            // 스크롤 영역
            var area = CreateRect("Scroll", root.transform);
            Stretch(area);
            area.offsetMin = new Vector2(170, 50);
            area.offsetMax = new Vector2(-170, -190);
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            var viewport = CreateImage("Viewport", area, Color.clear, raycast: true); // 휠과 드래그를 받으려면 레이캐스트 대상이 있어야 한다
            Stretch(viewport.rectTransform);
            viewport.rectTransform.offsetMax = new Vector2(-34, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = CreateRect("Content", viewport.transform);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 30;
            layout.padding = new RectOffset(10, 10, 10, 40);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var entry = CreateText("EntryTemplate", content, "이름\n대사", 32, ColText, TextAlignmentOptions.TopLeft);
            entry.lineSpacing = 10;

            var barGo = DefaultControls.CreateScrollbar(new DefaultControls.Resources());
            barGo.name = "Scrollbar";
            SetLayerRecursive(barGo, LayerMask.NameToLayer("UI"));
            barGo.transform.SetParent(area, false);
            SetRect((RectTransform)barGo.transform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, new Vector2(12, 0));
            var bar = barGo.GetComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            barGo.GetComponent<Image>().color = Hex("#2B2F38");
            bar.targetGraphic.color = ColAccent;

            scroll.content = content;
            scroll.viewport = viewport.rectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            var panel = canvas.gameObject.AddComponent<BacklogPanel>();
            SetField(panel, "root", root.gameObject);
            SetField(panel, "scroll", scroll);
            SetField(panel, "content", content);
            SetField(panel, "entryTemplate", entry);
            SetField(panel, "closeButton", close);
            return panel;
        }

        // ================================================================ 세이브 슬롯 (4-7, 타이틀과 일시정지 공용)

        static SaveSlotPanel CreateSaveSlotPanel(Canvas canvas)
        {
            var root = CreateImage("SaveSlotPanel", canvas.transform, new Color(0, 0, 0, 0.85f), raycast: true);
            Stretch(root.rectTransform);
            var window = CreateImage("Window", root.transform, ColPanel, raycast: true);
            SetRect(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1720, 960));
            var accent = CreateImage("AccentLine", window.transform, ColAccent);
            SetRect(accent.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 6));

            var title = CreateText("Title", window.transform, "저장", 48, ColText, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            SetRect(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -36), new Vector2(500, 70));
            var hint = CreateText("Hint", window.transform, "", 24, ColSubText, TextAlignmentOptions.Left);
            SetRect(hint.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -106), new Vector2(1300, 40));
            var close = CreateButton("CloseButton", window.transform, "닫기", 28, 0, skew: 16f);
            SetRect((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-50, -40), new Vector2(200, 64));

            // 2열 x 4행 = 자동 저장 + 슬롯 7개
            var grid = CreateRect("Grid", window.transform);
            SetRect(grid, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -170), new Vector2(1600, 760));
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(784, 172);
            gridLayout.spacing = new Vector2(32, 24);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 2;

            var cardButton = CreateButton("SlotTemplate", grid, "슬롯", 28, 0);
            var cardTitle = cardButton.GetComponentInChildren<TMP_Text>();
            cardTitle.name = "Title";
            cardTitle.alignment = TextAlignmentOptions.TopLeft;
            SetRect(cardTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(292, -16), new Vector2(470, 40));
            var thumb = CreateRect("Thumbnail", cardButton.transform).gameObject.AddComponent<RawImage>();
            thumb.raycastTarget = false;
            SetRect(thumb.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(256, 144));
            var thumbLabel = CreateText("ThumbnailLabel", thumb.transform, "", 22, ColSubText, TextAlignmentOptions.Center);
            Stretch(thumbLabel.rectTransform);
            var info = CreateText("Info", cardButton.transform, "", 22, ColText, TextAlignmentOptions.TopLeft);
            info.overflowMode = TextOverflowModes.Ellipsis;
            info.lineSpacing = 4;
            SetRect(info.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(292, -58), new Vector2(470, 104));
            var card = cardButton.gameObject.AddComponent<SaveSlotCard>();
            SetField(card, "button", cardButton);
            SetField(card, "thumbnail", thumb);
            SetField(card, "thumbnailLabel", thumbLabel);
            SetField(card, "title", cardTitle);
            SetField(card, "info", info);

            var panel = canvas.gameObject.AddComponent<SaveSlotPanel>();
            SetField(panel, "root", root.gameObject);
            SetField(panel, "titleText", title);
            SetField(panel, "hintText", hint);
            SetField(panel, "grid", grid);
            SetField(panel, "cardTemplate", card);
            SetField(panel, "closeButton", close);
            return panel;
        }

        static void CreateDevCanvas()
        {
            var devCanvas = CreateCanvas("DevCanvas", 100);
            var devText = CreateText("DevOverlay", devCanvas.transform, "", 20, ColDev, TextAlignmentOptions.TopLeft);
            SetRect(devText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -16), new Vector2(1100, 420));
            var overlay = devText.gameObject.AddComponent<DevOverlay>();
            SetField(overlay, "text", devText);
        }

        // ================================================================ Exploration 씬

        static void BuildExplorationScene(TextAsset episodesCsv)
        {
            var d = BuildDialogueScene(ExplorationScenePath);

            // 탐색 UI는 무대 위, 대사창 아래에 둔다.
            var layer = CreateRect("ExplorationLayer", d.canvas.transform);
            Stretch(layer);
            layer.SetSiblingIndex(d.dialogueBox.GetSiblingIndex());

            var hotspots = CreateRect("Hotspots", layer);
            Stretch(hotspots);
            var hotspotTemplate = CreateImage("HotspotTemplate", hotspots, Color.clear, raycast: true);
            var hotspotLabel = CreateText("Label", hotspotTemplate.transform, "이름", 28, ColText, TextAlignmentOptions.Center);
            hotspotLabel.fontStyle = FontStyles.Bold;
            hotspotLabel.outlineWidth = 0.2f;
            hotspotLabel.outlineColor = new Color32(0, 0, 0, 255);
            SetRect(hotspotLabel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(120, 80));
            var hotspot = hotspotTemplate.gameObject.AddComponent<ExplorationHotspot>();
            SetField(hotspot, "fill", hotspotTemplate);
            SetField(hotspot, "label", hotspotLabel);

            // 장소 이름 (위쪽 가운데. 왼쪽 위는 개발용 표시 자리)
            var plate = CreateImage("LocationPlate", layer, ColAccent);
            plate.gameObject.AddComponent<UISkew>().Skew = 24f;
            SetRect(plate.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(420, 66));
            var plateText = CreateText("LocationName", plate.transform, "장소", 34, ColText, TextAlignmentOptions.Center);
            plateText.fontStyle = FontStyles.Bold;
            Stretch(plateText.rectTransform, 16, 4);

            // 지도 버튼 (메뉴 버튼 아래)
            var mapButton = CreateButton("MapButton", layer, "지도", 26, 0, skew: 14f);
            SetRect((RectTransform)mapButton.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -86), new Vector2(190, 52));

            // 지도 화면
            var map = CreateImage("MapView", layer, new Color(0.04f, 0.045f, 0.055f, 0.96f), raycast: true);
            Stretch(map.rectTransform);
            var mapTitle = CreateText("Title", map.transform, "지도", 64, ColText, TextAlignmentOptions.Left);
            mapTitle.fontStyle = FontStyles.Bold;
            SetRect(mapTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(170, -120), new Vector2(800, 100));
            var mapHint = CreateText("Hint", map.transform, "갈 곳을 고른다. 조건을 채우면 새 장소가 나타난다.", 26, ColSubText, TextAlignmentOptions.Left);
            SetRect(mapHint.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(176, -220), new Vector2(1200, 50));
            var mapBand = CreateImage("AccentBand", map.transform, ColAccent);
            mapBand.gameObject.AddComponent<UISkew>().Skew = 120f;
            SetRect(mapBand.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(60, 0), new Vector2(26, 0));
            var mapList = CreateRect("List", map.transform);
            SetRect(mapList, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(170, -300), new Vector2(560, 0));
            var mapLayout = mapList.gameObject.AddComponent<VerticalLayoutGroup>();
            mapLayout.spacing = 16;
            mapLayout.childControlWidth = true;
            mapLayout.childControlHeight = true;
            mapLayout.childForceExpandHeight = false;
            mapList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var mapItem = CreateButton("MapItemTemplate", mapList, "장소", 34, 88, skew: 22f);

            var controller = d.system.AddComponent<ExplorationController>();
            SetField(controller, "runner", d.runner);
            SetField(controller, "stage", d.stage);
            SetField(controller, "episodesCsv", episodesCsv);
            SetField(controller, "hotspotRoot", hotspots);
            SetField(controller, "hotspotTemplate", hotspot);
            SetField(controller, "locationPlate", plate.gameObject);
            SetField(controller, "locationName", plateText);
            SetField(controller, "mapButton", mapButton);
            SetField(controller, "mapView", map.gameObject);
            SetField(controller, "mapListRoot", mapList);
            SetField(controller, "mapItemTemplate", mapItem);

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
            var bg = CreateImage("Background", canvas.transform, Hex("#15181E"));
            Stretch(bg.rectTransform);
            var shake = CreateRect("ShakeRoot", canvas.transform);
            Stretch(shake);

            // 적 줄 (위쪽 가운데), 아군 줄 (아래 왼쪽)
            var enemyRow = CreateRect("EnemyRow", shake);
            SetRect(enemyRow, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-120, -90), new Vector2(1300, 560));
            var enemyLayout = enemyRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            enemyLayout.spacing = 40;
            enemyLayout.childAlignment = TextAnchor.MiddleCenter;
            enemyLayout.childControlWidth = false;
            enemyLayout.childControlHeight = false;

            var allyRow = CreateRect("AllyRow", shake);
            SetRect(allyRow, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 30), new Vector2(1180, 300));
            var allyLayout = allyRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            allyLayout.spacing = 20;
            allyLayout.childAlignment = TextAnchor.LowerLeft;
            allyLayout.childControlWidth = false;
            allyLayout.childControlHeight = false;

            var enemyTemplate = CreateUnitView("EnemyTemplate", enemyRow, new Vector2(360, 540), enemy: true);
            var allyTemplate = CreateUnitView("AllyTemplate", allyRow, new Vector2(380, 290), enemy: false);

            // 명령 창 (아래 오른쪽)
            var panel = CreateImage("CommandPanel", canvas.transform, ColBox, raycast: true);
            SetRect(panel.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 30), new Vector2(640, 380));
            var panelAccent = CreateImage("AccentLine", panel.transform, ColAccent);
            SetRect(panelAccent.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 4));
            var actor = CreateText("Actor", panel.transform, "행동", 32, ColText, TextAlignmentOptions.Left);
            actor.fontStyle = FontStyles.Bold;
            SetRect(actor.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(-60, 50));
            var skillButtons = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                var b = CreateButton("Skill" + (i + 1), panel.transform, "스킬", 26, 0, skew: 14f);
                int col = i % 2, row = i / 2;
                SetRect((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(30 + col * 295, -84 - row * 82), new Vector2(285, 72));
                skillButtons[i] = b;
            }
            var info = CreateText("SkillInfo", panel.transform, "", 22, ColSubText, TextAlignmentOptions.TopLeft);
            SetRect(info.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(-60, 90));
            var back = CreateButton("BackButton", panel.transform, "뒤로", 22, 0);
            SetRect((RectTransform)back.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -18), new Vector2(110, 46));

            // 정보
            var round = CreateText("Round", canvas.transform, "ROUND 1", 34, ColText, TextAlignmentOptions.Left);
            round.fontStyle = FontStyles.Bold;
            SetRect(round.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -28), new Vector2(400, 50));
            var logText = CreateText("Log", canvas.transform, "", 22, ColSubText, TextAlignmentOptions.TopLeft);
            SetRect(logText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -86), new Vector2(520, 220));

            var menuButton = CreateButton("MenuButton", canvas.transform, "메뉴 (Esc)", 24, 0, skew: 14f);
            SetRect((RectTransform)menuButton.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -22), new Vector2(190, 52));
            var autoButton = CreateButton("AutoButton", canvas.transform, "자동 전투: 끔", 22, 0, skew: 14f);
            SetRect((RectTransform)autoButton.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -86), new Vector2(220, 52));

            // 결과
            var result = CreateImage("ResultPanel", canvas.transform, new Color(0, 0, 0, 0.78f), raycast: true);
            Stretch(result.rectTransform);
            var resultText = CreateText("ResultText", result.transform, "승리", 120, ColText, TextAlignmentOptions.Center);
            resultText.fontStyle = FontStyles.Bold;
            SetRect(resultText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(1000, 180));
            var resultButton = CreateButton("ResultButton", result.transform, "계속", 34, 0, skew: 22f);
            SetRect((RectTransform)resultButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(360, 84));

            var flash = CreateImage("Flash", canvas.transform, Color.white);
            Stretch(flash.rectTransform);

            CreatePauseCanvas(menuButton, withBacklog: false);

            var controller = canvas.gameObject.AddComponent<BattleController>();
            SetField(controller, "episodesCsv", episodesCsv);
            SetField(controller, "background", bg);
            SetField(controller, "shakeRoot", shake);
            SetField(controller, "enemyRow", enemyRow);
            SetField(controller, "allyRow", allyRow);
            SetField(controller, "enemyTemplate", enemyTemplate);
            SetField(controller, "allyTemplate", allyTemplate);
            SetField(controller, "flash", flash);
            SetField(controller, "commandPanel", panel.gameObject);
            SetField(controller, "actorText", actor);
            var so = new SerializedObject(controller);
            var arr = so.FindProperty("skillButtons");
            arr.arraySize = 4;
            for (int i = 0; i < 4; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = skillButtons[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            SetField(controller, "skillInfo", info);
            SetField(controller, "backButton", back);
            SetField(controller, "autoButton", autoButton);
            SetField(controller, "roundText", round);
            SetField(controller, "logText", logText);
            SetField(controller, "resultPanel", result.gameObject);
            SetField(controller, "resultText", resultText);
            SetField(controller, "resultButton", resultButton);

            EditorSceneManager.SaveScene(scene, BattleScenePath);
            Debug.Log("[Setup] Battle 씬 생성: " + BattleScenePath);
        }

        /// <summary>전투 유닛 카드. 적은 큰 그림 위주, 아군은 작은 그림과 정보 위주.</summary>
        static BattleUnitView CreateUnitView(string name, Transform parent, Vector2 size, bool enemy)
        {
            // 바탕은 불투명해야 뒤의 Frame이 테두리로만 보인다.
            var root = CreateImage(name, parent, ColPanel, raycast: true);
            root.rectTransform.sizeDelta = size;
            var frame = root.gameObject.AddComponent<Outline>();
            frame.effectDistance = new Vector2(5, -5);
            frame.effectColor = Color.clear;
            var body = CreateRect("Body", root.transform);
            Stretch(body);

            var portrait = CreateImage("Portrait", body, Color.white);
            var portraitLabel = CreateText("PortraitLabel", portrait.transform, "", 26, ColSubText, TextAlignmentOptions.Center);
            Stretch(portraitLabel.rectTransform, 8, 8);
            var nameText = CreateText("Name", body, "이름", enemy ? 30 : 28, ColText, TextAlignmentOptions.Left);
            nameText.fontStyle = FontStyles.Bold;
            var hpBar = CreateImage("HpBar", body, Hex("#2B2F38"));
            var hpFill = CreateImage("HpFill", hpBar.transform, new Color(0.35f, 0.8f, 0.4f));
            hpFill.rectTransform.anchorMin = Vector2.zero;
            hpFill.rectTransform.anchorMax = Vector2.one;
            hpFill.rectTransform.offsetMin = hpFill.rectTransform.offsetMax = Vector2.zero;
            var hpText = CreateText("HpText", body, "0 / 0", 22, ColText, TextAlignmentOptions.Right);
            var infoText = CreateText("Info", body, "", 20, ColSubText, TextAlignmentOptions.TopLeft);
            var popup = CreateText("Popup", body, "0", 56, Color.white, TextAlignmentOptions.Center);
            popup.fontStyle = FontStyles.Bold;
            popup.outlineWidth = 0.25f;
            popup.outlineColor = new Color32(0, 0, 0, 255);

            if (enemy)
            {
                SetRect(portrait.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(-20, -140));
                SetRect(nameText.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 62), new Vector2(-24, 40));
                SetRect(hpBar.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(-24, 16));
                SetRect(hpText.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 62), new Vector2(-24, 40));
                SetRect(infoText.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(-24, 30));
                SetRect(popup.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(300, 90));
            }
            else
            {
                SetRect(portrait.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(150, -20));
                SetRect(nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(172, -14), new Vector2(-184, 40));
                SetRect(hpBar.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(172, -66), new Vector2(-190, 18));
                SetRect(hpText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(172, -88), new Vector2(-190, 34));
                SetRect(infoText.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(172, -130), new Vector2(-190, -140));
                SetRect(popup.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(300, 90));
            }

            var view = root.gameObject.AddComponent<BattleUnitView>();
            SetField(view, "body", body);
            SetField(view, "portrait", portrait);
            SetField(view, "portraitLabel", portraitLabel);
            SetField(view, "nameText", nameText);
            SetField(view, "hpFill", hpFill.rectTransform);
            SetField(view, "hpText", hpText);
            SetField(view, "infoText", infoText);
            SetField(view, "frame", frame);
            SetField(view, "popupTemplate", popup);
            return view;
        }

        // ================================================================ Title 씬

        static void BuildTitleScene(TextAsset episodesCsv)
        {
            var scene = OpenClean(TitleScenePath);
            CreateCameraAndLight();
            CreateEventSystem();

            var canvas = CreateCanvas("TitleCanvas", 0);
            Stretch(CreateImage("Background", canvas.transform, ColTitleBackground).rectTransform);

            // 왼쪽에 기울어진 포인트 띠
            var band = CreateImage("AccentBand", canvas.transform, ColAccent);
            band.gameObject.AddComponent<UISkew>().Skew = 120f;
            SetRect(band.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(60, 0), new Vector2(26, 0));

            // 가제 미정. 세계관 문서명을 임시로 쓴다.
            var logo = CreateText("Logo", canvas.transform, "PROJECT : VITA", 112, ColText, TextAlignmentOptions.Left);
            logo.fontStyle = FontStyles.Bold;
            SetRect(logo.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(170, 230), new Vector2(1100, 150));
            var sub = CreateText("Subtitle", canvas.transform, "가제 · 데모 빌드", 30, ColSubText, TextAlignmentOptions.Left);
            SetRect(sub.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(176, 140), new Vector2(1100, 50));

            var menu = CreateRect("Menu", canvas.transform);
            SetRect(menu, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 1), new Vector2(170, 60), new Vector2(440, 0));
            var menuLayout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            menuLayout.spacing = 16;
            menuLayout.childControlWidth = true;
            menuLayout.childControlHeight = true;
            menuLayout.childForceExpandHeight = false;
            menu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var startButton = CreateButton("StartButton", menu, "처음부터", 36, 84, skew: 22f);
            var continueButton = CreateButton("ContinueButton", menu, "이어하기", 36, 84, skew: 22f);
            var loadButton = CreateButton("LoadButton", menu, "불러오기", 36, 84, skew: 22f);
            var settingsButton = CreateButton("SettingsButton", menu, "설정", 36, 84, skew: 22f);
            var quitButton = CreateButton("QuitButton", menu, "종료", 36, 84, skew: 22f);

            var hint = CreateText("ControlsHint", canvas.transform,
                "클릭 / Space / Enter: 넘기기    숫자 키: 선택지    L, 휠 위로: 대사 기록    A: 오토    Tab: 스킵(읽은 대사)    Ctrl: 빨리감기    Esc: 메뉴\n" +
                "<size=90%>개발용    F1: 개발 정보 숨기기    F5: 지금 노드 다시 시작</size>", 22, ColSubText, TextAlignmentOptions.BottomLeft);
            SetRect(hint.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(170, 28), new Vector2(1180, 70));

            // ---- 에피소드 선택 (개발용)
            var panel = CreateImage("EpisodePanel", canvas.transform, new Color(1, 1, 1, 0.02f));
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

            // ---- 설정
            var overlayCanvas = CreateCanvas("OverlayCanvas", 50);
            var slots = CreateSaveSlotPanel(overlayCanvas);
            var settings = CreateSettingsPanel(overlayCanvas);

            var menuComp = canvas.gameObject.AddComponent<TitleMenu>();
            SetField(menuComp, "episodesCsv", episodesCsv);
            SetField(menuComp, "startButton", startButton);
            SetField(menuComp, "continueButton", continueButton);
            SetField(menuComp, "loadButton", loadButton);
            SetField(menuComp, "settingsButton", settingsButton);
            SetField(menuComp, "quitButton", quitButton);
            SetField(menuComp, "settings", settings);
            SetField(menuComp, "slots", slots);
            SetField(menuComp, "episodePanel", panel.gameObject);
            SetField(menuComp, "episodeListRoot", list);
            SetField(menuComp, "episodeButtonTemplate", episodeTemplate);

            EditorSceneManager.SaveScene(scene, TitleScenePath);
            Debug.Log("[Setup] Title 씬 생성: " + TitleScenePath);
        }

        // ================================================================ 설정 화면 (타이틀과 일시정지 공용)

        /// <summary>SettingsPanel 컴포넌트는 항상 켜진 캔버스에 붙이고, 화면(root)만 켜고 끈다.</summary>
        static SettingsPanel CreateSettingsPanel(Canvas canvas)
        {
            var root = CreateImage("SettingsPanel", canvas.transform, new Color(0, 0, 0, 0.8f), raycast: true);
            Stretch(root.rectTransform);

            var window = CreateImage("Window", root.transform, ColPanel, raycast: true);
            SetRect(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, 720));
            var accent = CreateImage("AccentLine", window.transform, ColAccent);
            SetRect(accent.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 6));

            var title = CreateText("Title", window.transform, "설정", 48, ColText, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            SetRect(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -40), new Vector2(600, 70));

            var (bgmSlider, bgmValue) = CreateSliderRow(window.transform, "BGM 음량", -150);
            var (sfxSlider, sfxValue) = CreateSliderRow(window.transform, "효과음 음량", -235);
            var (speedSlider, speedValue) = CreateSliderRow(window.transform, "텍스트 속도", -320);
            var (autoSlider, autoValue) = CreateSliderRow(window.transform, "오토 속도", -405);

            var windowMode = CreateButton("WindowModeButton", window.transform, "화면: 창 모드", 30, 0);
            SetRect((RectTransform)windowMode.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(60, -505), new Vector2(420, 70));
            var close = CreateButton("CloseButton", window.transform, "닫기", 30, 0, skew: 16f);
            SetRect((RectTransform)close.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-60, 50), new Vector2(220, 70));

            var panel = canvas.gameObject.AddComponent<SettingsPanel>();
            SetField(panel, "root", root.gameObject);
            SetField(panel, "bgmSlider", bgmSlider);
            SetField(panel, "bgmValue", bgmValue);
            SetField(panel, "sfxSlider", sfxSlider);
            SetField(panel, "sfxValue", sfxValue);
            SetField(panel, "textSpeedSlider", speedSlider);
            SetField(panel, "textSpeedValue", speedValue);
            SetField(panel, "autoSpeedSlider", autoSlider);
            SetField(panel, "autoSpeedValue", autoValue);
            SetField(panel, "windowModeButton", windowMode);
            SetField(panel, "windowModeLabel", windowMode.GetComponentInChildren<TMP_Text>());
            SetField(panel, "closeButton", close);
            return panel;
        }

        static (Slider, TMP_Text) CreateSliderRow(Transform parent, string label, float y)
        {
            var labelText = CreateText(label + "Label", parent, label, 30, ColText, TextAlignmentOptions.Left);
            SetRect(labelText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(60, y), new Vector2(260, 60));

            var sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
            sliderGo.name = label + "Slider";
            SetLayerRecursive(sliderGo, LayerMask.NameToLayer("UI"));
            sliderGo.transform.SetParent(parent, false);
            var slider = sliderGo.GetComponent<Slider>();
            SetRect((RectTransform)sliderGo.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(340, y), new Vector2(440, 28));
            foreach (var img in sliderGo.GetComponentsInChildren<Image>(true))
            {
                img.color = img.name switch
                {
                    "Background" => Hex("#2B2F38"),
                    "Fill" => ColAccent,
                    _ => ColText, // Handle
                };
            }

            var value = CreateText(label + "Value", parent, "0", 30, ColText, TextAlignmentOptions.Right);
            SetRect(value.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(800, y), new Vector2(120, 60));
            return (slider, value);
        }

        // ================================================================ Build Settings

        static void ApplyBuildScenes()
        {
            // Title, Story를 맨 앞에 두고, 그 밖에 이미 등록된 씬(에피소드 전용 씬 등)은 뒤에 유지한다.
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

        static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        static RectTransform CreateVerticalList(string name, Transform parent, Vector2 center, float width)
        {
            var list = CreateRect(name, parent);
            SetRect(list, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), center, new Vector2(width, 0));
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return list;
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

        /// <summary>height가 0보다 크면 레이아웃 그룹 안에서 쓸 높이(LayoutElement)를 준다. skew를 주면 평행사변형이 된다.</summary>
        static Button CreateButton(string name, Transform parent, string label, float fontSize, float height, float skew = 0f)
        {
            // 버튼 색 변화는 이미지 색에 곱해진다. 이미지는 흰색으로 두고 상태별 색을 그대로 지정한다.
            var img = CreateImage(name, parent, Color.white, raycast: true);
            if (skew != 0f) img.gameObject.AddComponent<UISkew>().Skew = skew;
            var button = img.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = ColButton;
            colors.highlightedColor = Hex("#5A1E1B");
            colors.pressedColor = ColAccent;
            colors.selectedColor = ColButton; // 클릭 뒤에 강조가 남지 않게
            colors.disabledColor = new Color(0.13f, 0.14f, 0.16f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            if (height > 0f) img.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

            var text = CreateText("Label", img.transform, label, fontSize, ColText, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
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

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
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
