using System;
using System.Collections.Generic;
using ProjectVita.Core;
using ProjectVita.Story;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;

namespace ProjectVita.Exploration
{
    /// <summary>
    /// 포인트앤클릭 탐색 (문서 4-5). 지도 화면 → 장소 화면 → 오브젝트 클릭 → Yarn 노드.
    /// 대사가 끝나면 탐색으로 돌아온다. 대사 UI와 무대(StoryStage)는 Story 씬과 같은 것을 쓴다.
    /// - 장소: 처음 들어갈 때 enter_node를 한 번 실행하고 $장소id_visited를 켠다.
    /// - 오브젝트: 대사가 끝나면 $오브젝트id_seen을 켠다. once면 그 뒤로 사라진다. encounter_id가 있으면 전투로 간다.
    /// - 이 씬에서 나가려면 대사에서 &lt;&lt;episode&gt;&gt;, &lt;&lt;explore 다른장소&gt;&gt;, &lt;&lt;battle&gt;&gt;를 쓴다.
    /// </summary>
    public sealed class ExplorationController : MonoBehaviour
    {
        public static ExplorationController Current { get; private set; }

        [SerializeField] DialogueRunner runner;
        [SerializeField] StoryStage stage;
        [SerializeField] TextAsset episodesCsv;

        [Header("장소 화면")]
        [SerializeField] RectTransform hotspotRoot;
        [SerializeField] ExplorationHotspot hotspotTemplate;
        [SerializeField] GameObject locationPlate;
        [SerializeField] TMP_Text locationName;
        [SerializeField] Button mapButton;

        [Header("지도 화면")]
        [SerializeField] GameObject mapView;
        [SerializeField] RectTransform mapListRoot;
        [SerializeField] Button mapItemTemplate;

        [Tooltip("이 씬을 바로 플레이했을 때 들어갈 장소. 비우면 지도부터.")]
        [SerializeField] string fallbackLocationId = "";
        [Tooltip("이 씬을 바로 플레이했을 때의 에피소드. 비우면 Episodes.csv의 첫 에피소드.")]
        [SerializeField] string fallbackEpisodeId = "";

        public string LocationId => location?.id;

        ExplorationData data;
        ExplorationData.LocationDef location;
        Episode episode;
        bool busy;
        readonly List<ExplorationHotspot> hotspots = new List<ExplorationHotspot>();
        readonly List<Button> mapItems = new List<Button>();

