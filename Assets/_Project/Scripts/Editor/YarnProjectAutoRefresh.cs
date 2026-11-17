using System;
using System.IO;
using System.Linq;
using UnityEditor;

namespace ProjectVita.EditorTools
{
    /// <summary>
    /// Dialogue 폴더에 .yarn 파일이 새로 생기거나 지워지거나 옮겨지면 Dialogue.yarnproject를 다시 임포트한다.
    /// Yarn Spinner 3.3.2는 기존 .yarn을 고치면 프로젝트를 다시 컴파일하지만, **새 파일은 프로젝트를 다시 임포트할 때까지 빠진다.**
    /// 작가가 새 파일을 올린 뒤 pull해도 노드가 안 보이는 문제를 막는다.
    /// delayCall은 에디터가 쉬는 동안 돌지 않을 때가 있어서, 후처리 안에서 바로 재임포트를 요청한다.
    /// </summary>
    public sealed class YarnProjectAutoRefresh : AssetPostprocessor
    {
        const string DialogueDir = "Assets/_Project/Dialogue/";
        const string ProjectPath = DialogueDir + "Dialogue.yarnproject";

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
        {
            // 프로젝트가 이번에 이미 다시 임포트됐으면 할 일이 없다 (무한 반복 방지).
            if (imported.Contains(ProjectPath)) return;

            bool changed = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                .Any(p => p.StartsWith(DialogueDir, StringComparison.Ordinal) && p.EndsWith(".yarn", StringComparison.OrdinalIgnoreCase));
            if (!changed || !File.Exists(ProjectPath)) return;

            AssetDatabase.ImportAsset(ProjectPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
