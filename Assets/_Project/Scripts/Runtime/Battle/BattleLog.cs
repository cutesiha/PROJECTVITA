using System.Collections.Generic;
using ProjectVita.Story;
using UnityEngine;

namespace ProjectVita.Battle
{
    /// <summary>
    /// 전투 기록 (LOG 버튼, 일시정지의 "전투 기록"). 대사 기록과 같은 창(BacklogPanel)이 그린다.
    /// 전투를 시작할 때마다 비운다.
    /// </summary>
    public static class BattleLog
    {
        public const int MaxEntries = 300;

        static readonly List<Backlog.Entry> entries = new List<Backlog.Entry>();
        public static IReadOnlyList<Backlog.Entry> Entries => entries;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => entries.Clear();

        /// <summary>speaker가 비면 시스템 줄 (화자 줄 없이 흐린 색).</summary>
        public static void Add(string speaker, string text, Color color, string tag = "")
        {
            entries.Add(new Backlog.Entry { speaker = speaker ?? "", text = text ?? "", nameColor = color, tag = tag ?? "" });
            if (entries.Count > MaxEntries) entries.RemoveRange(0, entries.Count - MaxEntries);
        }

        public static void Clear() => entries.Clear();
    }
}
