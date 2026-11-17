using UnityEngine;
using Yarn.Unity;

namespace ProjectVita.Story
{
    /// <summary>
    /// Yarn 변수($변수)와 노드 방문 기록을 씬 밖에 보관한다.
    /// 에피소드마다 씬을 다시 로드하므로, DialogueRunner에 붙은 저장소를 쓰면 에피소드가 바뀔 때 변수가 사라진다.
    /// TODO(4-4): 세이브와 로드는 이 저장소의 GetAllVariables / SetAllVariables로 한다.
    /// </summary>
    public static class GameVariables
    {
        static InMemoryVariableStorage storage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => storage = null;

        public static VariableStorageBehaviour Storage
        {
            get
            {
                if (storage == null)
                {
                    var go = new GameObject("[GameVariables]");
                    Object.DontDestroyOnLoad(go);
                    storage = go.AddComponent<InMemoryVariableStorage>();
                }
                return storage;
            }
        }

        /// <summary>새 게임을 시작할 때 모든 변수를 지운다.</summary>
        public static void Clear()
        {
            if (storage != null) storage.Clear();
        }
    }
}
