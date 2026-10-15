// Track selection: the built-in flat test area (always present in the main scene) or an additively loaded track scene.
// Selected in the Quick menu (saved in the input profile) or with -track <name> on the command line.
using System.Collections; using UnityEngine; using UnityEngine.SceneManagement;
public class TrackManager : MonoBehaviour
{
    public static readonly string[] Names = { "Test area (flat)", "Laguna Seca" };
    public static readonly string[] Scenes = { "", "LagunaSeca" };
    public CarController car; public GameObject testArea; public InputRouter router;
    public int current { get; private set; }
    public bool Ready { get; private set; } = true; public bool Loading { get; private set; }
    public TrackInfo info { get; private set; }
    public string Credit => info ? info.credit : "";
    public static TrackManager Instance;
    Vector3 testSpawn; Quaternion testRot;
    // @description - Registers the singleton and remembers where the car starts on the flat test area so it can be put back there.
    // @return {Void} - nothing
    void Awake() { Instance = this; if (car) { testSpawn = car.transform.position; testRot = car.transform.rotation; } }
    // @description - Chooses the starting track from the -track command line flag, or from the track saved in the input profile, and loads it unless it is the flat test area.
    // @return {Void} - nothing
    void Start()
    {
        var arguments = System.Environment.GetCommandLineArgs(); int flagIndex = System.Array.IndexOf(arguments, "-track"); int want = -1;
        if (flagIndex >= 0 && flagIndex + 1 < arguments.Length) want = Find(arguments[flagIndex + 1]);
        else if (router && router.map != null) want = Find(router.map.profile.track);
        if (want > 0) Select(want);
    }
    // @description - Looks up a track index by name (display name prefix or scene name, ignoring spaces and case).
    // @param {string} name - track name to find
    // @return {int} - index into Names; 0 (the flat test area) if the name is empty or unknown
    public static int Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0; name = name.Replace(" ", "").ToLowerInvariant();
        for (int k = 0; k < Names.Length; k++) if (Names[k].Replace(" ", "").ToLowerInvariant().StartsWith(name) || Scenes[k].ToLowerInvariant() == name) return k;
        return 0;
    }
    // @description - Starts loading a track by index, ignored while another load is running or if the index is out of range.
    // @param {int} trackIndex - index into Names
    // @return {Void} - nothing
    public void Select(int trackIndex) { if (Loading || trackIndex < 0 || trackIndex >= Names.Length) return; StartCoroutine(Load(trackIndex)); }
    // @description - Coroutine that unloads the current track scene, additively loads the new one, finds its TrackInfo, drops the car at the spawn point (or back at the test area spawn), switches the flat test area on or off, extends the camera far plane and saves the choice in the profile.
    // @param {int} trackIndex - index into Names
    // @return {IEnumerator} - coroutine enumerator
    IEnumerator Load(int trackIndex)
    {
        Loading = true; Ready = false;
        if (current > 0) { var previousScene = SceneManager.GetSceneByName(Scenes[current]); if (previousScene.isLoaded) yield return SceneManager.UnloadSceneAsync(previousScene); info = null; }
        if (trackIndex > 0)
        {
            yield return SceneManager.LoadSceneAsync(Scenes[trackIndex], LoadSceneMode.Additive);
            var trackScene = SceneManager.GetSceneByName(Scenes[trackIndex]);
            foreach (var rootObject in trackScene.GetRootGameObjects()) { var trackInfo = rootObject.GetComponentInChildren<TrackInfo>(); if (trackInfo) { info = trackInfo; break; } }
            Physics.SyncTransforms();
            if (info) Place(info.spawnPosition, info.spawnYawDeg); else Debug.LogError("track scene has no TrackInfo: " + Scenes[trackIndex]);
            if (testArea) testArea.SetActive(false);
            var mainCamera = Camera.main; if (mainCamera) mainCamera.farClipPlane = Mathf.Max(mainCamera.farClipPlane, 4000f);
        }
        else { if (testArea) testArea.SetActive(true); Physics.SyncTransforms(); PlaceExact(testSpawn, testRot); }
        current = trackIndex; if (router && router.map != null) router.map.profile.track = trackIndex == 0 ? "" : Scenes[trackIndex];
        Debug.Log($"[Track] loaded '{Names[trackIndex]}'" + (info ? $" spawn {info.spawnPosition} yaw {info.spawnYawDeg}" : ""));
        Loading = false; Ready = true;
    }
    // @description - Puts the car on the ground at a spawn point. A ray finds the surface height and normal; the car is aligned to the slope (so a car dropped on a hill does not start inside it) with the given heading, and lifted by design hub drop plus wheel radius plus 2 cm.
    // @param {Vector3} position - spawn position in world metres (height is replaced by the ground height)
    // @param {float} yawDeg - heading in degrees
    // @return {Void} - nothing
    public void Place(Vector3 position, float yawDeg)
    {
        float dropHeight = car.carParams.hubDropDesign + car.carParams.wheelRadius + 0.02f;
        var up = Vector3.up;
        if (Physics.Raycast(position + Vector3.up * 30f, Vector3.down, out var hit, 600f)) { position.y = hit.point.y; up = hit.normal; } else Debug.LogWarning("[Track] no surface under spawn");
        // spawn aligned to the ground (pitch/roll = surface slope, heading = yawDeg): a level car dropped on a slope starts with its body inside the hill
        var rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, yawDeg, 0f);
        PlaceExact(position + up * dropHeight, rotation);
    }
    // @description - Teleports the car to an exact pose with zero velocity, resets the wheel states (no stored spin or slip), selects neutral and raises the teleported event.
    // @param {Vector3} pos - world position in metres
    // @param {Quaternion} rot - world rotation
    // @return {Void} - nothing
    void PlaceExact(Vector3 pos, Quaternion rot)
    {
        var body = car.GetComponent<Rigidbody>(); body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
        body.position = pos; body.rotation = rot; car.transform.SetPositionAndRotation(pos, rot); Physics.SyncTransforms();
        foreach (var wheel in new[] { car.frontLeftWheel, car.frontRightWheel, car.rearLeftWheel, car.rearRightWheel }) if (wheel) wheel.ResetState();   // no stored wheel spin / slip after a teleport
        if (car.drivetrain != null) car.drivetrain.SelectGear(0);
        CarController.RaiseTeleported(car);
    }
    // data credit: always on screen while a track that needs it is loaded (independent of the HUD toggle)
    GUIStyle creditStyle;
    // @description - Draws the data credit line (bottom right) whenever a loaded track requires it, independent of the HUD toggle.
    // @return {Void} - nothing
    void OnGUI()
    {
        if (string.IsNullOrEmpty(Credit) || VrPanel.Active) return;
        if (creditStyle == null) { creditStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.LowerRight }; creditStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f); }
        GUI.depth = -20; GUI.Label(new Rect(Screen.width - 610, Screen.height - 22, 600, 20), Credit, creditStyle);
    }
    // @description - Safety net: if the car falls more than 5 m below the lowest terrain sample it is logged, counted and respawned at the start.
    // @return {Void} - nothing
    void FixedUpdate()
    {
        if (!info || Loading || !car) return;
        if (BelowWorld(car.transform.position.y, info.minY)) { Debug.LogWarning($"[Track] car fell below terrain (y {car.transform.position.y:0.0} < {info.minY - 5f:0.0}) at {car.transform.position} -> reset to start/finish"); FallResets++; if (router && router.ffb) router.ffb.ResetRamp(); Respawn(); }
    }
    public int FallResets { get; private set; }
    // @description - Puts the car back at the current track's spawn point, or at the test area start if no track is loaded.
    // @return {Void} - nothing
    public void Respawn() { if (info) Place(info.spawnPosition, info.spawnYawDeg); else PlaceExact(testSpawn, testRot); }
    // @description - Fall safety test: true when the car is more than 5 m under the lowest terrain sample.
    // @param {float} carY - car height in metres
    // @param {float} terrainMinY - lowest terrain height in metres
    // @return {bool} - true if the car has fallen out of the world
    public static bool BelowWorld(float carY, float terrainMinY) => carY < terrainMinY - 5f;   // fall safety threshold: 5 m under the lowest DEM sample
    // @description - Finds the terrain height and steepness at a world position, using the finest terrain (smallest size) that covers the position.
    // @param {Vector3} worldPosition - world position in metres (x and z are used)
    // @param {float} steepnessDeg - receives the terrain steepness in degrees
    // @return {float} - terrain height in world metres, or NaN if no terrain covers the position
    public static float TerrainHeightAt(Vector3 worldPosition, out float steepnessDeg)
    {
        steepnessDeg = 0f; float best = float.MaxValue, y = float.NaN;
        foreach (var terrainCollider in FindObjectsOfType<TerrainCollider>())
        {
            var terrainData = terrainCollider.terrainData; if (!terrainData) continue; var origin = terrainCollider.transform.position; float fractionX = (worldPosition.x - origin.x) / terrainData.size.x, fractionZ = (worldPosition.z - origin.z) / terrainData.size.z;
            if (fractionX < 0f || fractionX > 1f || fractionZ < 0f || fractionZ > 1f || terrainData.size.x >= best) continue;
            best = terrainData.size.x; y = origin.y + terrainData.GetInterpolatedHeight(fractionX, fractionZ); steepnessDeg = terrainData.GetSteepness(fractionX, fractionZ);
        }
        return y;
    }
    TerrainCollider terrain; public TerrainCollider Terrain { get { if (!terrain) terrain = FindObjectOfType<TerrainCollider>(); return terrain; } }
}
