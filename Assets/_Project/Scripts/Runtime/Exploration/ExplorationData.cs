using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectVita.Exploration
{
    /// <summary>
    /// 탐색 데이터. Data/Exploration_Location.csv, Exploration_Object.csv를 에디터 임포터가 이 에셋으로 바꾼다.
    /// (Resources/ExplorationData.asset, 손으로 고치지 않는다)
    /// </summary>
    public sealed class ExplorationData : ScriptableObject
    {
        public const string ResourcePath = "ExplorationData";

        [Serializable]
        public sealed class LocationDef
        {
            public string id;
            public string displayName;
            /// <summary>AssetCatalog 배경 이름</summary>
            public string background;
            /// <summary>처음 들어갈 때 한 번 실행하는 Yarn 노드 (비면 없음)</summary>
            public string enterNode;
            /// <summary>지도에 나타나는 조건 (Yarn식, 비면 항상)</summary>
            public string unlockIf;
        }

        [Serializable]
        public sealed class ObjectDef
        {
            public string id;
            public string locationId;
            public string displayName;
            /// <summary>클릭하면 실행할 Yarn 노드 (비면 대사 없음)</summary>
            public string node;
            /// <summary>보이는 조건 (Yarn식, 비면 항상)</summary>
            public string showIf;
            /// <summary>true면 한 번 조사한 뒤 사라진다</summary>
            public bool once;
            /// <summary>클릭 영역. 1920x1080 기준, 왼쪽 위가 (0,0)인 픽셀 좌표</summary>
            public Rect area;
            /// <summary>대사가 끝난 뒤 시작할 전투 (비면 없음)</summary>
            public string encounterId;
        }

        public List<LocationDef> locations = new List<LocationDef>();
        public List<ObjectDef> objects = new List<ObjectDef>();

        public static string SeenVariable(string objectId) => "$" + objectId + "_seen";
        public static string VisitedVariable(string locationId) => "$" + locationId + "_visited";

        static ExplorationData instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static ExplorationData Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<ExplorationData>(ResourcePath);
                    if (instance == null)
                    {
                        Debug.LogError("[탐색] Resources/ExplorationData가 없다. Tools > Project Vita > CSV 임포트를 실행한다.");
                        instance = CreateInstance<ExplorationData>();
                    }
                }
                return instance;
            }
        }

        public LocationDef FindLocation(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var l in locations)
                if (string.Equals(l.id, id, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        public IEnumerable<ObjectDef> ObjectsIn(string locationId)
        {
            foreach (var o in objects)
                if (string.Equals(o.locationId, locationId, StringComparison.OrdinalIgnoreCase)) yield return o;
        }
    }
}
