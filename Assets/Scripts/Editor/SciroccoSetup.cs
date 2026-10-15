#if UNITY_EDITOR
using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
public static class SciroccoSetup
{
    // @description - Creates one wheel corner object under the car at the given position (layer IgnoreRaycast so suspension rays skip it), connects the parameters and adds a cylinder as visual wheel sized from the wheel radius and a 0.0925 m tire width.
    // @param {CarParams} carParams - car parameter set shared by all corners
    // @param {GameObject} car - car root game object the wheel is attached to
    // @param {string} objectName - name of the wheel object ("FL", "FR", "RL" or "RR")
    // @param {float} lateralPosition - lateral position in car-local meters (negative = left)
    // @param {float} longitudinalPosition - longitudinal position in car-local meters (positive = forward)
    // @param {bool} isFront - true for a front wheel
    // @param {bool} isLeft - true for a left wheel
    // @return {WheelCorner} - the new wheel corner component
    static WheelCorner Make(CarParams carParams, GameObject car, string objectName, float lateralPosition, float longitudinalPosition, bool isFront, bool isLeft)
        {
            var wheelObject = new GameObject(objectName); wheelObject.transform.SetParent(car.transform, false); wheelObject.transform.localPosition = new Vector3(lateralPosition, 0f, longitudinalPosition); wheelObject.layer = 2;
            var wheel = wheelObject.AddComponent<WheelCorner>(); wheel.carParams = carParams; wheel.front = isFront; wheel.left = isLeft;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(car.transform, false); visual.transform.localScale = new Vector3(carParams.wheelRadius * 2, 0.0925f, carParams.wheelRadius * 2); visual.name = objectName + "_vis";
            wheel.visual = visual.transform; return wheel;
        }
    [MenuItem("Scirocco/Build Test Scene")]
    // @description - Menu entry "Scirocco > Build Test Scene": builds the Laguna Seca scene, then the flat test scene: parameter and suspension table assets (always refreshed from the generated data), a 10 km ground plane with cones, the car with body and chassis colliders sized from the sourced dimensions, the four wheel corners and all runtime components (camera, HUD, input router, force feedback, audio, cockpit, VR, track manager), then saves the scene and the build settings. Physics runs at 500 Hz (0.002 s step).
    // @return {Void} - nothing
    public static void Build()
    {
        Time.fixedDeltaTime = 0.002f; // 500 Hz
        TrackSetup.Build();   // Laguna Seca additive scene (its own scene file; built first because NewScene below replaces the open scene)
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        const string path = "Assets/SciroccoParams.asset";
        var carParams = AssetDatabase.LoadAssetAtPath<CarParams>(path);
        if (!carParams) { carParams = ScriptableObject.CreateInstance<CarParams>(); AssetDatabase.CreateAsset(carParams, path); }
        const string kpath = "Assets/SuspensionTables.asset";
        var suspensionTables = AssetDatabase.LoadAssetAtPath<SuspensionTables>(kpath);
        if (!suspensionTables) { suspensionTables = ScriptableObject.CreateInstance<SuspensionTables>(); AssetDatabase.CreateAsset(suspensionTables, kpath); }
        SuspensionTablesData.Fill(suspensionTables); EditorUtility.SetDirty(suspensionTables);   // always refresh from generated data
        carParams.suspensionTables = suspensionTables;
        EditorUtility.SetDirty(carParams);
        AssetDatabase.DeleteAsset("Assets/SuspensionTablesProposed.asset");   // single handling baseline (2026-10-13): the second table set is gone
        // flat track: 2km plane + cones
        var testArea = new GameObject("TestArea");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Track"; ground.transform.SetParent(testArea.transform, false); ground.transform.localScale = new Vector3(1000, 1, 1000); // 10 km x 10 km
        for (int coneIndex = 0; coneIndex < 20; coneIndex++) { var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cone.transform.position = new Vector3((coneIndex % 2) * 4 - 2, 0.3f, 30 + coneIndex * 15); cone.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f); cone.transform.SetParent(testArea.transform, true); Object.DestroyImmediate(cone.GetComponent<Collider>()); }
        var car = new GameObject("Scirocco"); car.transform.position = new Vector3(0, carParams.hubDropDesign + carParams.wheelRadius + 0.02f, 0);
        var carBody = car.AddComponent<Rigidbody>(); carBody.interpolation = RigidbodyInterpolation.Interpolate;
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "Body"; body.transform.SetParent(car.transform, false);
        body.transform.localScale = new Vector3(1.62f, 0.75f, 4.05f); body.transform.localPosition = new Vector3(0, 0.25f, 0);
        { // v1.3b: chassis colliders from sourced body dimensions (L 4.050 / W 1.625 / H 1.280 m S, VW; ground clearance 0.125 m S). The old single box
          // (0.46-1.21 m above ground) left the bottom 0.46 m open: a bounce of ~0.2 m put its lower edge above an 0.8 m barrier, which the car then vaulted.
            Object.DestroyImmediate(body.GetComponent<Collider>());
            float groundLocalHeight = -(carParams.hubDropDesign + carParams.wheelRadius);   // ground in car-local y at design ride height
            const float sill = 0.20f, belt = 0.85f, roof = 1.28f;   // m above ground: bumper/sill underside (E, above the 0.125 m S lowest underbody point), belt line (E), roof (S)
            var lowerChassis = new GameObject("ChassisLower"); lowerChassis.layer = 2; lowerChassis.transform.SetParent(car.transform, false);   // IgnoreRaycast like the old body: suspension/probe rays skip the own chassis
            var lowerCollider = lowerChassis.AddComponent<BoxCollider>(); lowerCollider.center = new Vector3(0f, groundLocalHeight + 0.5f * (sill + belt), 0f); lowerCollider.size = new Vector3(1.625f, belt - sill, 4.05f);
            var cabin = new GameObject("ChassisCabin"); cabin.layer = 2; cabin.transform.SetParent(car.transform, false); var cabinCollider = cabin.AddComponent<BoxCollider>(); cabinCollider.center = new Vector3(0f, groundLocalHeight + 0.5f * (belt + roof), -0.15f); cabinCollider.size = new Vector3(1.36f, roof - belt, 2.2f);   // greenhouse width/length (E)
        }
        body.layer = 2; // IgnoreRaycast so suspension rays skip own body
        var carController = car.AddComponent<CarController>(); carController.carParams = carParams;
        float frontAxleZ = carParams.wheelbase / 2, rearAxleZ = -carParams.wheelbase / 2;
        carController.frontLeftWheel = Make(carParams, car, "FL", -carParams.trackFront / 2, frontAxleZ, true, true); carController.frontRightWheel = Make(carParams, car, "FR", carParams.trackFront / 2, frontAxleZ, true, false);
        carController.rearLeftWheel = Make(carParams, car, "RL", -carParams.trackRear / 2, rearAxleZ, false, true); carController.rearRightWheel = Make(carParams, car, "RR", carParams.trackRear / 2, rearAxleZ, false, false);
        var cameraObject = Camera.main.gameObject; cameraObject.AddComponent<FollowCam>().target = car.transform;
        cameraObject.AddComponent<DebugHUD>().car = carController;
        var router = car.AddComponent<InputRouter>(); router.car = carController; router.cam = cameraObject.GetComponent<FollowCam>(); carController.router = router;
        var forceFeedback = car.AddComponent<FfbController>(); forceFeedback.car = carController; forceFeedback.router = router; router.ffb = forceFeedback;
        car.AddComponent<SmokeTest>().car = carController;
        car.AddComponent<CarAudio>().car = carController;
        var cockpit = car.AddComponent<Cockpit>(); cockpit.car = carController;
        var vrManager = car.AddComponent<VrManager>(); vrManager.car = carController; vrManager.cockpit = cockpit; vrManager.cam = cameraObject.GetComponent<Camera>(); vrManager.router = router; router.vrManager = vrManager;
        var vrPanel = car.AddComponent<VrPanel>(); vrPanel.hud = cameraObject.GetComponent<DebugHUD>(); vrPanel.router = router; vrPanel.cockpit = cockpit; router.panel = vrPanel;
        var trackManager = car.AddComponent<TrackManager>(); trackManager.car = carController; trackManager.testArea = testArea; trackManager.router = router;
        EditorSceneManager.SaveScene(scene, "Assets/TestTrack.unity"); AssetDatabase.SaveAssets();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/TestTrack.unity", true), new EditorBuildSettingsScene("Assets/Tracks/LagunaSeca/LagunaSeca.unity", true) };
        Debug.Log("Scirocco test scene built. Press Play.");
    }

    // @description - Batch entry point for the Linux player build (Unity -batchmode -quit -executeMethod SciroccoSetup.BuildLinux): sets player settings, deletes the generated assets so they are regenerated, removes the OpenXR loader (Windows only), builds the scene and writes the player to the output folder. Exits with code 1 if the build fails.
    // @return {Void} - nothing
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
        var buildReport = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/TestTrack.unity", "Assets/Tracks/LagunaSeca/LagunaSeca.unity" },
            locationPathName = System.IO.Path.Combine(outDir, "SciroccoSimDemo.x86_64"), target = BuildTarget.StandaloneLinux64, options = BuildOptions.Development });
        Debug.Log("BUILD RESULT: " + buildReport.summary.result + " size=" + buildReport.summary.totalSize + " errors=" + buildReport.summary.totalErrors);
        if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
    // @description - Batch entry point for the Windows player build: same as BuildLinux but assigns the OpenXR loader and builds SciroccoSimDemo.exe for 64-bit Windows.
    // @return {Void} - nothing
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
        var buildReport = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/TestTrack.unity", "Assets/Tracks/LagunaSeca/LagunaSeca.unity" },
            locationPathName = System.IO.Path.Combine(outDir, "SciroccoSimDemo.exe"), target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        Debug.Log("BUILD RESULT: " + buildReport.summary.result + " size=" + buildReport.summary.totalSize + " errors=" + buildReport.summary.totalErrors);
        if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
#endif
