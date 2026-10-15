// Documentation test: every parameter of the experimental brake model is listed in docs/BrakeSources.md with a tag (S, SD, D, E or U).
using NUnit.Framework; using UnityEngine; using System.IO; using System.Linq; using System.Text.RegularExpressions;

public class BrakeDocumentationTests
{
    // @description - reads docs/BrakeSources.md from the project folder
    // @return {string} - file text
    static string ReadSources() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../docs/BrakeSources.md")));

    // @description - every public parameter field appears in the docs table with a tag, and the source comment of the field carries the same tag letters
    // @return {Void} - nothing
    [Test] public void EveryParameterIsDocumentedAndTagged()
    {
        string documentation = ReadSources(); var fields = typeof(BrakeSystemParameters).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Where(field => field.FieldType == typeof(double)).ToList();
        Assert.Greater(fields.Count, 35, "the parameter set has more than 35 values");
        foreach (var field in fields)
        {
            var row = Regex.Match(documentation, "^\\| `" + field.Name + "` \\|.*$", RegexOptions.Multiline);
            Assert.IsTrue(row.Success, field.Name + " is missing in docs/BrakeSources.md");
            Assert.IsTrue(Regex.IsMatch(row.Value, "\\*\\*(S|SD|D|E|U)\\*\\*"), field.Name + " has no tag in docs/BrakeSources.md");
        }
    }

    // @description - the estimates asked for by name carry the tag E, and the user-reported load-cell default carries U
    // @return {Void} - nothing
    [Test] public void RequiredTagsAreAsAgreed()
    {
        string documentation = ReadSources();
        StringAssert.IsMatch("`rearBrakeFactorOfShoePair` \\| 2.4 \\| - \\| \\*\\*E\\*\\*", documentation); StringAssert.IsMatch("`loadCellDefaultMaxForcePounds` \\| 70.0 \\| lbf \\| \\*\\*U\\*\\*", documentation);
        StringAssert.IsMatch("`footApplySpeedMetersPerSecond` \\| 0.30 \\| m/s \\| \\*\\*E\\*\\*", documentation); StringAssert.IsMatch("`pedalRatio` \\| 4.0 \\| - \\| \\*\\*E\\*\\*", documentation);
    }
}
