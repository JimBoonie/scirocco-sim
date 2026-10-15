// Detects Git LFS pointer files under Assets (the repo was cloned/opened before `git lfs pull`).
// Unity imports a pointer as a corrupt asset ("Failed to load ... File may be corrupted"); for the Laguna Seca TerrainData this
// gives "Terrain Asset Missing" / no ground, and the import result is cached in Library until the asset is reimported.
// This logs one clear error with the fix and offers a menu item that reimports the affected files after `git lfs pull`.
using System.Collections.Generic; using System.IO; using UnityEditor; using UnityEngine;
[InitializeOnLoad]
public static class LfsPointerCheck
{
    static readonly string[] Exts = { ".asset", ".fbx", ".png", ".raw", ".dll" };
    // @description - Runs the pointer check once after the editor has loaded (delayed one editor tick so the asset database is ready), so a missing git lfs pull is reported as soon as the project is opened.
    // @return {Void} - nothing (static constructor)
    static LfsPointerCheck() { EditorApplication.delayCall += () => Check(false); }
    // @description - Searches Assets/Tracks and Assets/Plugins for Git LFS pointer files: small files (1 KB or less) with a track or plugin extension whose first line starts with the LFS spec URL. Such a file means the real content was never pulled.
    // @return {List<string>} - asset paths (forward slashes) of all LFS pointer files found, empty if none
    static List<string> Find()
    {
        var hits = new List<string>(); if (!Directory.Exists("Assets/Tracks") && !Directory.Exists("Assets/Plugins")) return hits;
        foreach (var root in new[] { "Assets/Tracks", "Assets/Plugins" })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var filePath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (System.Array.IndexOf(Exts, Path.GetExtension(filePath).ToLowerInvariant()) < 0) continue;
                var fileInfo = new FileInfo(filePath); if (fileInfo.Length > 1024) continue;
                using (var reader = new StreamReader(filePath)) { var firstLine = reader.ReadLine(); if (firstLine != null && firstLine.StartsWith("version https://git-lfs.github.com/spec/")) hits.Add(filePath.Replace('\\', '/')); }
            }
        }
        return hits;
    }
    [MenuItem("Scirocco/Check Git LFS files (reimport after git lfs pull)")]
    // @description - Menu entry "Scirocco > Check Git LFS files": runs the check and, when no pointer files remain, reimports Assets/Tracks so assets that were imported as corrupt pointers are loaded properly.
    // @return {Void} - nothing
    static void MenuCheck() => Check(true);
    // @description - Logs one clear error with the fix (git lfs install, git lfs pull) if any pointer files exist; optionally force-reimports the track assets afterwards.
    // @param {bool} reimport - true to reimport Assets/Tracks when no pointer files remain (menu use), false for the automatic startup check
    // @return {Void} - nothing
    static void Check(bool reimport)
    {
        var hits = Find();
        if (hits.Count > 0) { Debug.LogError($"[LFS] {hits.Count} Git LFS pointer file(s) under Assets (e.g. {hits[0]}): LFS content is missing. Run `git lfs install` and `git lfs pull` in the repo, then Scirocco > Check Git LFS files to reimport (or close Unity and delete Library/)."); return; }
        if (reimport) { AssetDatabase.ImportAsset("Assets/Tracks", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate); Debug.Log("[LFS] all LFS files present; Assets/Tracks reimported"); }
    }
}
