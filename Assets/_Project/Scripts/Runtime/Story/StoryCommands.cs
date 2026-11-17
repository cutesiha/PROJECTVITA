using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
using Yarn.Unity;

namespace ProjectVita.Story
{
    /// <summary>
    /// Yarn 명령 (&lt;&lt;bg ...&gt;&gt; 등).
    /// 4-2단계: 문법을 먼저 굳히려고 로그만 찍는 빈 구현으로 등록한다. 실제 동작은 4-4단계에서 채운다.
    /// [YarnCommand]가 붙은 정적 메서드는 Yarn Spinner가 컴파일 시점에 자동 등록한다.
    /// &lt;&lt;wait 초&gt;&gt;와 &lt;&lt;stop&gt;&gt;은 Yarn 내장 명령이라 여기서 등록하지 않는다.
    /// </summary>
    public static class StoryCommands
    {
        // ------------------------------------------------------------ 연출 (TODO(4-4): 실제 구현)

        // 인자 형식은 4-4에서 확정한다. 지금은 몇 개가 오든 받아서 로그로 남긴다.
        [YarnCommand("bg")] public static void Bg(params string[] args) => Log("bg", args);
        [YarnCommand("show")] public static void Show(params string[] args) => Log("show", args);
        [YarnCommand("hide")] public static void Hide(params string[] args) => Log("hide", args);
        [YarnCommand("move")] public static void Move(params string[] args) => Log("move", args);
        [YarnCommand("face")] public static void Face(params string[] args) => Log("face", args);
        [YarnCommand("cg")] public static void Cg(params string[] args) => Log("cg", args);
        [YarnCommand("fade")] public static void Fade(params string[] args) => Log("fade", args);
        [YarnCommand("shake")] public static void Shake(params string[] args) => Log("shake", args);
        [YarnCommand("bgm")] public static void Bgm(params string[] args) => Log("bgm", args);
        [YarnCommand("sfx")] public static void Sfx(params string[] args) => Log("sfx", args);

        // ------------------------------------------------------------ 모드 전환 (TODO(4-5, 4-6))

        [YarnCommand("explore")] public static void Explore(params string[] args) => Log("explore", args);
        [YarnCommand("battle")] public static void Battle(params string[] args) => Log("battle", args);

        // ------------------------------------------------------------ 에피소드 이동

        /// <summary>
        /// &lt;&lt;episode Ch1_01&gt;&gt;: Episodes.csv의 에피소드로 바로 이동한다.
        /// 씬이 다시 로드되면서 DialogueRunner가 파괴되고, 그때 token이 취소된다.
        /// 그 사이 다음 대사가 한 프레임이라도 보이지 않도록 취소될 때까지 기다린다.
        /// </summary>
        [YarnCommand("episode")]
        public static async YarnTask Episode(string episodeId, CancellationToken token)
        {
            Debug.Log($"[명령] <<episode {episodeId}>>");
            if (!EpisodeFlow.TryGo(episodeId))
            {
                DevOverlay.SetStatus($"<<episode {episodeId}>> 실패. Console 확인");
                return; // 이동에 실패하면 대사를 계속 진행한다.
            }
            await YarnTask.WaitUntilCanceled(token);
        }

        // ------------------------------------------------------------ 공통

        /// <summary>등록된 명령인지 (Yarn 내장 명령 포함). 오타 경고에 쓴다.</summary>
        public static bool IsKnown(string commandName)
        {
            if (known == null)
            {
                known = new HashSet<string> { "wait", "stop" };
                foreach (var m in typeof(StoryCommands).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var attr = m.GetCustomAttribute<YarnCommandAttribute>();
                    if (attr != null) known.Add(string.IsNullOrEmpty(attr.Name) ? m.Name : attr.Name);
                }
            }
            return known.Contains(commandName);
        }
        static HashSet<string> known;

        static void Log(string name, string[] args)
        {
            Debug.Log($"[명령] <<{name}{(args.Length > 0 ? " " + string.Join(" ", args) : "")}>>  (4-2: 로그만 찍는 빈 구현)");
        }
    }
}
