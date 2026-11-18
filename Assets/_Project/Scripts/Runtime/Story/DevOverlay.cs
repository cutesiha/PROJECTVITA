using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectVita.Story
{
    /// <summary>
    /// 개발용 정보 표시: 현재 에피소드, 노드, 최근 명령. F1로 켜고 끈다.
    /// 4-2단계에서는 명령이 로그만 찍으므로, 콘솔을 보지 않고도 명령이 실행됐는지 화면에서 확인하려고 둔다.
    /// 릴리스 빌드(Development Build 아님)에서는 숨는다.
    /// </summary>
    public sealed class DevOverlay : MonoBehaviour
    {
        const int MaxCommands = 8;

        [SerializeField] TMP_Text text;

        static DevOverlay instance;
        static bool hiddenByUser;
        static string episode = "-";
        static string node = "-";
        static string status = "";
        static readonly Queue<string> commands = new Queue<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            hiddenByUser = false;
            episode = "-";
            node = "-";
            status = "";
            commands.Clear();
        }

        void Awake()
        {
            instance = this;
            if (!Debug.isDebugBuild) gameObject.SetActive(false);
            Refresh();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) hiddenByUser = !hiddenByUser;
            // F5: 지금 노드(탐색이면 장소)를 처음부터 다시 연다. .yarn을 고친 뒤 바로 확인할 때 쓴다.
            if (kb.f5Key.wasPressedThisFrame)
                DialogueTest.ReloadCurrent("F5");
        }

        // 메뉴(일시정지, 대사 기록, 슬롯)가 열려 있으면 숨는다. 메뉴 제목과 겹치지 않고, 세이브 썸네일에도 찍히지 않는다.
        // LateUpdate라서 메뉴를 연 프레임의 화면 캡처(프레임 끝) 전에 숨는다.
        void LateUpdate()
        {
            if (text == null) return;
            bool show = !hiddenByUser && !ProjectVita.Core.GamePause.IsPaused;
            if (text.enabled != show) text.enabled = show;
        }

        public static void SetEpisode(Episode ep)
        {
            episode = ep == null ? "-" : ep.ToString();
            commands.Clear();
            status = "";
            Refresh();
        }

        public static void SetNode(string nodeName)
        {
            node = nodeName;
            Refresh();
        }

        /// <summary>오류처럼 화면에서 바로 봐야 하는 메시지.</summary>
        public static void SetStatus(string message)
        {
            status = message;
            Refresh();
        }

        public static void PushCommand(string commandText)
        {
            commands.Enqueue(commandText);
            while (commands.Count > MaxCommands) commands.Dequeue();
            Refresh();
        }

        static void Refresh()
        {
            if (instance == null || instance.text == null) return;
            var sb = new StringBuilder();
            sb.Append("<b>[DEV]</b> F1 숨기기 · F5 노드 다시 시작\n");
            sb.Append("에피소드: ").Append(episode).Append('\n');
            sb.Append("노드: ").Append(node).Append('\n');
            if (!string.IsNullOrEmpty(status)) sb.Append("<color=#FF6B6B><noparse>").Append(status).Append("</noparse></color>\n");
            if (commands.Count > 0)
            {
                sb.Append("최근 명령:\n");
                // <<bg ...>>의 <bg ...>가 TMP 태그로 해석되지 않게 noparse로 감싼다.
                foreach (var c in commands) sb.Append("  <noparse><<").Append(c).Append(">></noparse>\n");
            }
            instance.text.text = sb.ToString();
        }
    }
}
