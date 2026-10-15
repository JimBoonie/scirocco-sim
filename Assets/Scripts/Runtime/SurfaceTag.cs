using UnityEngine;
// Surface class of a track collider (from Blender Bot's COL_* / Walls_* object names). Tag only: the tire model does NOT use it yet.
// Future work: per-surface grip / rolling drag / bump spectrum, from sourced data. No friction values are invented here.
public enum SurfaceType { Unknown, Asphalt, Kerb, RunoffPaved, Gravel, Grass, Terrain, Wall }
public class SurfaceTag : MonoBehaviour
{
    public SurfaceType type;
    // @description - Classifies a track collider from its object name prefix (COL_Road, COL_Kerb, COL_Runoff, COL_Gravel, COL_Grass, Walls_, COL_Barriers, TerrainCollider). Tag only: the tire model does not use it yet.
    // @param {string} objectName - name of the collider object
    // @return {SurfaceType} - the surface type (Unknown if no prefix matches)
    public static SurfaceType FromName(string objectName)
    {
        if (objectName.StartsWith("COL_Road") || objectName.StartsWith("COL_PitLane")) return SurfaceType.Asphalt;
        if (objectName.StartsWith("COL_Kerb")) return SurfaceType.Kerb;
        if (objectName.StartsWith("COL_Runoff")) return SurfaceType.RunoffPaved;
        if (objectName.StartsWith("COL_Gravel")) return SurfaceType.Gravel;
        if (objectName.StartsWith("COL_Grass")) return SurfaceType.Grass;
        if (objectName.StartsWith("Walls_")) return SurfaceType.Wall;
        if (objectName.StartsWith("COL_Barriers")) return SurfaceType.Wall;   // v1.3.1 closed vertical barrier boxes
        if (objectName.StartsWith("TerrainCollider")) return SurfaceType.Terrain;
        return SurfaceType.Unknown;
    }
    // @description - Surface type of a collider: an explicit SurfaceTag component wins, otherwise the name is used.
    // @param {Collider} collider - the collider the tire touches, may be null
    // @return {SurfaceType} - the surface type (Unknown for null)
    public static SurfaceType Of(Collider collider) { if (!collider) return SurfaceType.Unknown; var tag = collider.GetComponent<SurfaceTag>(); return tag ? tag.type : FromName(collider.name); }
}
