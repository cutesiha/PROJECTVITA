using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectVita.Story
{
    /// <summary>
    /// 세이브: 에피소드, 노드(또는 탐색 장소), Yarn 변수, 무대 상태(배경, CG, 스탠딩), BGM.
    /// - 자동 저장 (슬롯 0, autosave.json): 노드에 들어갈 때마다, 탐색에서는 장소에 들어갈 때와 조사 후에 덮어쓴다.
    /// - 수동 슬롯 1~7 (save_N.json + 썸네일 save_N.png): 일시정지 메뉴의 저장. 마지막 자동 저장 시점(LastSnapshot)을 복사한다.
    /// 불러오면 저장한 노드의 처음부터 다시 시작한다 (노드 중간의 위치는 저장하지 않는다).
    /// 저장 위치: Application.persistentDataPath (Player Settings의 Company/Product 이름에 따라 바뀐다)
    /// </summary>
    public static class SaveSystem
    {
        const int Version = 1;

        /// <summary>수동 슬롯 수. 슬롯 번호 0은 자동 저장이다.</summary>
        public const int SlotCount = 7;
        public const int AutoSlot = 0;

        [Serializable]
        public sealed class FloatVar { public string name; public float value; }
        [Serializable]
        public sealed class StringVar { public string name; public string value; }
        [Serializable]
        public sealed class BoolVar { public string name; public bool value; }

        [Serializable]
        public sealed class SaveData
        {
            public int version = Version;
            public string savedAt;
            public string episodeId;
            /// <summary>슬롯 화면에 보여 줄 에피소드 이름</summary>
            public string episodeName;
            /// <summary>Story(기본) 또는 Exploration</summary>
            public string scene;
            /// <summary>탐색 중이면 장소 id</summary>
            public string location;
            /// <summary>슬롯 화면에 보여 줄 장소 이름</summary>
            public string locationName;
            public string node;
            public string bgm;
            /// <summary>슬롯 화면에 보여 줄 마지막 대사</summary>
            public string preview;
            public StoryStage.StageState stage;
            public List<FloatVar> floats = new List<FloatVar>();
            public List<StringVar> strings = new List<StringVar>();
            public List<BoolVar> bools = new List<BoolVar>();
        }

        /// <summary>
        /// 마지막 자동 저장 내용 (메모리). 수동 저장과 핫리로드(현재 노드 다시 시작)가 쓴다.
        /// 새 게임에서 비우고, 불러오면 불러온 내용이 된다.
        /// </summary>
        public static SaveData LastSnapshot { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LastSnapshot = null;

        static string Dir => Application.persistentDataPath;
        static string SlotPath(int slot) => Path.Combine(Dir, slot == AutoSlot ? "autosave.json" : $"save_{slot}.json");
        static string ThumbnailPath(int slot) => Path.Combine(Dir, $"save_{slot}.png");

        public static bool HasAutoSave => File.Exists(SlotPath(AutoSlot));

        public static bool Exists(int slot) => File.Exists(SlotPath(slot));

        public static bool HasAnySave
        {
            get
            {
                for (int i = 0; i <= SlotCount; i++)
                    if (Exists(i)) return true;
                return false;
            }
        }

        /// <summary>지금 수동 저장을 할 수 있는지. 전투 중과 씬을 바꾸는 중에는 안 된다.</summary>
        public static bool CanSaveNow =>
            LastSnapshot != null && !EpisodeFlow.IsLoading && SceneManager.GetActiveScene().name != EpisodeFlow.BattleSceneName;

        // ------------------------------------------------------------ 자동 저장

        public static void AutoSave(string episodeId, string node, StoryStage.StageState stage, string bgm,
            string scene = EpisodeFlow.StorySceneName, string location = "", string locationName = "")
        {
            var data = new SaveData
            {
                savedAt = Now(),
                episodeId = episodeId,
                episodeName = EpisodeFlow.Current?.DisplayName ?? episodeId,
                scene = scene,
                location = location ?? string.Empty,
                locationName = locationName ?? string.Empty,
                node = node ?? string.Empty,
                bgm = bgm ?? string.Empty,
                stage = stage,
            };

            var (floats, strings, bools) = GameVariables.Storage.GetAllVariables();
            foreach (var kv in floats) data.floats.Add(new FloatVar { name = kv.Key, value = kv.Value });
            foreach (var kv in strings) data.strings.Add(new StringVar { name = kv.Key, value = kv.Value });
            foreach (var kv in bools) data.bools.Add(new BoolVar { name = kv.Key, value = kv.Value });

            LastSnapshot = data;
            Write(AutoSlot, data);
            ReadLines.Flush();
        }

        public static SaveData LoadAutoSave() => Load(AutoSlot);

        // ------------------------------------------------------------ 슬롯

        /// <summary>마지막 자동 저장 시점을 슬롯에 복사한다. 썸네일이 있으면 같이 저장한다.</summary>
        public static bool SaveToSlot(int slot, Texture2D thumbnail)
        {
            if (slot < 1 || slot > SlotCount || !CanSaveNow) return false;

            // 메모리의 스냅숏은 계속 쓰이니 복사해서 고친다.
            var data = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(LastSnapshot));
            data.savedAt = Now();
            data.preview = Backlog.Preview();
            if (!Write(slot, data)) return false;

            try
            {
                if (thumbnail != null) File.WriteAllBytes(ThumbnailPath(slot), thumbnail.EncodeToPNG());
                else if (File.Exists(ThumbnailPath(slot))) File.Delete(ThumbnailPath(slot));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] 썸네일 저장 실패: " + e.Message);
            }
            Debug.Log($"[Save] 슬롯 {slot} 저장: {data.episodeId} / {(string.IsNullOrEmpty(data.node) ? data.locationName : data.node)}");
            return true;
        }

        /// <summary>슬롯을 읽는다 (0 = 자동 저장). 없거나 깨졌으면 null.</summary>
        public static SaveData Load(int slot)
        {
            string path = SlotPath(slot);
            if (!File.Exists(path)) return null;
            try
            {
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                // 이야기는 노드가, 탐색은 장소가 있어야 한다 (탐색 지도에서 저장했으면 둘 다 비어 있다).
                bool hasPlace = !string.IsNullOrEmpty(data?.node) || !string.IsNullOrEmpty(data?.location) || data?.scene == EpisodeFlow.ExplorationSceneName;
                if (data == null || string.IsNullOrEmpty(data.episodeId) || !hasPlace)
                {
                    Debug.LogWarning($"[Save] {Path.GetFileName(path)}이 비어 있거나 깨졌다.");
                    return null;
                }
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] {Path.GetFileName(path)} 불러오기 실패: " + e.Message);
                return null;
            }
        }

        /// <summary>슬롯 썸네일. 없으면 null. 받은 쪽에서 다 쓰면 Destroy한다.</summary>
        public static Texture2D LoadThumbnail(int slot)
        {
            string path = ThumbnailPath(slot);
            if (slot == AutoSlot || !File.Exists(path)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(File.ReadAllBytes(path))) return tex;
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] 썸네일 읽기 실패: " + e.Message);
            }
            return null;
        }

        /// <summary>저장된 변수로 GameVariables를 덮어쓴다.</summary>
        public static void RestoreVariables(SaveData data)
        {
            var floats = new Dictionary<string, float>();
            var strings = new Dictionary<string, string>();
            var bools = new Dictionary<string, bool>();
            foreach (var v in data.floats) floats[v.name] = v.value;
            foreach (var v in data.strings) strings[v.name] = v.value;
            foreach (var v in data.bools) bools[v.name] = v.value;
            GameVariables.Storage.SetAllVariables(floats, strings, bools, true);
        }

        /// <summary>불러온 세이브를 현재 스냅숏으로 둔다 (불러온 직후에도 저장할 수 있게).</summary>
        public static void SetSnapshot(SaveData data) => LastSnapshot = data;

        public static void ClearSnapshot() => LastSnapshot = null;

        public static void DeleteAutoSave()
        {
            if (HasAutoSave) File.Delete(SlotPath(AutoSlot));
        }

        // ------------------------------------------------------------ 공통

        static bool Write(int slot, SaveData data)
        {
            try
            {
                File.WriteAllText(SlotPath(slot), JsonUtility.ToJson(data, true));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] 슬롯 {slot} 저장 실패: " + e.Message);
                return false;
            }
        }

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }
}
