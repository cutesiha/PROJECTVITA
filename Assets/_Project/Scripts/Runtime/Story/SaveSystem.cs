using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ProjectVita.Story
{
    /// <summary>
    /// 최소 세이브 (문서 4-4): 에피소드, 노드, Yarn 변수, 그리고 무대 상태(배경, CG, 스탠딩, BGM).
    /// 노드에 들어갈 때마다 자동 저장 슬롯 하나에 덮어쓴다. 이어하기는 그 노드의 처음부터 다시 시작한다.
    /// 슬롯 UI는 7단계(편의 기능)에서 만든다.
    /// 저장 위치: Application.persistentDataPath/autosave.json (Player Settings의 Company/Product 이름에 따라 바뀐다)
    /// </summary>
    public static class SaveSystem
    {
        const int Version = 1;

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
            /// <summary>Story(기본) 또는 Exploration</summary>
            public string scene;
            /// <summary>탐색 중이면 장소 id</summary>
            public string location;
            public string node;
            public string bgm;
            public StoryStage.StageState stage;
            public List<FloatVar> floats = new List<FloatVar>();
            public List<StringVar> strings = new List<StringVar>();
            public List<BoolVar> bools = new List<BoolVar>();
        }

        static string AutoSavePath => Path.Combine(Application.persistentDataPath, "autosave.json");

        public static bool HasAutoSave => File.Exists(AutoSavePath);

        public static void AutoSave(string episodeId, string node, StoryStage.StageState stage, string bgm,
            string scene = EpisodeFlow.StorySceneName, string location = "")
        {
            var data = new SaveData
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                episodeId = episodeId,
                scene = scene,
                location = location ?? string.Empty,
                node = node ?? string.Empty,
                bgm = bgm ?? string.Empty,
                stage = stage,
            };

            var (floats, strings, bools) = GameVariables.Storage.GetAllVariables();
            foreach (var kv in floats) data.floats.Add(new FloatVar { name = kv.Key, value = kv.Value });
            foreach (var kv in strings) data.strings.Add(new StringVar { name = kv.Key, value = kv.Value });
            foreach (var kv in bools) data.bools.Add(new BoolVar { name = kv.Key, value = kv.Value });

            try
            {
                File.WriteAllText(AutoSavePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogError("[Save] 저장 실패: " + e.Message);
            }
        }

        public static SaveData LoadAutoSave()
        {
            if (!HasAutoSave) return null;
            try
            {
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(AutoSavePath));
                if (data == null || string.IsNullOrEmpty(data.episodeId) || (string.IsNullOrEmpty(data.node) && string.IsNullOrEmpty(data.location)))
                {
                    Debug.LogWarning("[Save] 자동 저장 파일이 비어 있거나 깨졌다.");
                    return null;
                }
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError("[Save] 불러오기 실패: " + e.Message);
                return null;
            }
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

        public static void DeleteAutoSave()
        {
            if (HasAutoSave) File.Delete(AutoSavePath);
        }
    }
}
