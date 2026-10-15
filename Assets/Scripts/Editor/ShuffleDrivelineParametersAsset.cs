// Editor tool: writes the default parameter asset Assets/Resources/ShuffleDrivelineParameters.asset (the experimental driveline model loads it at runtime).
using UnityEditor; using UnityEngine;

public static class ShuffleDrivelineParametersAsset
{
    const string assetPath = "Assets/Resources/ShuffleDrivelineParameters.asset";

    // @description - creates or refreshes the parameter asset with the documented default values
    // @return {Void}
    [MenuItem("Scirocco/Write shuffle driveline parameter asset")]
    public static void Write()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var existing = AssetDatabase.LoadAssetAtPath<ShuffleDrivelineParameters>(assetPath);
        if (existing == null) AssetDatabase.CreateAsset(ShuffleDrivelineParameters.CreateDefault(), assetPath);
        AssetDatabase.SaveAssets(); Debug.Log("Wrote " + assetPath);
    }
}
