using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectVita.Core;
using UnityEditor;
using UnityEngine;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// AssetCatalog(Resources/AssetCatalog.asset)를 폴더 내용으로 자동 갱신한다.
    /// 감시 폴더에 그림이나 소리를 넣거나 지우면, 임포트가 끝난 뒤 목록이 저절로 바뀐다. 손으로 할 일이 없다.
    /// - Art/Backgrounds, Art/CG: 파일 이름 = &lt;&lt;bg&gt;&gt;, &lt;&lt;cg&gt;&gt; 이름
    /// - Art/Characters/{id}/{id}_{표정}.png + Data/Characters.csv (id, Yarn 화자 이름, 이름표 색)
    /// - Audio/BGM, Audio/SFX: 파일 이름 = &lt;&lt;bgm&gt;&gt;, &lt;&lt;sfx&gt;&gt; 이름
    /// 그림은 Sprite로, BGM은 Streaming으로 임포트 설정도 맞춘다.
    /// </summary>
    public sealed class AssetCatalogBuilder : AssetPostprocessor
    {
        const string MenuRoot = "Tools/Project Vita/";

        public const string CatalogPath = "Assets/_Project/Resources/AssetCatalog.asset";
        public const string BackgroundsDir = "Assets/_Project/Art/Backgrounds";
        public const string CGDir = "Assets/_Project/Art/CG";
        public const string CharactersDir = "Assets/_Project/Art/Characters";
        public const string BattleArtDir = "Assets/_Project/Art/Battle";
        public const string BgmDir = "Assets/_Project/Audio/BGM";
        public const string SfxDir = "Assets/_Project/Audio/SFX";
        public const string CharactersCsvPath = "Assets/_Project/Data/Characters.csv";

        static readonly string[] WatchedDirs = { BackgroundsDir, CGDir, CharactersDir, BattleArtDir, BgmDir, SfxDir };

        static bool rebuildQueued;

        // ------------------------------------------------------------ 임포트 설정

        void OnPreprocessTexture()
        {
            if (!IsUnder(assetPath, BackgroundsDir, CGDir, CharactersDir, BattleArtDir)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.maxTextureSize = 4096;
        }

        void OnPreprocessAudio()
        {
            if (!IsUnder(assetPath, BgmDir, SfxDir)) return;
            var ai = (AudioImporter)assetImporter;
            var settings = ai.defaultSampleSettings;
            settings.loadType = IsUnder(assetPath, BgmDir) ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            ai.defaultSampleSettings = settings;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
        {
            if (rebuildQueued) return;
            bool relevant = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                .Any(p => IsUnder(p, WatchedDirs) || p == CharactersCsvPath);
            if (!relevant) return;

            rebuildQueued = true;
            EditorApplication.delayCall += () =>
            {
                rebuildQueued = false;
                Rebuild(log: false);
            };
        }

        // ------------------------------------------------------------ 갱신

        [MenuItem(MenuRoot + "리소스 목록 갱신", priority = 12)]
        public static void RebuildFromMenu() => Rebuild(log: true);

        public static AssetCatalog Rebuild(bool log)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(CatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                catalog = ScriptableObject.CreateInstance<AssetCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.backgrounds = CollectSprites(BackgroundsDir, "배경");
            catalog.cgs = CollectSprites(CGDir, "CG");
            catalog.bgm = CollectAudio(BgmDir, "BGM");
            catalog.sfx = CollectAudio(SfxDir, "효과음");
            catalog.characters = CollectCharacters();
            catalog.battlePortraits = CollectSprites(BattleArtDir, "전투 그림");

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            if (log)
                Debug.Log($"[Catalog] 리소스 목록 갱신: 배경 {catalog.backgrounds.Count}, CG {catalog.cgs.Count}, BGM {catalog.bgm.Count}, " +
                          $"효과음 {catalog.sfx.Count}, 캐릭터 {catalog.characters.Count} " +
                          $"({string.Join(", ", catalog.characters.Select(c => $"{c.id}={c.yarnName}[{string.Join("/", c.expressions.Select(e => e.name))}]"))})");
            return catalog;
        }

        static List<AssetCatalog.SpriteEntry> CollectSprites(string dir, string label)
        {
            var list = new List<AssetCatalog.SpriteEntry>();
            if (!AssetDatabase.IsValidFolder(dir)) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                if (!seen.Add(name))
                {
                    Debug.LogWarning($"[Catalog] {label} 이름 '{name}'이 겹친다: {path}. 앞의 것을 쓴다.");
                    continue;
                }
                list.Add(new AssetCatalog.SpriteEntry { name = name, sprite = sprite });
            }
            return list.OrderBy(e => e.name).ToList();
        }

        static List<AssetCatalog.AudioEntry> CollectAudio(string dir, string label)
        {
            var list = new List<AssetCatalog.AudioEntry>();
            if (!AssetDatabase.IsValidFolder(dir)) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                if (!seen.Add(name))
                {
                    Debug.LogWarning($"[Catalog] {label} 이름 '{name}'이 겹친다: {path}. 앞의 것을 쓴다.");
                    continue;
                }
                list.Add(new AssetCatalog.AudioEntry { name = name, clip = clip });
            }
            return list.OrderBy(e => e.name).ToList();
        }

        static List<AssetCatalog.CharacterEntry> CollectCharacters()
        {
            var result = new List<AssetCatalog.CharacterEntry>();
            var byId = new Dictionary<string, AssetCatalog.CharacterEntry>(StringComparer.OrdinalIgnoreCase);

            // 1) Characters.csv: id, Yarn 화자 이름, 이름표 색
            var csv = AssetDatabase.LoadAssetAtPath<TextAsset>(CharactersCsvPath);
            if (csv != null)
            {
                var table = CsvTable.Parse(csv.name, csv.text);
                foreach (var row in table.Rows)
                {
                    string id = row["character_id"];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (byId.ContainsKey(id))
                    {
                        Debug.LogWarning($"[Catalog] Characters.csv {row.LineNumber}행: character_id '{id}'가 겹친다.");
                        continue;
                    }
                    var entry = new AssetCatalog.CharacterEntry { id = id, yarnName = row["yarn_name"] };
                    if (string.IsNullOrEmpty(entry.yarnName)) entry.yarnName = id;
                    string color = row["name_color"];
                    if (!string.IsNullOrEmpty(color))
                    {
                        if (ColorUtility.TryParseHtmlString(color, out var c)) entry.nameColor = c;
                        else Debug.LogWarning($"[Catalog] Characters.csv {row.LineNumber}행: name_color '{color}'를 읽지 못했다 (#RRGGBB).");
                    }
                    byId.Add(id, entry);
                    result.Add(entry);
                }
            }

            // 2) Art/Characters/{id}/ 폴더의 그림 = 표정. 파일 이름에서 "{id}_"를 뗀 부분이 표정 이름이다.
            if (AssetDatabase.IsValidFolder(CharactersDir))
            {
                foreach (var folder in AssetDatabase.GetSubFolders(CharactersDir))
                {
                    string id = Path.GetFileName(folder);
                    if (!byId.TryGetValue(id, out var entry))
                    {
                        Debug.LogWarning($"[Catalog] Art/Characters/{id} 폴더가 Characters.csv에 없다. Yarn 화자 이름을 '{id}'로 둔다.");
                        entry = new AssetCatalog.CharacterEntry { id = id, yarnName = id };
                        byId.Add(id, entry);
                        result.Add(entry);
                    }
                    foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                        if (sprite == null) continue;
                        string file = Path.GetFileNameWithoutExtension(path);
                        string expr = file.StartsWith(id + "_", StringComparison.OrdinalIgnoreCase) ? file.Substring(id.Length + 1) : file;
                        entry.expressions.Add(new AssetCatalog.SpriteEntry { name = expr, sprite = sprite });
                    }
                    entry.expressions = entry.expressions.OrderBy(e => e.name == AssetCatalog.CharacterEntry.DefaultExpression ? "" : e.name).ToList();
                }
            }

            foreach (var e in result)
                if (e.expressions.Count == 0)
                    Debug.LogWarning($"[Catalog] 캐릭터 '{e.id}'의 그림이 없다. {CharactersDir}/{e.id}/{e.id}_default.png를 넣는다.");
            return result;
        }

        static bool IsUnder(string path, params string[] dirs)
        {
            foreach (var d in dirs)
                if (path.StartsWith(d + "/", StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
