#if UNITY_EDITOR
using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
public static class SciroccoSetup
{
    static WheelCorner Make(CarParams p, GameObject car, string n, float x, float z, bool f, bool l)
        {
            var g = new GameObject(n); g.transform.SetParent(car.transform, false); g.transform.localPosition = new Vector3(x, 0f, z); g.layer = 2;
            var w = g.AddComponent<WheelCorner>(); w.carParams = p; w.front = f; w.left = l;
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(vis.GetComponent<Collider>());
            vis.transform.SetParent(car.transform, false); vis.transform.localScale = new Vector3(p.wheelRadius * 2, 0.0925f, p.wheelRadius * 2); vis.name = n + "_vis";
            w.visual = vis.transform; return w;
        }
    [MenuItem("Scirocco/Build Test Scene")]
    public static void Build()
    {
        Time.fixedDeltaTime = 0.002f; // 500 Hz
        TrackSetup.Build();   // Laguna Seca additive scene (its own scene file; built first because NewScene below replaces the open scene)
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        const string path = "Assets/SciroccoParams.asset";
        var p = AssetDatabase.LoadAssetAtPath<CarParams>(path);
        if (!p) { p = ScriptableObject.CreateInstance<CarParams>(); AssetDatabase.CreateAsset(p, path); }
        const string kpath = "Assets/SuspensionTables.asset";
        var kin = AssetDatabase.LoadAssetAtPath<SuspensionTables>(kpath);
        if (!kin) { kin = ScriptableObject.CreateInstance<SuspensionTables>(); AssetDatabase.CreateAsset(kin, kpath); }
        SuspensionTablesData.Fill(kin); EditorUtility.SetDirty(kin);   // always refresh from generated data
        p.suspensionTables = kin;
        EditorUtility.SetDirty(p);
        AssetDatabase.DeleteAsset("Assets/SuspensionTablesProposed.asset");   // single handling baseline (2026-10-13): the second table set is gone
        // flat track: 2km plane + cones
        var testArea = new GameObject("TestArea");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Track"; ground.transform.SetParent(testArea.transform, false); ground.transform.localScale = new Vector3(1000, 1, 1000); // 10 km x 10 km
        for (int i = 0; i < 20; i++) { var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder); c.transform.position = new Vector3((i % 2) * 4 - 2, 0.3f, 30 + i * 15); c.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f); c.transform.SetParent(testArea.transform, true); Object.DestroyImmediate(c.GetComponent<Collider>()); }
        var car = new GameObject("Scirocco"); car.transform.position = new Vector3(0, p.hubDropDesign + p.wheelRadius + 0.02f, 0);
        var rb = car.AddComponent<Rigidbody>(); rb.interpolation = RigidbodyInterpolation.Interpolate;
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "Body"; body.transform.SetParent(car.transform, false);
        body.transform.localScale = new Vector3(1.62f, 0.75f, 4.05f); body.transform.localPosition = new Vector3(0, 0.25f, 0);
        { // v1.3b: chassis colliders from sourced body dimensions (L 4.050 / W 1.625 / H 1.280 m S, VW; ground clearance 0.125 m S). The old single box
          // (0.46-1.21 m above ground) left the bottom 0.46 m open: a bounce of ~0.2 m put its lower edge above an 0.8 m barrier, which the car then vaulted.
            Object.DestroyImmediate(body.GetComponent<Collider>());
            float g0 = -(p.hubDropDesign + p.wheelRadius);   // ground in car-local y at design ride height
            const float sill = 0.20f, belt = 0.85f, roof = 1.28f;   // m above ground: bumper/sill underside (E, above the 0.125 m S lowest underbody point), belt line (E), roof (S)
            var lo = new GameObject("ChassisLower"); lo.layer = 2; lo.transform.SetParent(car.transform, false);   // IgnoreRaycast like the old body: suspension/probe rays skip the own chassis
            var bl = lo.AddComponent<BoxCollider>(); bl.center = new Vector3(0f, g0 + 0.5f * (sill + belt), 0f); bl.size = new Vector3(1.625f, belt - sill, 4.05f);
            var hi = new GameObject("ChassisCabin"); hi.layer = 2; hi.transform.SetParent(car.transform, false); var bc = hi.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, g0 + 0.5f * (belt + roof), -0.15f); bc.size = new Vector3(1.36f, roof - belt, 2.2f);   // greenhouse width/length (E)
        }
        body.layer = 2; // IgnoreRaycast so suspension rays skip own body
        var ctl = car.AddComponent<CarController>(); ctl.carParams = p;
        float zf = p.wheelbase / 2, zr = -p.wheelbase / 2;
        ctl.frontLeftWheel = Make(p, car, "FL", -p.trackFront / 2, zf, true, true); ctl.frontRightWheel = Make(p, car, "FR", p.trackFront / 2, zf, true, false);
        ctl.rearLeftWheel = Make(p, car, "RL", -p.trackRear / 2, zr, false, true); ctl.rearRightWheel = Make(p, car, "RR", p.trackRear / 2, zr, false, false);
        var cam = Camera.main.gameObject; cam.AddComponent<FollowCam>().target = car.transform;
        cam.AddComponent<DebugHUD>().car = ctl;
        var router = car.AddComponent<InputRouter>(); router.car = ctl; router.cam = cam.GetComponent<FollowCam>(); ctl.router = router;
        var ffb = car.AddComponent<FfbController>(); ffb.car = ctl; ffb.router = router; router.ffb = ffb;
        car.AddComponent<SmokeTest>().car = ctl;
        car.AddComponent<CarAudio>().car = ctl;
        var ck = car.AddComponent<Cockpit>(); ck.car = ctl;
        var vr = car.AddComponent<VrManager>(); vr.car = ctl; vr.cockpit = ck; vr.cam = cam.GetComponent<Camera>(); vr.router = router; router.vr = vr;
        var vp = car.AddComponent<VrPanel>(); vp.hud = cam.GetComponent<DebugHUD>(); vp.router = router; vp.cockpit = ck; router.panel = vp;
        var tm = car.AddComponent<TrackManager>(); tm.car = ctl; tm.testArea = testArea; tm.router = router;
        EditorSceneManager.SaveScene(scene, "Assets/TestTrack.unity"); AssetDatabase.SaveAssets();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/TestTrack.unity", true), new EditorBuildSettingsScene("Assets/Tracks/LagunaSeca/LagunaSeca.unity", true) };
        Debug.Log("Scirocco test scene built. Press Play.");
    }

    // Batch entry: Unity -batchmode -quit -projectPath . -executeMethod SciroccoSetup.BuildLinux
    public static void BuildLinux()
    {
        PlayerSettings.productName = "SciroccoSimDemo"; PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);   // drops unreachable .NET code (System.Data/Xml/...); Assets/link.xml keeps our assemblies whole
        AssetDatabase.DeleteAsset("Assets/SciroccoParams.asset"); AssetDatabase.DeleteAsset("Assets/SuspensionTables.asset"); AssetDatabase.DeleteAsset("Assets/SuspensionTablesProposed.asset");   // batch builds always regenerate params from CarParams.cs defaults
        XrSetup.Configure(false);
        Build();
        string outDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../builds/linux"));
        System.IO.Directory.CreateDirectory(outDir);
        var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/TestTrack.unity", "Assets/Tracks/LagunaSeca/LagunaSeca.unity" },
            locationPathName = System.IO.Path.Combine(outDir, "SciroccoSimDemo.x86_64"), target = BuildTarget.StandaloneLinux64, options = BuildOptions.Development });
        Debug.Log("BUILD RESULT: " + r.summary.result + " size=" + r.summary.totalSize + " errors=" + r.summary.totalErrors);
        if (r.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
    // Batch entry: -executeMethod SciroccoSetup.BuildWindows  (needs Windows Build Support (Mono) module). Release player, keyboard-playable.
    public static void BuildWindows()
    {
        PlayerSettings.productName = "SciroccoSimDemo"; PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);   // drops unreachable .NET code (System.Data/Xml/...); Assets/link.xml keeps our assemblies whole
        AssetDatabase.DeleteAsset("Assets/SciroccoParams.asset"); AssetDatabase.DeleteAsset("Assets/SuspensionTables.asset"); AssetDatabase.DeleteAsset("Assets/SuspensionTablesProposed.asset");
        XrSetup.Configure();
        Build();
        string outDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../builds/windows/SciroccoSimDemo"));
        System.IO.Directory.CreateDirectory(outDir);
        var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/TestTrack.unity", "Assets/Tracks/LagunaSeca/LagunaSeca.unity" },
            locationPathName = System.IO.Path.Combine(outDir, "SciroccoSimDemo.exe"), target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        Debug.Log("BUILD RESULT: " + r.summary.result + " size=" + r.summary.totalSize + " errors=" + r.summary.totalErrors);
        if (r.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
#endif
