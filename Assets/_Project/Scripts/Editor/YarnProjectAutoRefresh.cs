using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectVita.Story;
using UnityEditor;
using UnityEngine;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// Dialogue 폴더에 .yarn 파일이 새로 생기거나 지워지거나 옮겨지면 Dialogue.yarnproject를 다시 임포트한다.
    /// Yarn Spinner 3.3.2는 기존 .yarn을 고치면 프로젝트를 다시 컴파일하지만, **새 파일은 프로젝트를 다시 임포트할 때까지 빠진다.**
    /// 작가가 새 파일을 올린 뒤 pull해도 노드가 안 보이는 문제를 막는다.
    /// delayCall은 에디터가 쉬는 동안 돌지 않을 때가 있어서, 후처리 안에서 바로 재임포트를 요청한다.
    ///
    /// 플레이 중 핫리로드 (문서 4-7): 플레이 중에 Yarn 프로젝트가 다시 컴파일되면 게임에 알려서 지금 노드를 처음부터 다시 연다.
    /// 플레이 중에는 Unity의 자동 새로 고침이 꺼져 있을 수 있어서, .yarn 파일의 수정 시각을 1초마다 보고 직접 임포트한다.
    /// </summary>
    [InitializeOnLoad]
    public sealed class YarnProjectAutoRefresh : AssetPostprocessor
    {
        const string DialogueDir = "Assets/_Project/Dialogue/";
        const string ProjectPath = DialogueDir + "Dialogue.yarnproject";

        static readonly Dictionary<string, DateTime> stamps = new Dictionary<string, DateTime>();
        static double nextCheck;

        static YarnProjectAutoRefresh()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
        {
            if (imported.Contains(ProjectPath))
            {
                // 플레이 중에 다시 컴파일됐다 → 게임이 지금 장면을 다시 연다.
                if (EditorApplication.isPlaying)
                {
                    var changed = imported.Where(IsYarn).Select(Path.GetFileName).ToArray();
                    foreach (var p in imported.Where(IsYarn)) Stamp(p);
                    string files = changed.Length > 0 ? string.Join(", ", changed) : "Dialogue.yarnproject";
                    DialogueTest.ReloadCurrent($"대사 변경 ({files})");
                }
                // 프로젝트가 이번에 이미 다시 임포트됐으면 할 일이 없다 (무한 반복 방지).
                return;
            }

            bool changedFiles = imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsYarn);
            if (!changedFiles || !File.Exists(ProjectPath)) return;

            AssetDatabase.ImportAsset(ProjectPath, ImportAssetOptions.ForceUpdate);
        }

        static bool IsYarn(string p) =>
            p.StartsWith(DialogueDir, StringComparison.Ordinal) && p.EndsWith(".yarn", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------ 플레이 중 파일 감시

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            stamps.Clear();
            foreach (var path in YarnFiles()) Stamp(path);
        }

        static void Poll()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 1.0;

            List<string> changed = null;
            foreach (var path in YarnFiles())
            {
                var time = File.GetLastWriteTimeUtc(path);
                if (stamps.TryGetValue(path, out var known) && known == time) continue;
                stamps[path] = time;
                (changed ??= new List<string>()).Add(path);
            }
            if (changed == null) return;

            // 이 .yarn들을 임포트하면 Yarn 프로젝트가 다시 컴파일되고, 위의 후처리가 게임에 알린다.
            Debug.Log("[핫리로드] 바뀐 대사 파일: " + string.Join(", ", changed.Select(Path.GetFileName)));
            foreach (var path in changed) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        static IEnumerable<string> YarnFiles()
        {
            if (!Directory.Exists(DialogueDir)) yield break;
            foreach (var f in Directory.GetFiles(DialogueDir, "*.yarn", SearchOption.AllDirectories))
                yield return f.Replace('\\', '/');
        }

        static void Stamp(string path)
        {
            if (File.Exists(path)) stamps[path] = File.GetLastWriteTimeUtc(path);
        }
    }
}
