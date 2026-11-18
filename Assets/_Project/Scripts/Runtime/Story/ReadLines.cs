using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Yarn.Unity;

namespace ProjectVita.Story
{
    /// <summary>
    /// 읽은 대사 기록. 스킵(읽은 대사만)이 쓴다. 세이브 슬롯과 상관없이 게임 전체에 하나다.
    /// 키는 Yarn 줄 id + 본문 해시다. 작가가 대사를 고치면 그 줄은 다시 "안 읽음"이 된다.
    /// 저장 위치: Application.persistentDataPath/read_lines.json (노드에 들어갈 때와 게임을 끌 때 저장)
    /// </summary>
    public static class ReadLines
    {
        [Serializable]
        sealed class FileData
        {
            public List<string> keys = new List<string>();
        }

        static HashSet<string> keys;
        static bool dirty;

        static string FilePath => Path.Combine(Application.persistentDataPath, "read_lines.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            keys = null;
            dirty = false;
            Application.quitting -= Flush;
            Application.quitting += Flush;
        }

        public static string KeyOf(LocalizedLine line) => line.TextID + "#" + Hash(line.RawText ?? line.Text.Text ?? string.Empty);

        public static bool IsRead(string key)
        {
            Load();
            return keys.Contains(key);
        }

        public static void Mark(string key)
        {
            Load();
            if (keys.Add(key)) dirty = true;
        }

        /// <summary>바뀐 것이 있으면 파일에 쓴다.</summary>
        public static void Flush()
        {
            if (!dirty || keys == null) return;
            try
            {
                var data = new FileData { keys = new List<string>(keys) };
                File.WriteAllText(FilePath, JsonUtility.ToJson(data));
                dirty = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[읽은 대사] 저장 실패: " + e.Message);
            }
        }

        /// <summary>읽은 기록을 모두 지운다 (개발용).</summary>
        public static void ClearAll()
        {
            keys = new HashSet<string>();
            dirty = false;
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }

        static void Load()
        {
            if (keys != null) return;
            keys = new HashSet<string>();
            if (!File.Exists(FilePath)) return;
            try
            {
                var data = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath));
                if (data?.keys != null) keys.UnionWith(data.keys);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[읽은 대사] 불러오기 실패: " + e.Message);
            }
        }

        /// <summary>FNV-1a 32비트. string.GetHashCode는 실행마다 달라질 수 있어서 직접 계산한다.</summary>
        static string Hash(string text)
        {
            uint h = 2166136261;
            foreach (char c in text)
            {
                h ^= c;
                h *= 16777619;
            }
            return h.ToString("x8");
        }
    }
}
