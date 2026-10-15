// Editor tool: writes the default parameter asset Assets/Resources/TransmissionParameters.asset (the experimental transmission model loads it at runtime).
using UnityEditor; using UnityEngine;

public static class TransmissionParametersAsset
{
    const string assetPath = "Assets/Resources/TransmissionParameters.asset";

    // @description - creates or refreshes the parameter asset with the documented default values
    // @return {Void}
    [MenuItem("Scirocco/Write transmission parameter asset")]
    public static void Write()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var existing = AssetDatabase.LoadAssetAtPath<TransmissionParameters>(assetPath);
        if (existing == null) AssetDatabase.CreateAsset(TransmissionParameters.CreateDefault(), assetPath);
        AssetDatabase.SaveAssets(); Debug.Log("Wrote " + assetPath);
    }
}
