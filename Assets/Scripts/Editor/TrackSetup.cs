#if UNITY_EDITOR
// Imports Blender Bot's Laguna Seca export (Assets/Tracks/LagunaSeca) into its own additive scene and verifies axes/scale.
using System.Collections.Generic; using System.Globalization; using System.Linq; using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
/// raycast against all terrain tiles; highest hit
public struct MultiTC { public bool Raycast(Ray ray, out RaycastHit hit, float dist) { hit = default; bool got = false; foreach (var t in Object.FindObjectsOfType<TerrainCollider>()) if (t.Raycast(ray, out var h, dist) && (!got || h.distance < hit.distance)) { hit = h; got = true; } return got; } public static implicit operator bool(MultiTC m) => true; }
public static class TrackSetup
{
    const string Dir = "Assets/Tracks/LagunaSeca", Vis = Dir + "/LagunaSeca_Visual.fbx", Col = Dir + "/LagunaSeca_Colliders.fbx", ScenePath = Dir + "/LagunaSeca.unity";
    public const float SpawnYaw = 215.6f;
    // @description - Model.
    // @param {string} path - path
    // @param {bool} readable - readable
    // @return {Void} - nothing
    static void Model(string path, bool readable)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(path); if (mi == null) { Debug.LogError("TRACKCHECK missing " + path); return; }
        mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = true; mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false;
        mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None; mi.meshCompression = ModelImporterMeshCompression.Off; mi.isReadable = readable;
        mi.importTangents = ModelImporterTangents.None;   // Standard shader without normal maps: no tangents (build size)
        if (!readable) mi.meshCompression = ModelImporterMeshCompression.Low;   // visual only (positions quantised finely); road collider FBX stays uncompressed
        mi.addCollider = false; mi.indexFormat = ModelImporterIndexFormat.Auto; mi.importNormals = ModelImporterNormals.Import;
        if (readable) mi.importNormals = ModelImporterNormals.None;   // v1.3.1: collider-only FBX (135k barrier tris); PhysX uses triangle normals, vertex normals only cost build size
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; mi.materialLocation = ModelImporterMaterialLocation.External;
        mi.materialName = ModelImporterMaterialName.BasedOnMaterialName; mi.materialSearch = ModelImporterMaterialSearch.Local;
        mi.SaveAndReimport();
    }
    // @description - Tex.
    // @param {string} path - path
    // @return {Void} - nothing
    static void Tex(string path)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path); if (ti == null) return;
        bool aerial = path.Contains("Aerial");
        ti.textureType = TextureImporterType.Default; ti.mipmapEnabled = true; ti.wrapMode = aerial ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        ti.maxTextureSize = aerial ? 2048 : 1024;   // aerial 2048 (0.93 m/px) keeps the Windows .7z under 25,000,000 bytes ti.textureCompression = TextureImporterCompression.Compressed; ti.crunchedCompression = true; ti.compressionQuality = aerial ? 60 : 50;
        if (path.Contains("T_Building") || path.Contains("T_Wall_Concrete")) ti.maxTextureSize = 512;   // v1.3.1: 135k-tri closed barrier boxes pushed the .7z past 25 MB; 0.8 m walls / distant buildings
        ti.anisoLevel = aerial ? 4 : 8; ti.SaveAndReimport();
    }
    // @description - Build (see the body for details).
    // @return {Void} - nothing
    public static void Build()
    {
        AssetDatabase.Refresh();
        foreach (var t in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir + "/textures" })) Tex(AssetDatabase.GUIDToAssetPath(t));
        Model(Vis, false); Model(Col, true);
        PlayerSettings.bakeCollisionMeshes = true;   // MeshColliders cooked at build time (visual meshes stay non-readable)
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("LagunaSeca");
        var vis = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Vis)); vis.transform.SetParent(root.transform, false);
        var col = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Col)); col.transform.SetParent(root.transform, false);
        // v1.1: the FBX data is pre-rotated by Blender Bot (+X East, +Z North); no compensating rotation (verified below via Marker_North / S/F heading).
        int nRoad = 0, nTerr = 0; var counts = new Dictionary<SurfaceType, int>();
        // all v1.1 colliders (road, kerbs with bump profile, paved runoff, gravel, grass): non-convex MeshColliders, renderers off, tagged by surface class
        // v1.3.1: COL_Barriers_* are vertex-identical to the visual Walls_Barriers_* (Blender Bot verify_barriers: max |dv| 0.000000 m): the colliders go on the
        // visual meshes (same closed vertical boxes) and the duplicate COL copies are dropped from the build (.7z size)
        int dropB = 0; foreach (var mf in col.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith("COL_Barriers")).ToList()) { if (PrefabUtility.IsPartOfPrefabInstance(col)) PrefabUtility.UnpackPrefabInstance(col, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); Object.DestroyImmediate(mf.gameObject); dropB++; }
        Debug.Log($"TRACKCHECK dropped {dropB} COL_Barriers duplicates; wall colliders on visual Walls_Barriers meshes");
        foreach (var mf in col.GetComponentsInChildren<MeshFilter>())
        {
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; var r = mf.GetComponent<MeshRenderer>(); if (r) r.enabled = false; nRoad++;
            var st = SurfaceTag.FromName(mf.name); mf.gameObject.AddComponent<SurfaceTag>().type = st; counts[st] = counts.TryGetValue(st, out var k) ? k + 1 : 1;
        }
        // barriers: MeshColliders on the visual wall meshes (cooked at build time)
        // v1.3.1: Blender Bot ships COL_Barriers_* (closed vertical boxes, identical vertices); only fall back to visual wall meshes when absent
        foreach (var mf in vis.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith("Walls_Barriers")))
        { mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; mf.gameObject.AddComponent<SurfaceTag>().type = SurfaceType.Wall; counts[SurfaceType.Wall] = counts.TryGetValue(SurfaceType.Wall, out var k) ? k + 1 : 1; }
        Debug.Log("TRACKCHECK collider objects by surface: " + string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")));
        // Terrain collider (option B): Unity TerrainCollider from the v1.1 RAW513, which is the same surface as the Terrain_* meshes (carved by Blender Bot).
        // Kept as the ground beyond the ~16 m surface colliders and as a safety layer under them. MeshColliders on the 524k-tri terrain would add ~10 MB to the build.
        Physics.SyncTransforms(); nTerr = MakeTerrainCollider(root.transform);
        foreach (var r in vis.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = r.name.StartsWith("Terrain_") || r.name.StartsWith("Track_") ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        var info = new GameObject("TrackInfo").AddComponent<TrackInfo>(); info.transform.SetParent(root.transform, false);
        info.displayName = "WeatherTech Raceway Laguna Seca"; info.credit = "Track data © OpenStreetMap contributors (ODbL) · elevation USGS 3DEP · imagery USDA NAIP";
        info.spawnPosition = Vector3.zero; info.spawnYawDeg = SpawnYaw; info.minY = minYFromMeta; info.centrelineCsv = AssetDatabase.LoadAssetAtPath<TextAsset>(Dir + "/centreline.csv");
        var sf = vis.transform.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.StartsWith("Marker_StartFinish")); if (sf) info.spawnPosition = sf.position;
        if (sf) { // v1.2: centre the spawn between the measured COL_Road asphalt edges along the S/F line (the origin is the centreline point; edges are asymmetric there)
            var hw0 = info.HalfWidths.Count > 0 ? info.HalfWidths[0] : Vector2.zero; float yaw = SpawnYaw * Mathf.Deg2Rad; var fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)); var right = new Vector3(fwd.z, 0f, -fwd.x);
            Physics.SyncTransforms(); var sfl = vis.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name.StartsWith("Track_StartFinishLine"));
            float l = 0f, r = 0f, geoOff = (hw0.y - hw0.x) * 0.5f;
            for (int it = 0; it < 3; it++)
            {
                l = r = 0f; for (float d = 0f; d < 14f; d += 0.02f) { if (RoadAt(info.spawnPosition - right * d)) l = d; else break; } for (float d = 0f; d < 14f; d += 0.02f) { if (RoadAt(info.spawnPosition + right * d)) r = d; else break; }
                if (it < 2) info.spawnPosition += right * (r - l) * 0.5f;
            }
            float totalOff = Vector3.Dot(info.spawnPosition - sf.position, right), along = sfl ? Vector3.Dot(sfl.bounds.center - info.spawnPosition, fwd) : float.NaN;
            spawnCheck = $"spawn {info.spawnPosition.ToString("F2")}: asphalt (COL_Road) left {l:0.00} m / right {r:0.00} m (centre offset {(r - l) * 0.5f:+0.00;-0.00} m); spawn is {totalOff:+0.00;-0.00} m right of the origin marker (geo.pkl hl/hr at s=0 say {geoOff:+0.00;-0.00} m); Track_StartFinishLine centre {along:+0.00;-0.00} m along the heading from spawn";
        }
        Verify(vis, col, info, nRoad, nTerr);
        PrefabUtility.UnpackPrefabInstance(vis, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        int strip = 0; foreach (var t in vis.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name.StartsWith("Terrain_")).ToList()) { Object.DestroyImmediate(t.gameObject); strip++; }
        Debug.Log($"TRACKCHECK stripped {strip} Terrain_* mesh objects (ground rendered by the 2049 Unity Terrain)");
        EditorSceneManager.SaveScene(scene, ScenePath);
    }
    [System.Serializable] class HMeta { public float[] extent_m; public float height_range_m; public float[] unity_terrain_position; }
    // inner window on the 2049 grid (node indices; multiples of 4 so the borders sit on 513 nodes): covers the track bbox +40 m
    public const int C0 = 560, R0 = 332;
    // @description - Raw.
    // @param {string} f - value of type string
    // @param {int} n - value of type int
    // @return {ushort[]} - the raw
    static ushort[] Raw(string f, int n) { var b = System.IO.File.ReadAllBytes(Dir + "/" + f); if (b.Length != n * n * 2) throw new System.Exception("TRACKCHECK heightmap size " + f); var h = new ushort[n * n]; for (int i = 0; i < h.Length; i++) h[i] = (ushort)(b[2 * i] | b[2 * i + 1] << 8); return h; }
    // Explicit terrain material asset: with materialTemplate = null the Terrain falls back to the built-in default terrain material, whose
    // shader (Nature/Terrain/Standard) is not pulled into player builds -> the Terrain components draw nothing (only the road/kerb meshes show).
    public const string TerrainMatPath = Dir + "/Materials/M_Terrain_DEM.mat";
    // @description - Terrain material.
    // @return {Material} - the terrain material
    public static Material TerrainMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(TerrainMatPath); if (m) return m;
        m = new Material(Shader.Find("Nature/Terrain/Standard")) { name = "M_Terrain_DEM" }; AssetDatabase.CreateAsset(m, TerrainMatPath); return m;
    }
    [MenuItem("Scirocco/Assign Laguna Seca terrain material")]
    // @description - Assign terrain material.
    // @return {Void} - nothing
    public static void AssignTerrainMaterial()
    {
        var sc = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single); var m = TerrainMaterial(); int n = 0;
        foreach (var t in Object.FindObjectsOfType<Terrain>(true)) { t.materialTemplate = m; EditorUtility.SetDirty(t); n++; }
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(sc); AssetDatabase.SaveAssets(); Debug.Log($"TRACKCHECK terrain material {m.shader.name} assigned to {n} terrains");
    }
    // @description - Makes the terrain collider.
    // @param {Transform} parent - parent
    // @return {int} - the make terrain collider
    static int MakeTerrainCollider(Transform parent)
    {
        var meta = JsonUtility.FromJson<HMeta>(System.IO.File.ReadAllText(Dir + "/LagunaSeca_Heightmap_meta.json"));
        float size = meta.extent_m[0], height = meta.height_range_m, sp = size / 2048f; var pos = new Vector3(meta.unity_terrain_position[0], meta.unity_terrain_position[1], meta.unity_terrain_position[2]);
        var h2 = Raw("LagunaSeca_Heightmap_2049_16bit_LE.raw", 2049); var h5 = Raw("LagunaSeca_Heightmap_513_16bit_LE.raw", 513);   // row 0 = south, little-endian, already carved
        var aerial = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/textures/T_Terrain_Aerial_USGS_NAIP.png");
        foreach (var old in AssetDatabase.FindAssets("LagunaSeca_Terrain", new[] { Dir })) { var ap = AssetDatabase.GUIDToAssetPath(old); if (ap.EndsWith(".asset") || ap.EndsWith(".terrainlayer")) AssetDatabase.DeleteAsset(ap); }
        AssetDatabase.DeleteAsset(Dir + "/LagunaSeca_TerrainData.asset"); AssetDatabase.DeleteAsset(Dir + "/LagunaSeca_Aerial.terrainlayer");
        int made = 0;
        void Tile(string name, int n, float spacing, Vector3 at, System.Func<int, int, float> h01, int order)
        {
            var hh = new float[n, n]; for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) hh[r, c] = h01(r, c);
            var td = new TerrainData { heightmapResolution = n }; td.size = new Vector3((n - 1) * spacing, height, (n - 1) * spacing); td.SetHeights(0, 0, hh);
            var off = new Vector2(at.x - pos.x, at.z - pos.z);   // splat uv = (local + tileOffset) / tileSize -> the one aerial spans the full extent on every tile
            var layer = new TerrainLayer { diffuseTexture = aerial, tileSize = new Vector2(size, size), tileOffset = off, smoothness = 0f, metallic = 0f };
            AssetDatabase.CreateAsset(layer, $"{Dir}/LagunaSeca_Terrain_{name}.terrainlayer");
            td.alphamapResolution = 16; td.baseMapResolution = 16; td.terrainLayers = new[] { layer };
            AssetDatabase.CreateAsset(td, $"{Dir}/LagunaSeca_Terrain_{name}.asset");
            var go = new GameObject("TerrainCollider_DEM_" + name); go.transform.SetParent(parent, false); go.transform.position = at;
            go.AddComponent<TerrainCollider>().terrainData = td; go.AddComponent<SurfaceTag>().type = SurfaceType.Terrain;
            var tv = go.AddComponent<Terrain>(); tv.terrainData = td; tv.heightmapPixelError = 1f; tv.basemapDistance = 4000f; tv.drawInstanced = true; tv.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tv.drawTreesAndFoliage = false;
            tv.materialTemplate = TerrainMaterial(); tv.groupingID = order; made++;
            Debug.Log($"TRACKCHECK terrain tile {name}: {n}x{n} at {spacing:0.000} m, origin {at.ToString("F2")}, size {td.size.x:0.0} m");
        }
        System.Func<int, int, float> fine = (r, c) => h2[r * 2049 + c] / 65535f;
        // inner: A = 1025^2 at (R0, C0); B1/B2 = 513^2 above it (rows R0+1024..R0+1536), side by side. Exact 2049 nodes, no resampling.
        Tile("Inner_A", 1025, sp, pos + new Vector3(C0 * sp, 0f, R0 * sp), (r, c) => fine(R0 + r, C0 + c), 1);
        Tile("Inner_B1", 513, sp, pos + new Vector3(C0 * sp, 0f, (R0 + 1024) * sp), (r, c) => fine(R0 + 1024 + r, C0 + c), 1);
        Tile("Inner_B2", 513, sp, pos + new Vector3((C0 + 512) * sp, 0f, (R0 + 1024) * sp), (r, c) => fine(R0 + 1024 + r, C0 + 512 + c), 1);
        // outer: 513 grid; strictly inside the inner rectangle lowered 1.5 m (hidden), border nodes = 2049 value (crack-free seam)
        int q0 = C0 / 4, q1 = (C0 + 1024) / 4, p0 = R0 / 4, p1 = (R0 + 1536) / 4; float drop = 1.5f / height;
        Tile("Outer", 513, sp * 4f, pos, (r, c) =>
        {
            bool inC = c >= q0 && c <= q1, inR = r >= p0 && r <= p1;
            if (inC && inR) { bool border = c == q0 || c == q1 || r == p0 || r == p1; return border ? fine(r * 4, c * 4) : Mathf.Max(0f, h5[r * 513 + c] / 65535f - drop); }
            return h5[r * 513 + c] / 65535f;
        }, 0);
        Debug.Log($"TRACKCHECK terrain: inner 2049-grid window x {pos.x + C0 * sp:0.0}..{pos.x + (C0 + 1024) * sp:0.0}, z {pos.z + R0 * sp:0.0}..{pos.z + (R0 + 1536) * sp:0.0}; outer 513 elsewhere");
        minYFromMeta = pos.y; return made;
    }
    static float minYFromMeta = -92.13f; static string spawnCheck = "";
    // @description - Road at.
    // @param {Vector3} p - value of type Vector3
    // @return {bool} - the road at
    static bool RoadAt(Vector3 p) => Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var h, 20f) && h.collider.name.StartsWith("COL_Road");
    // @description - Verify.
    // @param {GameObject} vis - vis
    // @param {GameObject} col - col
    // @param {TrackInfo} info - info
    // @param {int} nRoad - n road
    // @param {int} nTerr - n terr
    // @return {Void} - nothing
    static void Verify(GameObject vis, GameObject col, TrackInfo info, int nRoad, int nTerr)
    {
        var inv = CultureInfo.InvariantCulture; var log = new List<string>(); void L(string s) { log.Add(s); }
        Physics.SyncTransforms();
        int nWall = vis.GetComponentsInChildren<MeshCollider>().Count(m => m.name.StartsWith("Walls_")); L($"collider objects (COL_*) {nRoad}, wall colliders {nWall}, terrain collider {nTerr}; root rotations vis {vis.transform.localEulerAngles} col {col.transform.localEulerAngles}");
        { // DEM collider vs Blender's visual terrain mesh away from the road (sanity: same DEM, should agree to a few cm)
            var tiles = vis.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith("Terrain_")).ToList(); float sum = 0f, mx = 0f; int n = 0;
            var tmp = new List<MeshCollider>(); foreach (var t in tiles) { var mc = t.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = t.sharedMesh; tmp.Add(mc); }
            Physics.SyncTransforms(); var tc = new MultiTC();
            for (float x = -600f; x < 1150f; x += 11f) for (float z = -1000f; z < 780f; z += 11f)
            {
                var ray = new Ray(new Vector3(x, 300f, z), Vector3.down);
                if (!tc.Raycast(ray, out var a, 600f)) continue; RaycastHit best = default; bool got = false;
                foreach (var mc in tmp) if (mc.Raycast(ray, out var b, 600f)) { best = b; got = true; break; }
                if (!got) continue; float d = a.point.y - best.point.y; sum += Mathf.Abs(d); mx = Mathf.Max(mx, Mathf.Abs(d)); n++;
            }
            foreach (var mc in tmp) Object.DestroyImmediate(mc); Physics.SyncTransforms();
            L($"terrain collider (RAW2049) vs Blender Terrain_* mesh (1025 grid, stripped from the scene after this check) at {n} grid points: mean |dy| {sum / Mathf.Max(1, n):0.000} m, max {mx:0.00} m");
        }
        { // each surface collider vs its visual mesh: collider-mesh vertices raycast onto a temporary MeshCollider of the matching visual mesh
            var tc = new MultiTC();
            foreach (var pr in new[] { ("COL_Road", "Track_Road"), ("COL_PitLane", "Track_PitLane"), ("COL_Kerbs", "Track_Kerbs"), ("COL_Runoff_Paved", "Track_Runoff_Paved"), ("COL_Gravel", "Track_Gravel"), ("COL_Grass", "Track_Grass") })
            {
                var vm = vis.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith(pr.Item2)).ToList(); var tmp = new List<MeshCollider>();
                foreach (var m in vm) { var go = new GameObject("tmp"); go.transform.SetPositionAndRotation(m.transform.position, m.transform.rotation); go.transform.localScale = m.transform.lossyScale; var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = m.sharedMesh; tmp.Add(mc); }
                Physics.SyncTransforms(); int n = 0, vmiss = 0; float sum = 0f, mx = 0f, clr = float.MaxValue;
                foreach (var cm in col.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith(pr.Item1)))
                {
                    var vs = cm.sharedMesh.vertices; for (int i = 0; i < vs.Length; i += 3)
                    {
                        var w = cm.transform.TransformPoint(vs[i]); var ray = new Ray(w + Vector3.up * 1.5f, Vector3.down); bool got = false; float best = float.MaxValue;
                        foreach (var mc in tmp) if (mc.Raycast(ray, out var hh, 3f)) { got = true; best = Mathf.Min(best, Mathf.Abs(hh.point.y - w.y)); }
                        if (got) { sum += best; mx = Mathf.Max(mx, best); n++; } else vmiss++;
                        if (tc && tc.Raycast(new Ray(w + Vector3.up * 50f, Vector3.down), out var th, 200f)) clr = Mathf.Min(clr, w.y - th.point.y);
                    }
                }
                foreach (var mc in tmp) Object.DestroyImmediate(mc.gameObject); Physics.SyncTransforms();
                L($"{pr.Item1} vs visual {pr.Item2} ({vm.Count} meshes): {n} vertices, mean |dy| {sum / Mathf.Max(1, n):0.000} m, max {mx:0.000} m, no visual within 1.5 m: {vmiss}; min height above terrain collider {clr:0.00} m");
            }
        }
        Bounds Bo(GameObject g, string prefix) { var rs = g.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith(prefix)).ToArray(); var b = rs.Length > 0 ? rs[0].bounds : new Bounds(); foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
        var tb = Bo(vis, "Terrain_"); var rb = Bo(col, "COL_Road");
        L($"terrain bounds min {tb.min.ToString("F1")} max {tb.max.ToString("F1")} (heightmap meta SW corner x=-675.0 z=-1090.2, size 1902.5, zmin -92.1)");
        L($"road collider bounds min {rb.min.ToString("F1")} max {rb.max.ToString("F1")}");
        var tr = vis.GetComponentsInChildren<Transform>(); var sf = tr.FirstOrDefault(t => t.name.StartsWith("Marker_StartFinish")); var mn = tr.FirstOrDefault(t => t.name.StartsWith("Marker_North"));
        if (sf) L($"Marker_StartFinish pos {sf.position.ToString("F2")} fwd(+Y local) {sf.up.ToString("F3")} fwd(+Z local) {sf.forward.ToString("F3")} (expected race dir (-0.583,0,-0.813)) heading {Mathf.Repeat(Mathf.Atan2(sf.forward.x, sf.forward.z) * Mathf.Rad2Deg, 360f):0.0} deg from +Z (expect 215.6)"); else L("Marker_StartFinish NOT FOUND");
        if (mn) L($"Marker_North pos {mn.position.ToString("F1")} (expect +Z)"); else L("Marker_North NOT FOUND");
        var scales = tr.Where(t => (t.localScale - Vector3.one).sqrMagnitude > 1e-6f || t.localRotation != Quaternion.identity).Select(t => $"{t.name} s={t.localScale} r={t.localEulerAngles}").Take(5).ToList();
        L("non-identity transforms: " + (scales.Count == 0 ? "none" : string.Join("; ", scales)));
        // centreline vs road collider
        var c = info.Centreline; float sumD = 0f, maxD = 0f; int miss = 0, hitsTerrain = 0; var hy = new float[c.Count];
        for (int i = 0; i < c.Count; i++)
        {
            if (Physics.Raycast(c[i] + Vector3.up * 20f, Vector3.down, out var h, 60f)) { hy[i] = h.point.y; float d = h.point.y - c[i].y; sumD += Mathf.Abs(d); maxD = Mathf.Max(maxD, Mathf.Abs(d)); if (!h.collider.name.StartsWith("COL_Road")) hitsTerrain++; }
            else { miss++; hy[i] = c[i].y; }
        }
        L($"centreline points {c.Count}: raycast misses {miss}, first hit not road collider {hitsTerrain} (bridges/terrain), |collider y - centreline z| mean {sumD / c.Count:0.000} max {maxD:0.000} m");
        L(spawnCheck);
        L($"lap length (centreline) {info.LapLength():0.0} m (official 3602)");
        float bestDrop = 0f; int at = 0; for (int i = 0; i < c.Count; i++) for (int k = 1; k <= 69; k++) { float dr = hy[i] - hy[(i + k) % c.Count]; if (dr > bestDrop) { bestDrop = dr; at = i; } }
        L($"max drop over <=138 m along race direction (raycast on collider) {bestDrop:0.0} m at s~{at * 2} m (Corkscrew ~2484-2621, official 18 m)");
        float emin = hy.Min(), emax = hy.Max(); L($"elevation range on road {emax - emin:0.0} m (official 55)");
        var hw = info.HalfWidths; float wmin = hw.Min(w => w.x + w.y), wmed = hw.Select(w => w.x + w.y).OrderBy(x => x).ElementAt(hw.Count / 2);
        L($"track width min {wmin:0.0} median {wmed:0.0} m vs car width 1.62 m / track 1.40 m -> width/car {wmed / 1.62f:0.0}x");
        // terrain must stay below the road under the road
        int terrAbove = 0; float worst = 0f;
        for (int i = 0; i < c.Count; i += 5)
        {
            var hits = Physics.RaycastAll(c[i] + Vector3.up * 20f, Vector3.down, 60f); float road = float.NaN, terr = float.NaN;
            foreach (var h in hits) { if (h.collider.name.StartsWith("COL_Road")) road = h.point.y; else if (h.collider is TerrainCollider) terr = Mathf.Max(float.IsNaN(terr) ? -1e9f : terr, h.point.y); }
            if (!float.IsNaN(road) && !float.IsNaN(terr) && terr > road - 0.02f) { terrAbove++; worst = Mathf.Max(worst, terr - road); }
        }
        L($"terrain above road collider at {terrAbove} of {c.Count / 5} sampled centreline points (worst {worst:0.00} m)");
        { // terrain layer UV vs Blender's aerial UV on Terrain_* vertices
            float x0 = -674.956f, z0 = -1090.236f, sz = 1902.4647f, eu = 0f; int nn = 0;
            foreach (var t in vis.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith("Terrain_")).Take(64)) { var vv = t.sharedMesh.vertices; var uu = t.sharedMesh.uv; if (uu.Length != vv.Length) continue;
                for (int i = 0; i < vv.Length; i += 97) { var w = t.transform.TransformPoint(vv[i]); var e = new Vector2((w.x - x0) / sz, (w.z - z0) / sz); eu = Mathf.Max(eu, (e - uu[i]).magnitude * sz); nn++; } }
            L($"aerial: Unity terrain-layer UV vs Blender Terrain_* UV at {nn} verts: max error {eu:0.00} m on the ground");
        }
        { // Corkscrew crest (s 2400-2520 m): down-rays across each asphalt edge (-0.3..+2.5 m) on the VISUAL surfaces; a ray whose first visual hit is the terrain = a gap (dark sliver)
            var vm = vis.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith("Track_") || m.name.StartsWith("Terrain_")).ToList(); var tmp = new List<MeshCollider>();
            foreach (var m in vm) { var go = new GameObject("tmp_" + m.name); go.transform.SetPositionAndRotation(m.transform.position, m.transform.rotation); go.transform.localScale = m.transform.lossyScale; var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = m.sharedMesh; tmp.Add(mc); }
            var tcv = new MultiTC(); Physics.SyncTransforms(); var cc = info.Centreline; var hw2 = info.HalfWidths;
            foreach (int side in new[] { -1, 1 })
            {
                int rays = 0, gaps = 0; float worstGap = 0f, minAbove = float.MaxValue; var where = new List<string>();
                for (int i = 1200; i <= 1260 && i < cc.Count - 1; i++)
                {
                    var fw = (cc[i + 1] - cc[i - 1]); fw.y = 0f; fw.Normalize(); var rt = new Vector3(fw.z, 0f, -fw.x); float he = side < 0 ? hw2[i].x : hw2[i].y;
                    for (float d = -0.3f; d <= 2.5f; d += 0.05f)
                    {
                        var o = cc[i] + rt * side * (he + d); var ray = new Ray(new Vector3(o.x, cc[i].y + 30f, o.z), Vector3.down); rays++;
                        float best = float.MaxValue; string nm = ""; foreach (var mc in tmp) if (mc.Raycast(ray, out var hh, 80f) && hh.distance < best) { best = hh.distance; nm = mc.name; }
                        bool terrFirst = nm.StartsWith("tmp_Terrain_"); float ty = float.NaN; if (tcv && tcv.Raycast(ray, out var th, 80f)) ty = th.point.y;
                        if (!terrFirst && nm.Length > 0 && !float.IsNaN(ty)) minAbove = Mathf.Min(minAbove, (ray.origin.y - best) - ty);   // visual surface height above the 2049 Unity terrain
                        if (terrFirst || nm.Length == 0 || (!float.IsNaN(ty) && ty > ray.origin.y - best + 0.002f)) { gaps++; float gz = float.IsNaN(ty) ? 0f : ty - (ray.origin.y - best); worstGap = Mathf.Max(worstGap, gz); if (where.Count < 6) where.Add($"s{i * 2}/{d:+0.00;-0.00}m:{(nm.Length == 0 ? "none" : nm.Substring(4))}"); }
                    }
                }
                L($"Corkscrew crest s2400-2520 {(side < 0 ? "left" : "right")} edge: {rays} rays, gaps/terrain-on-top {gaps} (worst terrain above visual {worstGap:0.000} m), min visual surface above 2049 terrain {minAbove:0.000} m {string.Join(" ", where)}");
            }
            foreach (var mc in tmp) Object.DestroyImmediate(mc.gameObject); Physics.SyncTransforms();
        }
        // materials / textures
        var mats = vis.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToList();
        L("materials: " + string.Join(", ", mats.Select(m => $"{m.name}[{(m.mainTexture ? m.mainTexture.name : "NO TEXTURE")}]")));
        int maxV = vis.GetComponentsInChildren<MeshFilter>().Max(m => m.sharedMesh ? m.sharedMesh.vertexCount : 0); int tris = vis.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh ? (int)(m.sharedMesh.triangles.Length / 3) : 0);
        L($"visual meshes {vis.GetComponentsInChildren<MeshFilter>().Length}, max verts/mesh {maxV}, total tris {tris}");
        foreach (var s in log) Debug.Log("TRACKCHECK " + s);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(Application.dataPath, "../../../builds/laguna_import_check.txt"), log);
    }
}
#endif
