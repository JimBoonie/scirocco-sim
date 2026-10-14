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
    void Awake() { Instance = this; if (car) { testSpawn = car.transform.position; testRot = car.transform.rotation; } }
    void Start()
    {
        var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, "-track"); int want = -1;
        if (i >= 0 && i + 1 < a.Length) want = Find(a[i + 1]);
        else if (router && router.map != null) want = Find(router.map.profile.track);
        if (want > 0) Select(want);
    }
    public static int Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0; name = name.Replace(" ", "").ToLowerInvariant();
        for (int k = 0; k < Names.Length; k++) if (Names[k].Replace(" ", "").ToLowerInvariant().StartsWith(name) || Scenes[k].ToLowerInvariant() == name) return k;
        return 0;
    }
    public void Select(int idx) { if (Loading || idx < 0 || idx >= Names.Length) return; StartCoroutine(Load(idx)); }
    IEnumerator Load(int idx)
    {
        Loading = true; Ready = false;
        if (current > 0) { var old = SceneManager.GetSceneByName(Scenes[current]); if (old.isLoaded) yield return SceneManager.UnloadSceneAsync(old); info = null; }
        if (idx > 0)
        {
            yield return SceneManager.LoadSceneAsync(Scenes[idx], LoadSceneMode.Additive);
            var sc = SceneManager.GetSceneByName(Scenes[idx]);
            foreach (var go in sc.GetRootGameObjects()) { var ti = go.GetComponentInChildren<TrackInfo>(); if (ti) { info = ti; break; } }
            Physics.SyncTransforms();
            if (info) Place(info.spawnPosition, info.spawnYawDeg); else Debug.LogError("track scene has no TrackInfo: " + Scenes[idx]);
            if (testArea) testArea.SetActive(false);
            var cam = Camera.main; if (cam) cam.farClipPlane = Mathf.Max(cam.farClipPlane, 4000f);
        }
        else { if (testArea) testArea.SetActive(true); Physics.SyncTransforms(); PlaceExact(testSpawn, testRot); }
        current = idx; if (router && router.map != null) router.map.profile.track = idx == 0 ? "" : Scenes[idx];
        Debug.Log($"[Track] loaded '{Names[idx]}'" + (info ? $" spawn {info.spawnPosition} yaw {info.spawnYawDeg}" : ""));
        Loading = false; Ready = true;
    }
    /// put the car at rest on the surface below p (road collider or terrain), facing yawDeg
    public void Place(Vector3 p, float yawDeg)
    {
        float h = car.p.hubDropDesign + car.p.wheelRadius + 0.02f;
        var up = Vector3.up;
        if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 600f)) { p.y = hit.point.y; up = hit.normal; } else Debug.LogWarning("[Track] no surface under spawn");
        // spawn aligned to the ground (pitch/roll = surface slope, heading = yawDeg): a level car dropped on a slope starts with its body inside the hill
        var rot = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, yawDeg, 0f);
        PlaceExact(p + up * h, rot);
    }
    void PlaceExact(Vector3 pos, Quaternion rot)
    {
        var rb = car.GetComponent<Rigidbody>(); rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        rb.position = pos; rb.rotation = rot; car.transform.SetPositionAndRotation(pos, rot); Physics.SyncTransforms();
        foreach (var w in new[] { car.FL, car.FR, car.RL, car.RR }) if (w) w.ResetState();   // no stored wheel spin / slip after a teleport
        if (car.dt != null) car.dt.SelectGear(0);
        CarController.RaiseTeleported(car);
    }
    // data credit: always on screen while a track that needs it is loaded (independent of the HUD toggle)
    GUIStyle cs;
    void OnGUI()
    {
        if (string.IsNullOrEmpty(Credit) || VrPanel.Active) return;
        if (cs == null) { cs = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.LowerRight }; cs.normal.textColor = new Color(1f, 1f, 1f, 0.85f); }
        GUI.depth = -20; GUI.Label(new Rect(Screen.width - 610, Screen.height - 22, 600, 20), Credit, cs);
    }
    // safety net: below the lowest terrain point = fell out of the world -> back to start/finish (logged)
    void FixedUpdate()
    {
        if (!info || Loading || !car) return;
        if (BelowWorld(car.transform.position.y, info.minY)) { Debug.LogWarning($"[Track] car fell below terrain (y {car.transform.position.y:0.0} < {info.minY - 5f:0.0}) at {car.transform.position} -> reset to start/finish"); FallResets++; if (router && router.ffb) router.ffb.ResetRamp(); Respawn(); }
    }
    public int FallResets { get; private set; }
    /// respawn on the current track's start/finish (or test area origin)
    public void Respawn() { if (info) Place(info.spawnPosition, info.spawnYawDeg); else PlaceExact(testSpawn, testRot); }
    public static bool BelowWorld(float carY, float terrainMinY) => carY < terrainMinY - 5f;   // fall safety threshold: 5 m under the lowest DEM sample
    /// DEM height under p from the finest terrain tile containing it (v1.3 tiles: 2049-grid inner + 513 outer); NaN if none
    public static float TerrainHeightAt(Vector3 p, out float steep)
    {
        steep = 0f; float best = float.MaxValue, y = float.NaN;
        foreach (var tc in FindObjectsOfType<TerrainCollider>())
        {
            var td = tc.terrainData; if (!td) continue; var o = tc.transform.position; float u = (p.x - o.x) / td.size.x, v = (p.z - o.z) / td.size.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f || td.size.x >= best) continue;
            best = td.size.x; y = o.y + td.GetInterpolatedHeight(u, v); steep = td.GetSteepness(u, v);
        }
        return y;
    }
    TerrainCollider terrain; public TerrainCollider Terrain { get { if (!terrain) terrain = FindObjectOfType<TerrainCollider>(); return terrain; } }
}
