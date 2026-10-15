#!/usr/bin/env python3
# @description - Laguna Seca v1.3 import (2026-10-12 PT): new FBX/RAW data, 2049 Unity Terrain (collider + visual), Terrain_* meshes stripped.
# @param {String} arguments - command-line arguments (see the script body)
# @return {Int} - process exit status (0 = success)
# Laguna Seca v1.3 import (2026-10-12 23:xx PT): new FBX/RAWs, 2049 Unity Terrain (collider + visual), Terrain_* meshes stripped from the scene, Corkscrew edge-gap check.
import os, shutil, pickle, numpy as np
P='/workspace/scirocco_sim/unity/SciroccoSimDemo'; S='/workspace/tracks/laguna_seca/output'; A=P+'/Assets/Tracks/LagunaSeca'; R=P+'/Assets/Scripts'
for f in ['LagunaSeca_Visual.fbx','LagunaSeca_Colliders.fbx','LagunaSeca_Heightmap_2049_16bit_LE.raw','LagunaSeca_Heightmap_meta.json']: shutil.copy(f'{S}/{f}', f'{A}/{f}')
for f in ['LagunaSeca_Heightmap_513_16bit_LE.raw','LagunaSeca_Heightmap_513_16bit_LE.raw.meta']:
    if os.path.exists(f'{A}/{f}'): os.remove(f'{A}/{f}')
for f in os.listdir(S+'/textures'): shutil.copy(f'{S}/textures/{f}', f'{A}/textures/{f}')
g=pickle.load(open(S+'/geo.pkl','rb')); c=np.array(g['centre']); hl=np.array(g['hl']); hr=np.array(g['hr'])
with open(A+'/centreline.csv','w') as f:
    f.write('# E_m,N_m,z_m,half_left_m,half_right_m  (Blender Bot v1.3 geo.pkl centre/hl/hr: re-measured asphalt edges; s=0 at S/F, 2 m steps, race direction)\n')
    for (x,y,z),l,r in zip(c,hl,hr): f.write(f'{-x:.3f},{-y:.3f},{z:.3f},{l:.2f},{r:.2f}\n')
p=R+'/Editor/TrackSetup.cs'; s=open(p).read()
def ed(a,b):
    global s
    if b in s: return
    if a not in s: raise SystemExit('MISSING: '+a[:100])
    s=s.replace(a,b,1)
ed('const int N = 513;','const int N = 2049;')
ed('"/LagunaSeca_Heightmap_513_16bit_LE.raw"','"/LagunaSeca_Heightmap_2049_16bit_LE.raw"')
ed('''go.AddComponent<TerrainCollider>().terrainData = td; go.AddComponent<SurfaceTag>().type = SurfaceType.Terrain;''','''go.AddComponent<TerrainCollider>().terrainData = td; go.AddComponent<SurfaceTag>().type = SurfaceType.Terrain;
        // v1.3: the same 2049 TerrainData also renders the ground (Blender Bot's option B). Replaces the 2.1M-tri Terrain_* meshes (1025 grid), which would push the .7z over 25 MB.
        var aerial = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/textures/T_Terrain_Aerial_USGS_NAIP.png");
        var layer = new TerrainLayer { diffuseTexture = aerial, tileSize = new Vector2(size, size), tileOffset = Vector2.zero, smoothness = 0f, metallic = 0f };   // one layer over the full 1902.46 m extent; v=0 = south row
        AssetDatabase.DeleteAsset(Dir + "/LagunaSeca_Aerial.terrainlayer"); AssetDatabase.CreateAsset(layer, Dir + "/LagunaSeca_Aerial.terrainlayer");
        td.alphamapResolution = 16; td.baseMapResolution = 2048; td.terrainLayers = new[] { layer };
        var tv = go.AddComponent<Terrain>(); tv.terrainData = td; tv.heightmapPixelError = 1f; tv.basemapDistance = 4000f; tv.drawInstanced = true; tv.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tv.drawTreesAndFoliage = false;
        EditorUtility.SetDirty(td);''')
ed('TRACKCHECK terrain collider RAW513','TRACKCHECK terrain collider + visual RAW2049')
ed('terrain collider (RAW513) vs Terrain_* visual mesh','terrain collider (RAW2049) vs Blender Terrain_* mesh (1025 grid, stripped from the scene after this check)')
# order: verify (needs Terrain_* for comparison), then strip Terrain_*, then save
ed('''        EditorSceneManager.SaveScene(scene, ScenePath);
        Verify(vis, col, info, nRoad, nTerr);''','''        Verify(vis, col, info, nRoad, nTerr);
        PrefabUtility.UnpackPrefabInstance(vis, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        int strip = 0; foreach (var t in vis.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name.StartsWith("Terrain_")).ToList()) { Object.DestroyImmediate(t.gameObject); strip++; }
        Debug.Log($"TRACKCHECK stripped {strip} Terrain_* mesh objects (ground rendered by the 2049 Unity Terrain)");
        EditorSceneManager.SaveScene(scene, ScenePath);''')
# aerial UV alignment + Corkscrew crest edge-gap check, inserted before the materials line
ed('''        // materials / textures''','''        { // terrain layer UV vs Blender's aerial UV on Terrain_* vertices
            float x0 = -674.956f, z0 = -1090.236f, sz = 1902.4647f, eu = 0f; int nn = 0;
            foreach (var t in vis.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith("Terrain_")).Take(64)) { var vv = t.sharedMesh.vertices; var uu = t.sharedMesh.uv; if (uu.Length != vv.Length) continue;
                for (int i = 0; i < vv.Length; i += 97) { var w = t.transform.TransformPoint(vv[i]); var e = new Vector2((w.x - x0) / sz, (w.z - z0) / sz); eu = Mathf.Max(eu, (e - uu[i]).magnitude * sz); nn++; } }
            L($"aerial: Unity terrain-layer UV vs Blender Terrain_* UV at {nn} verts: max error {eu:0.00} m on the ground");
        }
        { // Corkscrew crest (s 2400-2520 m): down-rays across each asphalt edge (-0.3..+2.5 m) on the VISUAL surfaces; a ray whose first visual hit is the terrain = a gap (dark sliver)
            var vm = vis.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith("Track_") || m.name.StartsWith("Terrain_")).ToList(); var tmp = new List<MeshCollider>();
            foreach (var m in vm) { var go = new GameObject("tmp_" + m.name); go.transform.SetPositionAndRotation(m.transform.position, m.transform.rotation); go.transform.localScale = m.transform.lossyScale; var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = m.sharedMesh; tmp.Add(mc); }
            var tcv = Object.FindObjectOfType<TerrainCollider>(); Physics.SyncTransforms(); var cc = info.Centreline; var hw2 = info.HalfWidths;
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
        // materials / textures''')
open(p,'w').write(s)
# spawn: the v1.2 COL_Road-centring code now centres on the v1.3 asphalt (expected +0.25 m right); nothing else to change.
print('v13 applied')
