#if UNITY_EDITOR
// Imports Blender Bot's Laguna Seca export (Assets/Tracks/LagunaSeca) into its own additive scene and verifies axes/scale.
using System.Collections.Generic; using System.Globalization; using System.Linq; using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
/// Raycast against all terrain tiles at once and return the closest hit (the track is built from several terrain tiles).
public struct MultiTerrainRaycaster
{
    // @description - Casts a ray against every TerrainCollider in the scene and returns the nearest hit.
    // @param {Ray} ray - ray to cast, in world space
    // @param {RaycastHit} hit - receives the nearest hit (default if nothing is hit)
    // @param {float} distance - maximum ray length in metres
    // @return {bool} - true if any terrain tile was hit
    public bool Raycast(Ray ray, out RaycastHit hit, float distance)
    {
        hit = default; bool foundHit = false;
        foreach (var terrainCollider in Object.FindObjectsOfType<TerrainCollider>())
            if (terrainCollider.Raycast(ray, out var tileHit, distance) && (!foundHit || tileHit.distance < hit.distance)) { hit = tileHit; foundHit = true; }
        return foundHit;
    }
    // @description - Lets a raycaster be used in "if (raycaster && ...)" conditions: it is always valid because it is a stateless struct.
    // @param {MultiTerrainRaycaster} raycaster - the raycaster to convert
    // @return {bool} - always true
    public static implicit operator bool(MultiTerrainRaycaster raycaster) => true;
}
public static class TrackSetup
{
    const string Dir = "Assets/Tracks/LagunaSeca", Vis = Dir + "/LagunaSeca_Visual.fbx", Col = Dir + "/LagunaSeca_Colliders.fbx", ScenePath = Dir + "/LagunaSeca.unity";
    public const float SpawnYaw = 215.6f;
    // @description - Sets the import settings of a track FBX: metre scale, baked axis conversion, no cameras, lights or animation, normals only on the visual model, no tangents (build size), mesh compression low for visual and off for the collider model, external local materials. The collider model must stay readable so MeshColliders can be built from it.
    // @param {string} path - asset path of the FBX
    // @param {bool} readable - true for the collider model (CPU-readable meshes, no compression, no normals), false for the visual model
    // @return {Void} - nothing
    static void Model(string path, bool readable)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path); if (importer == null) { Debug.LogError("TRACKCHECK missing " + path); return; }
        importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true; importer.importCameras = false; importer.importLights = false; importer.importBlendShapes = false;
        importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None; importer.meshCompression = ModelImporterMeshCompression.Off; importer.isReadable = readable;
        importer.importTangents = ModelImporterTangents.None;   // Standard shader without normal maps: no tangents (build size)
        if (!readable) importer.meshCompression = ModelImporterMeshCompression.Low;   // visual only (positions quantised finely); road collider FBX stays uncompressed
        importer.addCollider = false; importer.indexFormat = ModelImporterIndexFormat.Auto; importer.importNormals = ModelImporterNormals.Import;
        if (readable) importer.importNormals = ModelImporterNormals.None;   // v1.3.1: collider-only FBX (135k barrier tris); PhysX uses triangle normals, vertex normals only cost build size
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; importer.materialLocation = ModelImporterMaterialLocation.External;
        importer.materialName = ModelImporterMaterialName.BasedOnMaterialName; importer.materialSearch = ModelImporterMaterialSearch.Local;
        importer.SaveAndReimport();
    }
    // @description - Sets the import settings of a track texture: mipmaps, crunch compression, wrap mode and size. The aerial image is clamped, 2048 px (about 0.93 m per pixel, keeps the Windows archive under 25 MB) with 4x anisotropy; other textures repeat at 1024 px (buildings and concrete walls 512 px) with 8x.
    // @param {string} path - asset path of the texture
    // @return {Void} - nothing
    static void Tex(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path); if (importer == null) return;
        bool isAerial = path.Contains("Aerial");
        importer.textureType = TextureImporterType.Default; importer.mipmapEnabled = true; importer.wrapMode = isAerial ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.maxTextureSize = isAerial ? 2048 : 1024;   // aerial 2048 (0.93 m/px) keeps the Windows .7z under 25,000,000 bytes ti.textureCompression = TextureImporterCompression.Compressed; ti.crunchedCompression = true; ti.compressionQuality = aerial ? 60 : 50;
        if (path.Contains("T_Building") || path.Contains("T_Wall_Concrete")) importer.maxTextureSize = 512;   // v1.3.1: 135k-tri closed barrier boxes pushed the .7z past 25 MB; 0.8 m walls / distant buildings
        importer.anisoLevel = isAerial ? 4 : 8; importer.SaveAndReimport();
    }
    // @description - Imports the Laguna Seca export into its own additive scene: configures the models and textures, adds MeshColliders (tagged by surface type from the object name) to the collider meshes and to the barrier walls, builds the terrain colliders from the heightmaps, creates the TrackInfo with spawn point centred between the measured asphalt edges at the start/finish line, runs the Verify checks, strips the visual terrain meshes (the Unity terrain draws the ground) and saves the scene.
    // @return {Void} - nothing
    public static void Build()
    {
        AssetDatabase.Refresh();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir + "/textures" })) Tex(AssetDatabase.GUIDToAssetPath(guid));
        Model(Vis, false); Model(Col, true);
        PlayerSettings.bakeCollisionMeshes = true;   // MeshColliders cooked at build time (visual meshes stay non-readable)
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("LagunaSeca");
        var vis = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Vis)); vis.transform.SetParent(root.transform, false);
        var col = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Col)); col.transform.SetParent(root.transform, false);
        // v1.1: the FBX data is pre-rotated by Blender Bot (+X East, +Z North); no compensating rotation (verified below via Marker_North / S/F heading).
        int colliderObjectCount = 0, terrainTileCount = 0; var counts = new Dictionary<SurfaceType, int>();
        // all v1.1 colliders (road, kerbs with bump profile, paved runoff, gravel, grass): non-convex MeshColliders, renderers off, tagged by surface class
        // v1.3.1: COL_Barriers_* are vertex-identical to the visual Walls_Barriers_* (Blender Bot verify_barriers: max |dv| 0.000000 m): the colliders go on the
        // visual meshes (same closed vertical boxes) and the duplicate COL copies are dropped from the build (.7z size)
        int droppedBarrierCount = 0; foreach (var meshFilter in col.GetComponentsInChildren<MeshFilter>().Where(meshFilter => meshFilter.name.StartsWith("COL_Barriers")).ToList()) { if (PrefabUtility.IsPartOfPrefabInstance(col)) PrefabUtility.UnpackPrefabInstance(col, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); Object.DestroyImmediate(meshFilter.gameObject); droppedBarrierCount++; }
        Debug.Log($"TRACKCHECK dropped {droppedBarrierCount} COL_Barriers duplicates; wall colliders on visual Walls_Barriers meshes");
        foreach (var meshFilter in col.GetComponentsInChildren<MeshFilter>())
        {
            meshFilter.gameObject.AddComponent<MeshCollider>().sharedMesh = meshFilter.sharedMesh; var meshRenderer = meshFilter.GetComponent<MeshRenderer>(); if (meshRenderer) meshRenderer.enabled = false; colliderObjectCount++;
            var surfaceType = SurfaceTag.FromName(meshFilter.name); meshFilter.gameObject.AddComponent<SurfaceTag>().type = surfaceType; counts[surfaceType] = counts.TryGetValue(surfaceType, out var k) ? k + 1 : 1;
        }
        // barriers: MeshColliders on the visual wall meshes (cooked at build time)
        // v1.3.1: Blender Bot ships COL_Barriers_* (closed vertical boxes, identical vertices); only fall back to visual wall meshes when absent
        foreach (var meshFilter in vis.GetComponentsInChildren<MeshFilter>().Where(meshFilter => meshFilter.name.StartsWith("Walls_Barriers")))
        { meshFilter.gameObject.AddComponent<MeshCollider>().sharedMesh = meshFilter.sharedMesh; meshFilter.gameObject.AddComponent<SurfaceTag>().type = SurfaceType.Wall; counts[SurfaceType.Wall] = counts.TryGetValue(SurfaceType.Wall, out var k) ? k + 1 : 1; }
        Debug.Log("TRACKCHECK collider objects by surface: " + string.Join(", ", counts.Select(surfaceCount => $"{surfaceCount.Key} {surfaceCount.Value}")));
        // Terrain collider (option B): Unity TerrainCollider from the v1.1 RAW513, which is the same surface as the Terrain_* meshes (carved by Blender Bot).
        // Kept as the ground beyond the ~16 m surface colliders and as a safety layer under them. MeshColliders on the 524k-tri terrain would add ~10 MB to the build.
        Physics.SyncTransforms(); terrainTileCount = MakeTerrainCollider(root.transform);
        foreach (var renderer in vis.GetComponentsInChildren<MeshRenderer>()) renderer.shadowCastingMode = renderer.name.StartsWith("Terrain_") || renderer.name.StartsWith("Track_") ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        var info = new GameObject("TrackInfo").AddComponent<TrackInfo>(); info.transform.SetParent(root.transform, false);
        info.displayName = "WeatherTech Raceway Laguna Seca"; info.credit = "Track data © OpenStreetMap contributors (ODbL) · elevation USGS 3DEP · imagery USDA NAIP";
        info.spawnPosition = Vector3.zero; info.spawnYawDeg = SpawnYaw; info.minY = minYFromMeta; info.centrelineCsv = AssetDatabase.LoadAssetAtPath<TextAsset>(Dir + "/centreline.csv");
        var startFinishMarker = vis.transform.GetComponentsInChildren<Transform>().FirstOrDefault(child => child.name.StartsWith("Marker_StartFinish")); if (startFinishMarker) info.spawnPosition = startFinishMarker.position;
        if (startFinishMarker) { // v1.2: centre the spawn between the measured COL_Road asphalt edges along the S/F line (the origin is the centreline point; edges are asymmetric there)
            var startHalfWidths = info.HalfWidths.Count > 0 ? info.HalfWidths[0] : Vector2.zero; float yaw = SpawnYaw * Mathf.Deg2Rad; var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)); var right = new Vector3(forward.z, 0f, -forward.x);
            Physics.SyncTransforms(); var startFinishLine = vis.GetComponentsInChildren<Renderer>().FirstOrDefault(renderer => renderer.name.StartsWith("Track_StartFinishLine"));
            float leftEdgeDistance = 0f, rightEdgeDistance = 0f, geometricOffset = (startHalfWidths.y - startHalfWidths.x) * 0.5f;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                leftEdgeDistance = rightEdgeDistance = 0f; for (float offset = 0f; offset < 14f; offset += 0.02f) { if (RoadAt(info.spawnPosition - right * offset)) leftEdgeDistance = offset; else break; } for (float offset = 0f; offset < 14f; offset += 0.02f) { if (RoadAt(info.spawnPosition + right * offset)) rightEdgeDistance = offset; else break; }
                if (iteration < 2) info.spawnPosition += right * (rightEdgeDistance - leftEdgeDistance) * 0.5f;
            }
            float totalOff = Vector3.Dot(info.spawnPosition - startFinishMarker.position, right), along = startFinishLine ? Vector3.Dot(startFinishLine.bounds.center - info.spawnPosition, forward) : float.NaN;
            spawnCheck = $"spawn {info.spawnPosition.ToString("F2")}: asphalt (COL_Road) left {leftEdgeDistance:0.00} m / right {rightEdgeDistance:0.00} m (centre offset {(rightEdgeDistance - leftEdgeDistance) * 0.5f:+0.00;-0.00} m); spawn is {totalOff:+0.00;-0.00} m right of the origin marker (geo.pkl hl/hr at s=0 say {geometricOffset:+0.00;-0.00} m); Track_StartFinishLine centre {along:+0.00;-0.00} m along the heading from spawn";
        }
        Verify(vis, col, info, colliderObjectCount, terrainTileCount);
        PrefabUtility.UnpackPrefabInstance(vis, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        int strippedCount = 0; foreach (var terrainFilter in vis.GetComponentsInChildren<MeshFilter>(true).Where(meshFilter => meshFilter.name.StartsWith("Terrain_")).ToList()) { Object.DestroyImmediate(terrainFilter.gameObject); strippedCount++; }
        Debug.Log($"TRACKCHECK stripped {strippedCount} Terrain_* mesh objects (ground rendered by the 2049 Unity Terrain)");
        EditorSceneManager.SaveScene(scene, ScenePath);
    }
    [System.Serializable] class HMeta { public float[] extent_m; public float height_range_m; public float[] unity_terrain_position; }
    // inner window on the 2049 grid (node indices; multiples of 4 so the borders sit on 513 nodes): covers the track bbox +40 m
    public const int InnerColumnStart = 560, InnerRowStart = 332;
    // @description - Reads a 16-bit little-endian square heightmap file into an array of raw values (0..65535, row 0 = south), and throws if the file size does not match.
    // @param {string} fileName - file name inside the track folder
    // @param {int} n - number of nodes per side
    // @return {ushort[]} - raw height values, row-major, n x n entries
    static ushort[] Raw(string fileName, int sideLength) { var bytes = System.IO.File.ReadAllBytes(Dir + "/" + fileName); if (bytes.Length != sideLength * sideLength * 2) throw new System.Exception("TRACKCHECK heightmap size " + fileName); var heights = new ushort[sideLength * sideLength]; for (int i = 0; i < heights.Length; i++) heights[i] = (ushort)(bytes[2 * i] | bytes[2 * i + 1] << 8); return heights; }
    // Explicit terrain material asset: with materialTemplate = null the Terrain falls back to the built-in default terrain material, whose
    // shader (Nature/Terrain/Standard) is not pulled into player builds -> the Terrain components draw nothing (only the road/kerb meshes show).
    public const string TerrainMatPath = Dir + "/Materials/M_Terrain_DEM.mat";
    // @description - Returns the material used by the terrain tiles, creating it with the Nature/Terrain/Standard shader if it does not exist. An explicit asset is needed because the default terrain material's shader is not included in player builds, which would leave the terrain invisible.
    // @return {Material} - the terrain material asset
    public static Material TerrainMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMatPath); if (material) return material;
        material = new Material(Shader.Find("Nature/Terrain/Standard")) { name = "M_Terrain_DEM" }; AssetDatabase.CreateAsset(material, TerrainMatPath); return material;
    }
    [MenuItem("Scirocco/Assign Laguna Seca terrain material")]
    // @description - Menu entry "Scirocco > Assign Laguna Seca terrain material": opens the Laguna Seca scene, assigns the terrain material to every terrain and saves.
    // @return {Void} - nothing
    public static void AssignTerrainMaterial()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single); var material = TerrainMaterial(); int terrainCount = 0;
        foreach (var terrain in Object.FindObjectsOfType<Terrain>(true)) { terrain.materialTemplate = material; EditorUtility.SetDirty(terrain); terrainCount++; }
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets(); Debug.Log($"TRACKCHECK terrain material {material.shader.name} assigned to {terrainCount} terrains");
    }
    // @description - Builds the Unity terrain from the 2049 and 513 heightmaps: a fine 2049-grid window (three tiles, no resampling) around the track plus an outer 513-grid tile with the window area lowered 1.5 m and seam nodes taken from the fine grid so there are no cracks. Each tile gets a TerrainData, a terrain layer using the aerial image across the whole extent, a TerrainCollider tagged Terrain and a Terrain renderer.
    // @param {Transform} parent - scene object that holds the tiles
    // @return {int} - number of tiles created
    static int MakeTerrainCollider(Transform parent)
    {
        var heightmapMeta = JsonUtility.FromJson<HMeta>(System.IO.File.ReadAllText(Dir + "/LagunaSeca_Heightmap_meta.json"));
        float extentMetres = heightmapMeta.extent_m[0], heightRangeMetres = heightmapMeta.height_range_m, nodeSpacing = extentMetres / 2048f; var terrainOrigin = new Vector3(heightmapMeta.unity_terrain_position[0], heightmapMeta.unity_terrain_position[1], heightmapMeta.unity_terrain_position[2]);
        var fineHeights = Raw("LagunaSeca_Heightmap_2049_16bit_LE.raw", 2049); var coarseHeights = Raw("LagunaSeca_Heightmap_513_16bit_LE.raw", 513);   // row 0 = south, little-endian, already carved
        var aerialTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/textures/T_Terrain_Aerial_USGS_NAIP.png");
        foreach (var oldAssetGuid in AssetDatabase.FindAssets("LagunaSeca_Terrain", new[] { Dir })) { var assetPath = AssetDatabase.GUIDToAssetPath(oldAssetGuid); if (assetPath.EndsWith(".asset") || assetPath.EndsWith(".terrainlayer")) AssetDatabase.DeleteAsset(assetPath); }
        AssetDatabase.DeleteAsset(Dir + "/LagunaSeca_TerrainData.asset"); AssetDatabase.DeleteAsset(Dir + "/LagunaSeca_Aerial.terrainlayer");
        int tileCount = 0;
        void Tile(string name, int resolution, float spacing, Vector3 tileOrigin, System.Func<int, int, float> h01, int order)
        {
            var heights = new float[resolution, resolution]; for (int row = 0; row < resolution; row++) for (int column = 0; column < resolution; column++) heights[row, column] = h01(row, column);
            var tileData = new TerrainData { heightmapResolution = resolution }; tileData.size = new Vector3((resolution - 1) * spacing, heightRangeMetres, (resolution - 1) * spacing); tileData.SetHeights(0, 0, heights);
            var off = new Vector2(tileOrigin.x - terrainOrigin.x, tileOrigin.z - terrainOrigin.z);   // splat uv = (local + tileOffset) / tileSize -> the one aerial spans the full extent on every tile
            var layer = new TerrainLayer { diffuseTexture = aerialTexture, tileSize = new Vector2(extentMetres, extentMetres), tileOffset = off, smoothness = 0f, metallic = 0f };
            AssetDatabase.CreateAsset(layer, $"{Dir}/LagunaSeca_Terrain_{name}.terrainlayer");
            tileData.alphamapResolution = 16; tileData.baseMapResolution = 16; tileData.terrainLayers = new[] { layer };
            AssetDatabase.CreateAsset(tileData, $"{Dir}/LagunaSeca_Terrain_{name}.asset");
            var terrainObject = new GameObject("TerrainCollider_DEM_" + name); terrainObject.transform.SetParent(parent, false); terrainObject.transform.position = tileOrigin;
            terrainObject.AddComponent<TerrainCollider>().terrainData = tileData; terrainObject.AddComponent<SurfaceTag>().type = SurfaceType.Terrain;
            var terrainComponent = terrainObject.AddComponent<Terrain>(); terrainComponent.terrainData = tileData; terrainComponent.heightmapPixelError = 1f; terrainComponent.basemapDistance = 4000f; terrainComponent.drawInstanced = true; terrainComponent.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; terrainComponent.drawTreesAndFoliage = false;
            terrainComponent.materialTemplate = TerrainMaterial(); terrainComponent.groupingID = order; tileCount++;
            Debug.Log($"TRACKCHECK terrain tile {name}: {resolution}x{resolution} at {spacing:0.000} m, origin {tileOrigin.ToString("F2")}, size {tileData.size.x:0.0} m");
        }
        System.Func<int, int, float> fineHeightFraction = (row, column) => fineHeights[row * 2049 + column] / 65535f;
        // inner: A = 1025^2 at (R0, C0); B1/B2 = 513^2 above it (rows R0+1024..R0+1536), side by side. Exact 2049 nodes, no resampling.
        Tile("Inner_A", 1025, nodeSpacing, terrainOrigin + new Vector3(InnerColumnStart * nodeSpacing, 0f, InnerRowStart * nodeSpacing), (row, column) => fineHeightFraction(InnerRowStart + row, InnerColumnStart + column), 1);
        Tile("Inner_B1", 513, nodeSpacing, terrainOrigin + new Vector3(InnerColumnStart * nodeSpacing, 0f, (InnerRowStart + 1024) * nodeSpacing), (row, column) => fineHeightFraction(InnerRowStart + 1024 + row, InnerColumnStart + column), 1);
        Tile("Inner_B2", 513, nodeSpacing, terrainOrigin + new Vector3((InnerColumnStart + 512) * nodeSpacing, 0f, (InnerRowStart + 1024) * nodeSpacing), (row, column) => fineHeightFraction(InnerRowStart + 1024 + row, InnerColumnStart + 512 + column), 1);
        // outer: 513 grid; strictly inside the inner rectangle lowered 1.5 m (hidden), border nodes = 2049 value (crack-free seam)
        int coarseColumnStart = InnerColumnStart / 4, coarseColumnEnd = (InnerColumnStart + 1024) / 4, coarseRowStart = InnerRowStart / 4, coarseRowEnd = (InnerRowStart + 1536) / 4; float hiddenDrop = 1.5f / heightRangeMetres;
        Tile("Outer", 513, nodeSpacing * 4f, terrainOrigin, (row, column) =>
        {
            bool insideColumns = column >= coarseColumnStart && column <= coarseColumnEnd, insideRows = row >= coarseRowStart && row <= coarseRowEnd;
            if (insideColumns && insideRows) { bool border = column == coarseColumnStart || column == coarseColumnEnd || row == coarseRowStart || row == coarseRowEnd; return border ? fineHeightFraction(row * 4, column * 4) : Mathf.Max(0f, coarseHeights[row * 513 + column] / 65535f - hiddenDrop); }
            return coarseHeights[row * 513 + column] / 65535f;
        }, 0);
        Debug.Log($"TRACKCHECK terrain: inner 2049-grid window x {terrainOrigin.x + InnerColumnStart * nodeSpacing:0.0}..{terrainOrigin.x + (InnerColumnStart + 1024) * nodeSpacing:0.0}, z {terrainOrigin.z + InnerRowStart * nodeSpacing:0.0}..{terrainOrigin.z + (InnerRowStart + 1536) * nodeSpacing:0.0}; outer 513 elsewhere");
        minYFromMeta = terrainOrigin.y; return tileCount;
    }
    static float minYFromMeta = -92.13f; static string spawnCheck = "";
    // @description - Tells whether the road collider is directly below a point (ray from 5 m above, 20 m long).
    // @param {Vector3} position - world position in metres
    // @return {bool} - true if the first collider hit is a COL_Road object
    static bool RoadAt(Vector3 position) => Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out var hit, 20f) && hit.collider.name.StartsWith("COL_Road");
    // @description - Import verification: logs object counts, the DEM terrain collider against the visual terrain mesh, each surface collider against its visual mesh, bounds and marker headings, centreline versus road collider, lap length, elevation range, track width, terrain below the road, aerial UV error, a Corkscrew crest gap check and material and mesh statistics. Writes the report to the Unity log and to laguna_import_check.txt.
    // @param {GameObject} vis - the visual model root
    // @param {GameObject} col - the collider model root
    // @param {TrackInfo} info - track information (centreline and half widths)
    // @param {int} colliderObjectCount - number of collider objects
    // @param {int} terrainTileCount - number of terrain tiles
    // @return {Void} - nothing
    static void Verify(GameObject vis, GameObject col, TrackInfo info, int colliderObjectCount, int terrainTileCount)
    {
        var culture = CultureInfo.InvariantCulture; var report = new List<string>(); void AddReportLine(string text) { report.Add(text); }
        Physics.SyncTransforms();
        int wallColliderCount = vis.GetComponentsInChildren<MeshCollider>().Count(meshCollider => meshCollider.name.StartsWith("Walls_")); AddReportLine($"collider objects (COL_*) {colliderObjectCount}, wall colliders {wallColliderCount}, terrain collider {terrainTileCount}; root rotations vis {vis.transform.localEulerAngles} col {col.transform.localEulerAngles}");
        { // DEM collider vs Blender's visual terrain mesh away from the road (sanity: same DEM, should agree to a few cm)
            var tiles = vis.GetComponentsInChildren<MeshFilter>().Where(meshFilter => meshFilter.name.StartsWith("Terrain_")).ToList(); float sumAbsoluteError = 0f, maxAbsoluteError = 0f; int sampleCount = 0;
            var temporaryColliders = new List<MeshCollider>(); foreach (var tile in tiles) { var temporaryCollider = tile.gameObject.AddComponent<MeshCollider>(); temporaryCollider.sharedMesh = tile.sharedMesh; temporaryColliders.Add(temporaryCollider); }
            Physics.SyncTransforms(); var terrainRaycaster = new MultiTerrainRaycaster();
            for (float sampleX = -600f; sampleX < 1150f; sampleX += 11f) for (float sampleZ = -1000f; sampleZ < 780f; sampleZ += 11f)
            {
                var ray = new Ray(new Vector3(sampleX, 300f, sampleZ), Vector3.down);
                if (!terrainRaycaster.Raycast(ray, out var visualHit, 600f)) continue; RaycastHit best = default; bool got = false;
                foreach (var temporaryCollider in temporaryColliders) if (temporaryCollider.Raycast(ray, out var hitInfo, 600f)) { best = hitInfo; got = true; break; }
                if (!got) continue; float heightError = visualHit.point.y - best.point.y; sumAbsoluteError += Mathf.Abs(heightError); maxAbsoluteError = Mathf.Max(maxAbsoluteError, Mathf.Abs(heightError)); sampleCount++;
            }
            foreach (var temporaryCollider in temporaryColliders) Object.DestroyImmediate(temporaryCollider); Physics.SyncTransforms();
            AddReportLine($"terrain collider (RAW2049) vs Blender Terrain_* mesh (1025 grid, stripped from the scene after this check) at {sampleCount} grid points: mean |dy| {sumAbsoluteError / Mathf.Max(1, sampleCount):0.000} m, max {maxAbsoluteError:0.00} m");
        }
        { // each surface collider vs its visual mesh: collider-mesh vertices raycast onto a temporary MeshCollider of the matching visual mesh
            var terrainRaycaster = new MultiTerrainRaycaster();
            foreach (var surfacePair in new[] { ("COL_Road", "Track_Road"), ("COL_PitLane", "Track_PitLane"), ("COL_Kerbs", "Track_Kerbs"), ("COL_Runoff_Paved", "Track_Runoff_Paved"), ("COL_Gravel", "Track_Gravel"), ("COL_Grass", "Track_Grass") })
            {
                var visualMeshes = vis.GetComponentsInChildren<MeshFilter>().Where(meshFilter => meshFilter.name.StartsWith(surfacePair.Item2)).ToList(); var temporaryColliders = new List<MeshCollider>();
                foreach (var meshFilter in visualMeshes) { var temporaryObject = new GameObject("tmp"); temporaryObject.transform.SetPositionAndRotation(meshFilter.transform.position, meshFilter.transform.rotation); temporaryObject.transform.localScale = meshFilter.transform.lossyScale; var temporaryCollider = temporaryObject.AddComponent<MeshCollider>(); temporaryCollider.sharedMesh = meshFilter.sharedMesh; temporaryColliders.Add(temporaryCollider); }
                Physics.SyncTransforms(); int sampleCount = 0, missCount = 0; float sumAbsoluteError = 0f, maxAbsoluteError = 0f, minClearance = float.MaxValue;
                foreach (var colliderMesh in col.GetComponentsInChildren<MeshFilter>().Where(meshFilter => meshFilter.name.StartsWith(surfacePair.Item1)))
                {
                    var vertices = colliderMesh.sharedMesh.vertices; for (int i = 0; i < vertices.Length; i += 3)
                    {
                        var worldPoint = colliderMesh.transform.TransformPoint(vertices[i]); var ray = new Ray(worldPoint + Vector3.up * 1.5f, Vector3.down); bool got = false; float best = float.MaxValue;
                        foreach (var temporaryCollider in temporaryColliders) if (temporaryCollider.Raycast(ray, out var hit, 3f)) { got = true; best = Mathf.Min(best, Mathf.Abs(hit.point.y - worldPoint.y)); }
                        if (got) { sumAbsoluteError += best; maxAbsoluteError = Mathf.Max(maxAbsoluteError, best); sampleCount++; } else missCount++;
                        if (terrainRaycaster && terrainRaycaster.Raycast(new Ray(worldPoint + Vector3.up * 50f, Vector3.down), out var terrainHit, 200f)) minClearance = Mathf.Min(minClearance, worldPoint.y - terrainHit.point.y);
                    }
                }
                foreach (var temporaryCollider in temporaryColliders) Object.DestroyImmediate(temporaryCollider.gameObject); Physics.SyncTransforms();
                AddReportLine($"{surfacePair.Item1} vs visual {surfacePair.Item2} ({visualMeshes.Count} meshes): {sampleCount} vertices, mean |dy| {sumAbsoluteError / Mathf.Max(1, sampleCount):0.000} m, max {maxAbsoluteError:0.000} m, no visual within 1.5 m: {missCount}; min height above terrain collider {minClearance:0.00} m");
            }
        }
        Bounds BoundsOf(GameObject root, string prefix) { var renderers = root.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.name.StartsWith(prefix)).ToArray(); var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(); foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds); return bounds; }
        var terrainBounds = BoundsOf(vis, "Terrain_"); var roadBounds = BoundsOf(col, "COL_Road");
        AddReportLine($"terrain bounds min {terrainBounds.min.ToString("F1")} max {terrainBounds.max.ToString("F1")} (heightmap meta SW corner x=-675.0 z=-1090.2, size 1902.5, zmin -92.1)");
        AddReportLine($"road collider bounds min {roadBounds.min.ToString("F1")} max {roadBounds.max.ToString("F1")}");
        var transforms = vis.GetComponentsInChildren<Transform>(); var startFinishMarker = transforms.FirstOrDefault(child => child.name.StartsWith("Marker_StartFinish")); var northMarker = transforms.FirstOrDefault(child => child.name.StartsWith("Marker_North"));
        if (startFinishMarker) AddReportLine($"Marker_StartFinish pos {startFinishMarker.position.ToString("F2")} fwd(+Y local) {startFinishMarker.up.ToString("F3")} fwd(+Z local) {startFinishMarker.forward.ToString("F3")} (expected race dir (-0.583,0,-0.813)) heading {Mathf.Repeat(Mathf.Atan2(startFinishMarker.forward.x, startFinishMarker.forward.z) * Mathf.Rad2Deg, 360f):0.0} deg from +Z (expect 215.6)"); else AddReportLine("Marker_StartFinish NOT FOUND");
        if (northMarker) AddReportLine($"Marker_North pos {northMarker.position.ToString("F1")} (expect +Z)"); else AddReportLine("Marker_North NOT FOUND");
        var scales = transforms.Where(child => (child.localScale - Vector3.one).sqrMagnitude > 1e-6f || child.localRotation != Quaternion.identity).Select(child => $"{child.name} s={child.localScale} r={child.localEulerAngles}").Take(5).ToList();
        AddReportLine("non-identity transforms: " + (scales.Count == 0 ? "none" : string.Join("; ", scales)));
        // centreline vs road collider
        var centreline = info.Centreline; float sumHeightError = 0f, maxHeightError = 0f; int centrelineMissCount = 0, hitsTerrainCount = 0; var roadHeights = new float[centreline.Count];
        for (int i = 0; i < centreline.Count; i++)
        {
            if (Physics.Raycast(centreline[i] + Vector3.up * 20f, Vector3.down, out var hit, 60f)) { roadHeights[i] = hit.point.y; float heightError = hit.point.y - centreline[i].y; sumHeightError += Mathf.Abs(heightError); maxHeightError = Mathf.Max(maxHeightError, Mathf.Abs(heightError)); if (!hit.collider.name.StartsWith("COL_Road")) hitsTerrainCount++; }
            else { centrelineMissCount++; roadHeights[i] = centreline[i].y; }
        }
        AddReportLine($"centreline points {centreline.Count}: raycast misses {centrelineMissCount}, first hit not road collider {hitsTerrainCount} (bridges/terrain), |collider y - centreline z| mean {sumHeightError / centreline.Count:0.000} max {maxHeightError:0.000} m");
        AddReportLine(spawnCheck);
        AddReportLine($"lap length (centreline) {info.LapLength():0.0} m (official 3602)");
        float largestDrop = 0f; int largestDropIndex = 0; for (int i = 0; i < centreline.Count; i++) for (int k = 1; k <= 69; k++) { float drop = roadHeights[i] - roadHeights[(i + k) % centreline.Count]; if (drop > largestDrop) { largestDrop = drop; largestDropIndex = i; } }
        AddReportLine($"max drop over <=138 m along race direction (raycast on collider) {largestDrop:0.0} m at s~{largestDropIndex * 2} m (Corkscrew ~2484-2621, official 18 m)");
        float minElevation = roadHeights.Min(), maxElevation = roadHeights.Max(); AddReportLine($"elevation range on road {maxElevation - minElevation:0.0} m (official 55)");
        var halfWidths = info.HalfWidths; float minWidth = halfWidths.Min(halfWidth => halfWidth.x + halfWidth.y), medianWidth = halfWidths.Select(halfWidth => halfWidth.x + halfWidth.y).OrderBy(width => width).ElementAt(halfWidths.Count / 2);
        AddReportLine($"track width min {minWidth:0.0} median {medianWidth:0.0} m vs car width 1.62 m / track 1.40 m -> width/car {medianWidth / 1.62f:0.0}x");
        // terrain must stay below the road under the road
        int terrainAboveRoadCount = 0; float worstOverlap = 0f;
        for (int i = 0; i < centreline.Count; i += 5)
        {
            var hits = Physics.RaycastAll(centreline[i] + Vector3.up * 20f, Vector3.down, 60f); float road = float.NaN, terr = float.NaN;
            foreach (var hit in hits) { if (hit.collider.name.StartsWith("COL_Road")) road = hit.point.y; else if (hit.collider is TerrainCollider) terr = Mathf.Max(float.IsNaN(terr) ? -1e9f : terr, hit.point.y); }
            if (!float.IsNaN(road) && !float.IsNaN(terr) && terr > road - 0.02f) { terrainAboveRoadCount++; worstOverlap = Mathf.Max(worstOverlap, terr - road); }
        }
        AddReportLine($"terrain above road collider at {terrainAboveRoadCount} of {centreline.Count / 5} sampled centreline points (worst {worstOverlap:0.00} m)");
        { // terrain layer UV vs Blender's aerial UV on Terrain_* vertices
            float westEdge = -674.956f, southEdge = -1090.236f, extentMetres = 1902.4647f, maxUvError = 0f; int vertexCount = 0;
            foreach (var terrainMesh in vis.GetComponentsInChildren<MeshFilter>().Where(meshFilter => meshFilter.name.StartsWith("Terrain_")).Take(64)) { var vertices = terrainMesh.sharedMesh.vertices; var uvs = terrainMesh.sharedMesh.uv; if (uvs.Length != vertices.Length) continue;
                for (int i = 0; i < vertices.Length; i += 97) { var worldPoint = terrainMesh.transform.TransformPoint(vertices[i]); var expectedUv = new Vector2((worldPoint.x - westEdge) / extentMetres, (worldPoint.z - southEdge) / extentMetres); maxUvError = Mathf.Max(maxUvError, (expectedUv - uvs[i]).magnitude * extentMetres); vertexCount++; } }
            AddReportLine($"aerial: Unity terrain-layer UV vs Blender Terrain_* UV at {vertexCount} verts: max error {maxUvError:0.00} m on the ground");
        }
        { // Corkscrew crest (s 2400-2520 m): down-rays across each asphalt edge (-0.3..+2.5 m) on the VISUAL surfaces; a ray whose first visual hit is the terrain = a gap (dark sliver)
            var visualMeshes = vis.GetComponentsInChildren<MeshFilter>().Where(meshFilter => meshFilter.name.StartsWith("Track_") || meshFilter.name.StartsWith("Terrain_")).ToList(); var temporaryColliders = new List<MeshCollider>();
            foreach (var meshFilter in visualMeshes) { var temporaryObject = new GameObject("tmp_" + meshFilter.name); temporaryObject.transform.SetPositionAndRotation(meshFilter.transform.position, meshFilter.transform.rotation); temporaryObject.transform.localScale = meshFilter.transform.lossyScale; var temporaryCollider = temporaryObject.AddComponent<MeshCollider>(); temporaryCollider.sharedMesh = meshFilter.sharedMesh; temporaryColliders.Add(temporaryCollider); }
            var crestTerrainRaycaster = new MultiTerrainRaycaster(); Physics.SyncTransforms(); var crestCentreline = info.Centreline; var crestHalfWidths = info.HalfWidths;
            foreach (int side in new[] { -1, 1 })
            {
                int rays = 0, gapCount = 0; float worstGapMetres = 0f, minHeightAboveTerrain = float.MaxValue; var gapLocations = new List<string>();
                for (int i = 1200; i <= 1260 && i < crestCentreline.Count - 1; i++)
                {
                    var forward = (crestCentreline[i + 1] - crestCentreline[i - 1]); forward.y = 0f; forward.Normalize(); var right = new Vector3(forward.z, 0f, -forward.x); float edgeHalfWidth = side < 0 ? crestHalfWidths[i].x : crestHalfWidths[i].y;
                    for (float edgeOffset = -0.3f; edgeOffset <= 2.5f; edgeOffset += 0.05f)
                    {
                        var edgePoint = crestCentreline[i] + right * side * (edgeHalfWidth + edgeOffset); var ray = new Ray(new Vector3(edgePoint.x, crestCentreline[i].y + 30f, edgePoint.z), Vector3.down); rays++;
                        float best = float.MaxValue; string hitName = ""; foreach (var temporaryCollider in temporaryColliders) if (temporaryCollider.Raycast(ray, out var hit, 80f) && hit.distance < best) { best = hit.distance; hitName = temporaryCollider.name; }
                        bool terrainFirst = hitName.StartsWith("tmp_Terrain_"); float terrainHeight = float.NaN; if (crestTerrainRaycaster && crestTerrainRaycaster.Raycast(ray, out var terrainHitInfo, 80f)) terrainHeight = terrainHitInfo.point.y;
                        if (!terrainFirst && hitName.Length > 0 && !float.IsNaN(terrainHeight)) minHeightAboveTerrain = Mathf.Min(minHeightAboveTerrain, (ray.origin.y - best) - terrainHeight);   // visual surface height above the 2049 Unity terrain
                        if (terrainFirst || hitName.Length == 0 || (!float.IsNaN(terrainHeight) && terrainHeight > ray.origin.y - best + 0.002f)) { gapCount++; float gapSize = float.IsNaN(terrainHeight) ? 0f : terrainHeight - (ray.origin.y - best); worstGapMetres = Mathf.Max(worstGapMetres, gapSize); if (gapLocations.Count < 6) gapLocations.Add($"s{i * 2}/{edgeOffset:+0.00;-0.00}m:{(hitName.Length == 0 ? "none" : hitName.Substring(4))}"); }
                    }
                }
                AddReportLine($"Corkscrew crest s2400-2520 {(side < 0 ? "left" : "right")} edge: {rays} rays, gaps/terrain-on-top {gapCount} (worst terrain above visual {worstGapMetres:0.000} m), min visual surface above 2049 terrain {minHeightAboveTerrain:0.000} m {string.Join(" ", gapLocations)}");
            }
            foreach (var temporaryCollider in temporaryColliders) Object.DestroyImmediate(temporaryCollider.gameObject); Physics.SyncTransforms();
        }
        // materials / textures
        var materials = vis.GetComponentsInChildren<Renderer>().SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct().ToList();
        AddReportLine("materials: " + string.Join(", ", materials.Select(material => $"{material.name}[{(material.mainTexture ? material.mainTexture.name : "NO TEXTURE")}]")));
        int maxVertices = vis.GetComponentsInChildren<MeshFilter>().Max(meshFilter => meshFilter.sharedMesh ? meshFilter.sharedMesh.vertexCount : 0); int triangleCount = vis.GetComponentsInChildren<MeshFilter>().Sum(meshFilter => meshFilter.sharedMesh ? (int)(meshFilter.sharedMesh.triangles.Length / 3) : 0);
        AddReportLine($"visual meshes {vis.GetComponentsInChildren<MeshFilter>().Length}, max verts/mesh {maxVertices}, total tris {triangleCount}");
        foreach (var line in report) Debug.Log("TRACKCHECK " + line);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(Application.dataPath, "../../../builds/laguna_import_check.txt"), report);
    }
}
#endif
