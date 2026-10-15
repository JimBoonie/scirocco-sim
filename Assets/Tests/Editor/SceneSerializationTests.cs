// Scene and asset serialization: every key stored in a scene or asset for a project script must still be a field of that script. A field renamed without updating the scene (and without FormerlySerializedAs) is silently dropped by Unity, which left references such as InputRouter.vrManager null.
using NUnit.Framework; using UnityEditor; using System.Collections.Generic; using System.IO; using System.Text.RegularExpressions;
public class SceneSerializationTests
{
    static readonly string[] SceneAndAssetPaths = { "Assets/TestTrack.unity", "Assets/Tracks/LagunaSeca/LagunaSeca.unity", "Assets/SciroccoParams.asset", "Assets/SuspensionTables.asset" };

    // @description - Finds the names a C# source file declares or lists in FormerlySerializedAs attributes.
    // @param {string} sourcePath - C# source path
    // @return {HashSet<string>} - identifiers in the file plus the old serialized names
    static HashSet<string> SerializedNames(string sourcePath)
    {
        string text = File.ReadAllText(sourcePath); var names = new HashSet<string>();
        foreach (Match match in Regex.Matches(text, @"[A-Za-z_]\w*")) names.Add(match.Value);
        foreach (Match match in Regex.Matches(text, "FormerlySerializedAs\\(\"(\\w+)\"\\)")) names.Add(match.Groups[1].Value);
        return names;
    }
    // @description - Lists the stored keys of every project-script MonoBehaviour in a scene or asset file that match no field of the script.
    // @param {string} path - scene or asset path
    // @return {List<string>} - "Script.key" for each orphaned key
    static List<string> OrphanedKeys(string path)
    {
        var orphans = new List<string>(); string text = File.ReadAllText(path);
        foreach (string document in Regex.Split(text, "(?m)^--- "))
        {
            if (!document.StartsWith("!u!114")) continue;
            var scriptMatch = Regex.Match(document, @"m_Script: \{fileID: \d+, guid: (\w+)");
            if (!scriptMatch.Success) continue;
            string sourcePath = AssetDatabase.GUIDToAssetPath(scriptMatch.Groups[1].Value);
            if (!sourcePath.StartsWith("Assets/Scripts") || !sourcePath.EndsWith(".cs")) continue;
            var names = SerializedNames(sourcePath);
            foreach (Match keyMatch in Regex.Matches(document, @"(?m)^  ([A-Za-z_]\w*):"))
            {
                string key = keyMatch.Groups[1].Value;
                if (!key.StartsWith("m_") && !names.Contains(key)) orphans.Add(Path.GetFileNameWithoutExtension(sourcePath) + "." + key);
            }
        }
        return orphans;
    }
    // @description - Verifies no scene or asset stores a key that its script no longer has (renamed fields would lose their values or references).
    // @return {Void} - nothing
    [Test] public void NoOrphanedSerializedKeys()
    {
        foreach (string path in SceneAndAssetPaths)
            Assert.IsEmpty(OrphanedKeys(path), path + " stores keys that no longer match a field: update the scene or add FormerlySerializedAs");
    }
    // @description - Verifies the main scene stores the router's VrManager reference under the current field name, and that the reference is not null.
    // @return {Void} - nothing
    [Test] public void RouterKeepsItsVrManagerReference()
    {
        string text = File.ReadAllText("Assets/TestTrack.unity");
        Assert.IsTrue(Regex.IsMatch(text, @"(?m)^  vrManager: \{fileID: [1-9]\d*\}"), "InputRouter.vrManager must be assigned in TestTrack.unity");
    }
}