        void Awake()
        {
            Current = this;
            hotspotTemplate.gameObject.SetActive(false);
            mapItemTemplate.gameObject.SetActive(false);
            mapButton.onClick.AddListener(ShowMap);
            mapView.SetActive(false);
            locationPlate.SetActive(false);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        async void Start()
        {
            data = ExplorationData.Instance;
            var table = EpisodeFlow.EnsureTable(episodesCsv);
            episode = EpisodeFlow.Current;
            if (episode == null)
            {
                episode = table.Find(fallbackEpisodeId) ?? table.First;
                EpisodeFlow.SetCurrentWithoutLoading(episode);
            }
            DevOverlay.SetEpisode(episode);

            if (runner.YarnProject == null || runner.YarnProject.Program == null)
            {
                Fail("Yarn 스크립트에 오류가 있거나 Yarn Project가 연결되지 않았다.");
                return;
            }
            var storage = GameVariables.Storage;
            storage.Program = runner.YarnProject.Program;
            runner.VariableStorage = storage;

            var resume = EpisodeFlow.TakePendingResume(episode?.Id);
            string locationId = EpisodeFlow.TakePendingLocation();
            string pendingNode = EpisodeFlow.TakePendingNode();
            if (resume != null) AudioManager.Instance.PlayBgm(resume.bgm, 0.5f);

            // 이 씬이 에피소드의 씬(unity_scene = Exploration)으로 시작했으면 에피소드 시작 노드를 먼저 실행한다.
            bool startedAsEpisode = resume == null && string.IsNullOrEmpty(locationId) && string.IsNullOrEmpty(pendingNode)
                                    && episode != null && episode.SceneName == EpisodeFlow.ExplorationSceneName;
            if (startedAsEpisode && HasNode(episode.StartNode)) pendingNode = episode.StartNode;
            if (string.IsNullOrEmpty(locationId) && resume == null && !startedAsEpisode) locationId = fallbackLocationId;

            if (!string.IsNullOrEmpty(pendingNode))
            {
                if (!await RunNode(pendingNode)) return;
            }

            if (!string.IsNullOrEmpty(locationId)) await EnterLocation(locationId);
            else ShowMap();
        }

        // ================================================================ 지도

        void ShowMap()
        {
            if (busy) return;
            location = null;
            ClearHotspots();
            locationPlate.SetActive(false);
            mapButton.gameObject.SetActive(false);

            foreach (var b in mapItems)
                if (b != null) Destroy(b.gameObject);
            mapItems.Clear();

            foreach (var loc in data.locations)
            {
                if (!Check(loc.unlockIf, $"장소 {loc.id} unlock_if")) continue;
                var item = Instantiate(mapItemTemplate, mapListRoot);
                item.gameObject.SetActive(true);
                item.name = "Map_" + loc.id;
                bool visited = IsTrue(ExplorationData.VisitedVariable(loc.id));
                item.GetComponentInChildren<TMP_Text>(true).text = visited ? loc.displayName : $"{loc.displayName}  <size=70%><color=#B3261E>NEW</color></size>";
                string id = loc.id;
                item.onClick.AddListener(() => Tween.Forget(EnterLocation(id)));
                mapItems.Add(item);
            }
            mapView.SetActive(true);
            DevOverlay.SetNode("(지도)");
            Save(); // 지도에서도 저장할 수 있게 (불러오면 지도부터)
        }

        // ================================================================ 장소

        async Awaitable EnterLocation(string id)
        {
            if (busy) return;
            var loc = data.FindLocation(id);
            if (loc == null)
            {
                Fail($"장소 '{id}'가 Exploration_Location.csv에 없다.");
                ShowMap();
                return;
            }

            location = loc;
            mapView.SetActive(false);
            locationPlate.SetActive(true);
            mapButton.gameObject.SetActive(true);
            locationName.text = loc.displayName;
            DevOverlay.SetNode($"(장소 {loc.id})");
            if (!string.IsNullOrEmpty(loc.background)) await stage.SetBackground(loc.background, "fade", 0.4f);
            if (this == null) return;

            BuildHotspots();
            Save();

            string visitedVar = ExplorationData.VisitedVariable(loc.id);
            if (!IsTrue(visitedVar))
            {
                GameVariables.Storage.SetValue(visitedVar, true);
                if (!string.IsNullOrEmpty(loc.enterNode) && !await RunNode(loc.enterNode)) return;
            }
        }

        void BuildHotspots()
        {
            ClearHotspots();
            foreach (var obj in data.ObjectsIn(location.id))
            {
                var h = Instantiate(hotspotTemplate, hotspotRoot);
                h.Setup(obj, OnHotspotClicked);
                hotspots.Add(h);
            }
            RefreshHotspots();
        }

        void RefreshHotspots()
        {
            foreach (var h in hotspots)
            {
                var def = h.Def;
                bool visible = Check(def.showIf, $"오브젝트 {def.id} show_if") && !(def.once && IsTrue(ExplorationData.SeenVariable(def.id)));
                h.gameObject.SetActive(visible && !busy);
            }
        }

        void ClearHotspots()
        {
            foreach (var h in hotspots)
                if (h != null) Destroy(h.gameObject);
            hotspots.Clear();
        }

        async void OnHotspotClicked(ExplorationHotspot hotspot)
        {
            if (busy || GamePause.IsPaused || EpisodeFlow.IsLoading) return;
            var def = hotspot.Def;

            if (!string.IsNullOrEmpty(def.node) && !await RunNode(def.node)) return;
            GameVariables.Storage.SetValue(ExplorationData.SeenVariable(def.id), true);

            if (!string.IsNullOrEmpty(def.encounterId))
            {
                EpisodeFlow.StartBattle(def.encounterId, location?.id);
                return;
            }
            RefreshHotspots();
            Save();
        }

        // ================================================================ 대사

        /// <summary>노드를 실행하고 탐색으로 돌아온다. 대사 중에 씬이 바뀌면 false.</summary>
        async Awaitable<bool> RunNode(string node)
        {
            if (!HasNode(node))
            {
                Fail($"Yarn 노드 '{node}'가 없다.");
                return true;
            }

            busy = true;
            hotspotRoot.gameObject.SetActive(false);
            mapButton.interactable = false;
            try
            {
                await runner.RunDialogue(node);
            }
            catch (OperationCanceledException)
            {
            }
            if (this == null || EpisodeFlow.IsLoading) return false;

            busy = false;
            mapButton.interactable = true;
            hotspotRoot.gameObject.SetActive(true);

            // 대사에서 바꾼 무대를 장소 화면으로 되돌린다.
            stage.Hide("all", 0.25f);
            Tween.Forget(stage.SetCG("off", "fade", 0.25f));
            Tween.Forget(stage.Fade("in", 0.25f, ""));
            if (location != null && !string.IsNullOrEmpty(location.background) && stage.GetState().background != location.background)
                Tween.Forget(stage.SetBackground(location.background, "fade", 0.3f));
            if (location != null) RefreshHotspots();
            return true;
        }

        // ================================================================ 공통

        /// <summary>자동 저장. 장소에 있으면 그 장소, 지도면 장소 없이 (불러오면 지도부터).</summary>
        void Save()
        {
            if (episode == null) return;
            SaveSystem.AutoSave(episode.Id, "", null, AudioManager.Instance.CurrentBgm, EpisodeFlow.ExplorationSceneName,
                location?.id ?? "", location?.displayName ?? "지도");
        }

        bool HasNode(string node) =>
            !string.IsNullOrEmpty(node) && runner.YarnProject != null && Array.IndexOf(runner.YarnProject.NodeNames, node) >= 0;

        static bool IsTrue(string variable) =>
            GameVariables.Storage.TryGetValue<bool>(variable, out var v) && v;

        static bool Check(string condition, string where)
        {
            bool ok = YarnCondition.Evaluate(condition, out var error);
            if (error != null) Debug.LogWarning($"[탐색] {where}: {error}");
            return ok;
        }

        void Fail(string message)
        {
            Debug.LogError("[탐색] " + message, this);
            DevOverlay.SetStatus(message);
        }
    }
}
