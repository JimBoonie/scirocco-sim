#!/usr/bin/env python3
# @description - v1.3b: tiled terrain to keep the Windows .7z < 25,000,000 bytes. Inner tiles = exact node subsets of the 2049 RAW (0.93 m) over the track
# @param {String} arguments - command-line arguments (see the script body)
# @return {Int} - process exit status (0 = success)
# v1.3b: tiled terrain to keep the Windows .7z < 25,000,000 bytes. Inner tiles = exact node subsets of the 2049 RAW (0.93 m) over the track
# area; outer = 513 RAW over the full 1.9 km, lowered 1.5 m under the inner rectangle, border nodes set to the 2049 values.
R='/workspace/scirocco_sim/unity/SciroccoSimDemo/Assets/Scripts'
def edit(p,a,b):
    s=open(p).read()
    if b in s: return
    if a not in s: raise SystemExit('MISSING in '+p+': '+a[:100])
    open(p,'w').write(s.replace(a,b,1))
p=R+'/Editor/TrackSetup.cs'; s=open(p).read()
i=s.index('    static int MakeTerrainCollider(Transform parent)'); j=s.index('    static float minYFromMeta')
s=s[:i]+'''    // inner window on the 2049 grid (node indices; multiples of 4 so the borders sit on 513 nodes): covers the track bbox +40 m
    public const int C0 = 560, R0 = 332;
    static ushort[] Raw(string f, int n) { var b = System.IO.File.ReadAllBytes(Dir + "/" + f); if (b.Length != n * n * 2) throw new System.Exception("TRACKCHECK heightmap size " + f); var h = new ushort[n * n]; for (int i = 0; i < h.Length; i++) h[i] = (ushort)(b[2 * i] | b[2 * i + 1] << 8); return h; }
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
            tv.groupingID = order; made++;
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
'''+s[j:]
s=s.replace('Object.FindObjectOfType<TerrainCollider>()','new MultiTC()')
s=s.replace('public static class TrackSetup\n{','''/// raycast against all terrain tiles; highest hit
public struct MultiTC { public bool Raycast(Ray ray, out RaycastHit hit, float dist) { hit = default; bool got = false; foreach (var t in Object.FindObjectsOfType<TerrainCollider>()) if (t.Raycast(ray, out var h, dist) && (!got || h.distance < hit.distance)) { hit = h; got = true; } return got; } public static implicit operator bool(MultiTC m) => true; }
public static class TrackSetup
{''',1)
open(p,'w').write(s)
edit(R+'/Runtime/TrackManager.cs','    TerrainCollider terrain; public TerrainCollider Terrain','''    /// DEM height under p from the finest terrain tile containing it (v1.3 tiles: 2049-grid inner + 513 outer); NaN if none
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
    TerrainCollider terrain; public TerrainCollider Terrain''')
p=R+'/Runtime/SmokeTest.cs'
edit(p,'if (tc) { var td = tc.terrainData; var o = tc.transform.position; hTerr = o.y + td.GetInterpolatedHeight((p.x - o.x) / td.size.x, (p.z - o.z) / td.size.z); slope = td.GetSteepness((p.x - o.x) / td.size.x, (p.z - o.z) / td.size.z); }','if (tc) hTerr = TrackManager.TerrainHeightAt(p, out slope);')
edit(p,'var tc = tm.Terrain; if (tc) { var td = tc.terrainData; var o = tc.transform.position; float hT = o.y + td.GetInterpolatedHeight((p.x - o.x) / td.size.x, (p.z - o.z) / td.size.z); oMinAboveDem','{ float hT = TrackManager.TerrainHeightAt(p, out _); if (!float.IsNaN(hT)) oMinAboveDem')
print('ok')
