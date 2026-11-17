using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using ProjectVita.Core;
using UnityEngine;
using Yarn.Unity;

namespace ProjectVita.Story
{
    /// <summary>
    /// Yarn 명령. [YarnCommand]가 붙은 정적 메서드는 Yarn Spinner가 컴파일 시점에 자동 등록한다.
    /// 인자 뒤의 기본값은 생략할 수 있다는 뜻이다. 예: &lt;&lt;bg ruins_night&gt;&gt; = &lt;&lt;bg ruins_night fade 0.6&gt;&gt;
    ///
    /// 기다리는 명령 (끝나야 다음 줄로 간다): bg, cg, fade
    /// 기다리지 않는 명령: show, hide, move, face, shake, flash, vignette, bgm, sfx
    /// 기다리게 하려면 뒤에 &lt;&lt;wait 초&gt;&gt;를 쓴다. wait와 stop은 Yarn 내장 명령이다.
    /// </summary>
    public static class StoryCommands
    {
        // ------------------------------------------------------------ 배경, CG

        /// <summary>&lt;&lt;bg 이름 [cut|fade|wipe] [시간]&gt;&gt;. none이면 배경을 지운다.</summary>
        [YarnCommand("bg")]
        public static async YarnTask Bg(string name, string transition = "fade", float time = Auto)
        {
            var stage = Stage("bg");
            if (stage != null) await stage.SetBackground(name, transition, Or(time, 0.6f));
        }

        /// <summary>&lt;&lt;cg 이름 [cut|fade] [시간]&gt;&gt;, &lt;&lt;cg off&gt;&gt;</summary>
        [YarnCommand("cg")]
        public static async YarnTask Cg(string name, string transition = "fade", float time = Auto)
        {
            var stage = Stage("cg");
            if (stage != null) await stage.SetCG(name, transition, Or(time, 0.6f));
        }

        // ------------------------------------------------------------ 스탠딩

        /// <summary>&lt;&lt;show 캐릭터 [표정] [위치] [시간]&gt;&gt;. 위치: left center right farleft farright</summary>
        [YarnCommand("show")]
        public static void Show(string character, string expression = "", string position = "", float time = Auto)
            => Stage("show")?.Show(character, expression, position, Or(time, 0.3f));

        /// <summary>&lt;&lt;hide 캐릭터 [시간]&gt;&gt;, &lt;&lt;hide all&gt;&gt;</summary>
        [YarnCommand("hide")]
        public static void Hide(string character, float time = Auto)
            => Stage("hide")?.Hide(character, Or(time, 0.3f));

        /// <summary>&lt;&lt;move 캐릭터 위치 [시간]&gt;&gt;</summary>
        [YarnCommand("move")]
        public static void Move(string character, string position, float time = Auto)
            => Stage("move")?.Move(character, position, Or(time, 0.4f));

        /// <summary>&lt;&lt;face 캐릭터 표정&gt;&gt;. 대사 끝에 #face:표정을 붙여도 된다 (그 줄의 화자에게 적용).</summary>
        [YarnCommand("face")]
        public static void Face(string character, string expression)
            => Stage("face")?.Face(character, expression);

        // ------------------------------------------------------------ 화면 효과

        /// <summary>&lt;&lt;fade out [시간] [black|white|#색]&gt;&gt;, &lt;&lt;fade in [시간]&gt;&gt;</summary>
        [YarnCommand("fade")]
        public static async YarnTask Fade(string direction, float time = Auto, string color = "")
        {
            var stage = Stage("fade");
            if (stage != null) await stage.Fade(direction, Or(time, 0.5f), color);
        }

        /// <summary>&lt;&lt;shake [세기] [시간]&gt;&gt;. 세기 1 = 약 30px</summary>
        [YarnCommand("shake")]
        public static void Shake(float strength = 1, float time = Auto)
            => Stage("shake")?.Shake(strength, Or(time, 0.4f));

        /// <summary>&lt;&lt;flash [white|red|#색] [시간]&gt;&gt;</summary>
        [YarnCommand("flash")]
        public static void Flash(string color = "white", float time = Auto)
            => Stage("flash")?.Flash(color, Or(time, 0.3f));

        /// <summary>&lt;&lt;vignette on|off|0~1&gt;&gt;</summary>
        [YarnCommand("vignette")]
        public static void Vignette(string value = "on")
            => Stage("vignette")?.Vignette(value);

        // ------------------------------------------------------------ 오디오

        /// <summary>&lt;&lt;bgm 이름 [페이드 시간]&gt;&gt;, &lt;&lt;bgm stop [페이드 시간]&gt;&gt;</summary>
        [YarnCommand("bgm")]
        public static void Bgm(string name, float fade = 1) => AudioManager.Instance.PlayBgm(name, fade);

        /// <summary>&lt;&lt;sfx 이름 [볼륨 0~1]&gt;&gt;</summary>
        [YarnCommand("sfx")]
        public static void Sfx(string name, float volume = 1) => AudioManager.Instance.PlaySfx(name, volume);

        // ------------------------------------------------------------ 모드 전환 (TODO(4-5, 4-6))

        [YarnCommand("explore")] public static void Explore(params string[] args) => LogOnly("explore", args);
        [YarnCommand("battle")] public static void Battle(params string[] args) => LogOnly("battle", args);

        // ------------------------------------------------------------ 에피소드 이동

        /// <summary>
        /// &lt;&lt;episode Ch1_01&gt;&gt;: Episodes.csv의 에피소드로 바로 이동한다.
        /// 씬이 다시 로드되면서 DialogueRunner가 파괴되고, 그때 token이 취소된다.
        /// 그 사이 다음 대사가 한 프레임이라도 보이지 않도록 취소될 때까지 기다린다.
        /// </summary>
        [YarnCommand("episode")]
        public static async YarnTask Episode(string episodeId, CancellationToken token)
        {
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

        // Yarn Spinner 3.3.2 소스 제너레이터는 float 기본값을 0.6처럼 f 없이 옮겨 써서 컴파일 에러를 낸다.
        // 그래서 소수 기본값은 Auto(-1)로 두고 메서드 안에서 실제 기본값으로 바꾼다. 정수 기본값(1)은 괜찮다.
        const int Auto = -1;
        static float Or(float value, float fallback) => value < 0f ? fallback : value;

        static StoryStage Stage(string command)
        {
            var stage = StoryStage.Current;
            if (stage == null) Debug.LogWarning($"[명령] <<{command}>>: 이 씬에 StoryStage가 없다.");
            return stage;
        }

        static void LogOnly(string name, string[] args)
        {
            Debug.Log($"[명령] <<{name}{(args.Length > 0 ? " " + string.Join(" ", args) : "")}>>  (아직 로그만 찍는 빈 구현)");
        }
    }
}
