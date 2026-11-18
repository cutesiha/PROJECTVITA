using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ProjectVita.Story
{
    /// <summary>
    /// 대사 기록(백로그)의 내용. 화면은 BacklogPanel이 그린다.
    /// 씬이 바뀌어도 유지되고, 새 게임이나 불러오기를 하면 비운다. 파일로 저장하지 않는다.
    /// </summary>
    public static class Backlog
    {
        public const int MaxEntries = 300;

        public sealed class Entry
        {
            /// <summary>비어 있으면 나레이션</summary>
            public string speaker;
            public string text;
            public Color nameColor;
            /// <summary>화자의 식별 칩 (Characters.csv name_tag). 없으면 빈 문자열</summary>
            public string tag;
            /// <summary>고른 선택지</summary>
            public bool choice;
        }

        static readonly List<Entry> entries = new List<Entry>();
        public static IReadOnlyList<Entry> Entries => entries;

        /// <summary>새 게임(불러오기) 뒤로 나온 대사 수. 대사창의 LOG.001 번호다.</summary>
        public static int LineCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            entries.Clear();
            LineCount = 0;
        }

        public static void AddLine(string speaker, string text, Color nameColor, string tag = "")
        {
            LineCount++;
            Add(new Entry { speaker = speaker ?? string.Empty, text = text ?? string.Empty, nameColor = nameColor, tag = tag ?? string.Empty });
        }

        public static void AddChoice(string text) =>
            Add(new Entry { speaker = string.Empty, text = text ?? string.Empty, choice = true });

        public static void Clear()
        {
            entries.Clear();
            LineCount = 0;
        }

        /// <summary>세이브 슬롯에 보여 줄 마지막 대사 한 줄 ("화자: 대사", 태그 없이, 길면 자름).</summary>
        public static string Preview(int maxLength = 46)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (e.choice) continue;
                string text = Regex.Replace(e.text, "<[^>]*>", string.Empty).Replace('\n', ' ').Trim();
                if (!string.IsNullOrEmpty(e.speaker)) text = e.speaker + ": " + text;
                return text.Length > maxLength ? text.Substring(0, maxLength) + "…" : text;
            }
            return string.Empty;
        }

        static void Add(Entry e)
        {
            entries.Add(e);
            if (entries.Count > MaxEntries) entries.RemoveRange(0, entries.Count - MaxEntries);
        }
    }
}
