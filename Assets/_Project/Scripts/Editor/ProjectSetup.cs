using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// 1단계 기본 세팅을 메뉴 하나로 적용한다. 여러 번 실행해도 안전하다 (이미 된 것은 건너뜀).
    /// 새 PC에서 clone한 뒤에는 실행할 필요가 없다. 결과물이 전부 레포에 커밋되기 때문이다.
    /// </summary>
    public static class ProjectSetup
    {
        const string MenuRoot = "Tools/Project Vita/";

        // 한글 폰트: 에이투지체 (본문 Medium, 굵게/제목 Bold). 상업 이용 가능한 무료 폰트. 2026-11-18 Pretendard에서 바꿈.
        const string FontDir = "Assets/_Project/Art/Fonts/A2Z";
        const string FontRegular = FontDir + "/A2Z-Medium.ttf";
        const string FontBold = FontDir + "/A2Z-Bold.ttf";
        internal const string FontAssetRegular = FontDir + "/A2Z-Medium SDF.asset";
        internal const string FontAssetBold = FontDir + "/A2Z-Bold SDF.asset";

        const string OldScenePath = "Assets/Scenes/SampleScene.unity";
        const string BootScenePath = "Assets/_Project/Scenes/Title.unity";

        // Dynamic 아틀라스 설정. 한글은 글리프가 많아서 아틀라스를 크게, 멀티 아틀라스를 켠다.
        const int SamplingPointSize = 64;
        const int AtlasPadding = 6;
        const int AtlasSize = 2048;

        [MenuItem(MenuRoot + "1단계 기본 세팅 적용", priority = 0)]
        public static void ApplyAll()
        {
            ApplyPlayerSettings();
            MoveBootScene();

            // TMP Essential Resources가 없으면 먼저 임포트하고, 끝나면 이어서 진행한다.
            if (FindTmpSettings() == null)
            {
                ImportTmpEssentials();
                return;
            }

            ApplyTmpAndFonts();
        }

        // ---------------------------------------------------------------- Player Settings

        static void ApplyPlayerSettings()
        {
            // TODO(가제 확정 후): Company와 Product 이름을 실제 값으로 바꾼다.
            // productName을 바꾸면 세이브 경로(persistentDataPath)도 바뀐다.
            if (PlayerSettings.companyName == "DefaultCompany")
                PlayerSettings.companyName = "TeamVita";

            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            // Active Input Handling: Input System Package (New) 하나만 쓴다.
            // 공개 API가 없어서 ProjectSettings를 직접 고친다. 0=Old, 1=New, 2=Both
            var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (playerSettings != null)
            {
                var so = new SerializedObject(playerSettings);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 1)
                {
                    prop.intValue = 1;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.LogWarning("[Setup] Active Input Handling을 Input System (New)로 바꿨다. 에디터를 재시작해야 적용된다.");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Player Settings 적용: 1920x1080 창 모드, Input System (New)");
        }

        // ---------------------------------------------------------------- 씬

        static void MoveBootScene()
        {
            if (File.Exists(OldScenePath) && !File.Exists(BootScenePath))
            {
                // 열려 있는 씬이면 저장부터 한다.
                EditorSceneManager.SaveOpenScenes();
                string err = AssetDatabase.MoveAsset(OldScenePath, BootScenePath);
                if (!string.IsNullOrEmpty(err))
                {
                    Debug.LogError("[Setup] 씬 이동 실패: " + err);
                    return;
                }
                if (AssetDatabase.IsValidFolder("Assets/Scenes") &&
                    AssetDatabase.FindAssets("", new[] { "Assets/Scenes" }).Length == 0)
                    AssetDatabase.DeleteAsset("Assets/Scenes");
            }

            if (File.Exists(BootScenePath))
            {
                // 빌드 씬 목록. 2단계에서 Story 등이 추가된다.
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
                Debug.Log("[Setup] 빌드 씬: " + BootScenePath);
            }
        }

        // ---------------------------------------------------------------- TMP

        static TMP_Settings FindTmpSettings()
        {
            string guid = AssetDatabase.FindAssets("t:TMP_Settings").FirstOrDefault();
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<TMP_Settings>(AssetDatabase.GUIDToAssetPath(guid));
        }

        static void ImportTmpEssentials()
        {
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_FontAsset).Assembly);
            string path = pkg == null ? null : Path.Combine(pkg.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            if (path == null || !File.Exists(path))
            {
                Debug.LogError("[Setup] TMP Essential Resources 패키지를 찾지 못했다. Window > TextMeshPro > Import TMP Essential Resources로 직접 임포트한 뒤 다시 실행한다.");
                return;
            }

            AssetDatabase.importPackageCompleted += OnTmpImported;
            AssetDatabase.importPackageFailed += OnTmpImportFailed;
            AssetDatabase.ImportPackage(path, false);
            Debug.Log("[Setup] TMP Essential Resources 임포트 중...");
        }

        static void OnTmpImported(string packageName)
        {
            UnhookImport();
            AssetDatabase.Refresh();
            EditorApplication.delayCall += ApplyTmpAndFonts;
        }

        static void OnTmpImportFailed(string packageName, string error)
        {
            UnhookImport();
            Debug.LogError("[Setup] TMP Essential Resources 임포트 실패: " + error);
        }

        static void UnhookImport()
        {
            AssetDatabase.importPackageCompleted -= OnTmpImported;
            AssetDatabase.importPackageFailed -= OnTmpImportFailed;
        }

        internal static void ApplyTmpAndFonts()
        {
            var settings = FindTmpSettings();
            if (settings == null)
            {
                Debug.LogError("[Setup] TMP Settings를 찾지 못했다. 메뉴를 한 번 더 실행한다.");
                return;
            }

            var regular = GetOrCreateFontAsset(FontRegular, FontAssetRegular);
            var bold = GetOrCreateFontAsset(FontBold, FontAssetBold);
            if (regular == null) return;

            // <b> 태그가 가짜 굵게 대신 실제 Bold 폰트를 쓰게 한다. 7 = 700 (Bold)
            if (bold != null && regular.fontWeightTable != null && regular.fontWeightTable.Length > 7)
            {
                regular.fontWeightTable[7].regularTypeface = bold;
                EditorUtility.SetDirty(regular);
            }

            var so = new SerializedObject(settings);
            // 끄면 한글이 단어 중간에서 줄바꿈된다. TMP Essential Resources를 다시 임포트하면 꺼지니 이 메뉴를 다시 실행한다.
            so.FindProperty("m_UseModernHangulLineBreakingRules").boolValue = true;
            so.FindProperty("m_defaultFontAsset").objectReferenceValue = regular;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);

            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] TMP: 기본 폰트 = A2Z-Medium SDF (Dynamic, 굵게 = A2Z-Bold SDF), Modern Hangul Line Breaking = ON");
        }

        internal static TMP_FontAsset GetOrCreateFontAsset(string fontPath, string assetPath, int atlasSize = AtlasSize)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null)
            {
                Debug.LogError("[Setup] 폰트 파일이 없다: " + fontPath);
                return null;
            }

            var fa = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                atlasSize, atlasSize, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
            if (fa == null) return null;

            // 아웃라인, 글로우 같은 연출을 쓰려고 Mobile 대신 일반 Distance Field 셰이더를 쓴다.
            var sdf = Shader.Find("TextMeshPro/Distance Field");
            if (sdf != null) fa.material.shader = sdf;

            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.atlasTextures[0].name = baseName + " Atlas";
            fa.material.name = baseName + " Material";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            AssetDatabase.SaveAssets();

            Debug.Log("[Setup] 폰트 에셋 생성: " + assetPath);
            return fa;
        }

        // ---------------------------------------------------------------- 커밋 전 정리

        /// <summary>
        /// Dynamic 폰트 에셋은 플레이하는 동안 글리프가 쌓여서 커진다. 커밋 전에 비운다.
        /// </summary>
        [MenuItem(MenuRoot + "Dynamic 폰트 비우기 (커밋 전)", priority = 100)]
        public static void ClearDynamicFonts()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/_Project" }))
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (fa == null || fa.atlasPopulationMode != AtlasPopulationMode.Dynamic) continue;
                fa.ClearFontAssetData(setAtlasSizeToZero: true);
                EditorUtility.SetDirty(fa);
                count++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Setup] Dynamic 폰트 에셋 {count}개를 비웠다.");
        }
    }
}
