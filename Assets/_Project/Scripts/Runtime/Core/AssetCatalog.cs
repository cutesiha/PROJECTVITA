using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectVita.Core
{
    /// <summary>
    /// 이름으로 리소스를 찾는 목록. 에디터의 AssetCatalogBuilder가 폴더를 훑어서 자동으로 채운다 (손으로 고치지 않는다).
    /// - 배경: Art/Backgrounds/{이름}.png → &lt;&lt;bg 이름&gt;&gt;
    /// - CG: Art/CG/{이름}.png → &lt;&lt;cg 이름&gt;&gt;
    /// - BGM, 효과음: Audio/BGM, Audio/SFX/{이름}.wav|ogg|mp3 → &lt;&lt;bgm 이름&gt;&gt;, &lt;&lt;sfx 이름&gt;&gt;
    /// - 캐릭터: Data/Characters.csv + Art/Characters/{id}/{id}_{표정}.png
    /// 최종 아트는 같은 파일 이름으로 덮어쓰면 그대로 바뀐다.
    /// </summary>
    public sealed class AssetCatalog : ScriptableObject
    {
        /// <summary>Assets/_Project/Resources/AssetCatalog.asset</summary>
        public const string ResourcePath = "AssetCatalog";

        [Serializable]
        public sealed class SpriteEntry
        {
            public string name;
            public Sprite sprite;
        }

        [Serializable]
        public sealed class AudioEntry
        {
            public string name;
            public AudioClip clip;
        }

        [Serializable]
        public sealed class CharacterEntry
        {
            /// <summary>영어 id. 폴더 이름과 파일 이름 앞부분.</summary>
            public string id;
            /// <summary>Yarn 대사의 화자 이름 ("주인공: 대사"의 "주인공").</summary>
            public string yarnName;
            public Color nameColor = new Color(0.70f, 0.15f, 0.12f);
            public List<SpriteEntry> expressions = new List<SpriteEntry>();

            public const string DefaultExpression = "default";

            /// <summary>표정 스프라이트. 없으면 default, 그것도 없으면 첫 번째 표정.</summary>
            public Sprite GetExpression(string expression, out string resolved)
            {
                resolved = null;
                if (expressions.Count == 0) return null;
                var hit = Find(expression) ?? Find(DefaultExpression) ?? expressions[0];
                resolved = hit.name;
                return hit.sprite;
            }

            public bool HasExpression(string expression) => Find(expression) != null;

            SpriteEntry Find(string key)
            {
                if (string.IsNullOrEmpty(key)) return null;
                foreach (var e in expressions)
                    if (string.Equals(e.name, key, StringComparison.OrdinalIgnoreCase)) return e;
                return null;
            }
        }

        public List<SpriteEntry> backgrounds = new List<SpriteEntry>();
        public List<SpriteEntry> cgs = new List<SpriteEntry>();
        public List<AudioEntry> bgm = new List<AudioEntry>();
        public List<AudioEntry> sfx = new List<AudioEntry>();
        public List<CharacterEntry> characters = new List<CharacterEntry>();
        /// <summary>전투 전용 그림 (Art/Battle/{portrait_id}.png). 없으면 대화 스탠딩을 쓴다.</summary>
        public List<SpriteEntry> battlePortraits = new List<SpriteEntry>();

        /// <summary>전투 그림: Art/Battle에 있으면 그것, 없으면 같은 id 캐릭터의 기본 표정.</summary>
        public Sprite FindBattlePortrait(string id)
        {
            var sprite = FindIn(battlePortraits, id)?.sprite;
            if (sprite != null) return sprite;
            return FindCharacter(id)?.GetExpression(CharacterEntry.DefaultExpression, out _);
        }

        // ------------------------------------------------------------ 찾기

        static AssetCatalog instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static AssetCatalog Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<AssetCatalog>(ResourcePath);
                    if (instance == null)
                    {
                        Debug.LogError("[Catalog] Resources/AssetCatalog가 없다. Tools > Project Vita > 리소스 목록 갱신을 실행한다.");
                        instance = CreateInstance<AssetCatalog>();
                    }
                }
                return instance;
            }
        }

        public Sprite FindBackground(string name) => FindIn(backgrounds, name)?.sprite;
        public Sprite FindCG(string name) => FindIn(cgs, name)?.sprite;
        public AudioClip FindBgm(string name) => FindIn(bgm, name)?.clip;
        public AudioClip FindSfx(string name) => FindIn(sfx, name)?.clip;

        /// <summary>id 또는 Yarn 화자 이름으로 캐릭터를 찾는다 (대소문자 무시).</summary>
        public CharacterEntry FindCharacter(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            key = key.Trim();
            foreach (var c in characters)
                if (string.Equals(c.id, key, StringComparison.OrdinalIgnoreCase) || string.Equals(c.yarnName, key, StringComparison.OrdinalIgnoreCase))
                    return c;
            return null;
        }

        static SpriteEntry FindIn(List<SpriteEntry> list, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var e in list)
                if (string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        static AudioEntry FindIn(List<AudioEntry> list, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var e in list)
                if (string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }
    }
}
