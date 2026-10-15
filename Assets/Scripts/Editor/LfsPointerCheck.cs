// Detects Git LFS pointer files under Assets (the repo was cloned/opened before `git lfs pull`).
// Unity imports a pointer as a corrupt asset ("Failed to load ... File may be corrupted"); for the Laguna Seca TerrainData this
// gives "Terrain Asset Missing" / no ground, and the import result is cached in Library until the asset is reimported.
// This logs one clear error with the fix and offers a menu item that reimports the affected files after `git lfs pull`.
using System.Collections.Generic; using System.IO; using UnityEditor; using UnityEngine;
[InitializeOnLoad]
public static class LfsPointerCheck
{
    static readonly string[] Exts = { ".asset", ".fbx", ".png", ".raw", ".dll" };
    // @description - Lfs pointer check.
    // @return {Void} - nothing
    static LfsPointerCheck() { EditorApplication.delayCall += () => Check(false); }
    // @description - Find (see the body for details).
    // @return {List<string>} - the find
    static List<string> Find()
    {
        var hits = new List<string>(); if (!Directory.Exists("Assets/Tracks") && !Directory.Exists("Assets/Plugins")) return hits;
        foreach (var root in new[] { "Assets/Tracks", "Assets/Plugins" })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (System.Array.IndexOf(Exts, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                var fi = new FileInfo(f); if (fi.Length > 1024) continue;
                using (var r = new StreamReader(f)) { var l = r.ReadLine(); if (l != null && l.StartsWith("version https://git-lfs.github.com/spec/")) hits.Add(f.Replace('\\', '/')); }
            }
        }
        return hits;
    }
    [MenuItem("Scirocco/Check Git LFS files (reimport after git lfs pull)")]
    // @description - Menu check.
    // @return {Void} - nothing
    static void MenuCheck() => Check(true);
    // @description - Check (see the body for details).
    // @param {bool} reimport - reimport
    // @return {Void} - nothing
    static void Check(bool reimport)
    {
        var hits = Find();
        if (hits.Count > 0) { Debug.LogError($"[LFS] {hits.Count} Git LFS pointer file(s) under Assets (e.g. {hits[0]}): LFS content is missing. Run `git lfs install` and `git lfs pull` in the repo, then Scirocco > Check Git LFS files to reimport (or close Unity and delete Library/)."); return; }
        if (reimport) { AssetDatabase.ImportAsset("Assets/Tracks", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate); Debug.Log("[LFS] all LFS files present; Assets/Tracks reimported"); }
    }
}
