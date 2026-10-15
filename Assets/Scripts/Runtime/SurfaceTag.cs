using UnityEngine;
// Surface class of a track collider (from Blender Bot's COL_* / Walls_* object names). Tag only: the tire model does NOT use it yet.
// Future work: per-surface grip / rolling drag / bump spectrum, from sourced data. No friction values are invented here.
public enum SurfaceType { Unknown, Asphalt, Kerb, RunoffPaved, Gravel, Grass, Terrain, Wall }
public class SurfaceTag : MonoBehaviour
{
    public SurfaceType type;
    // @description - From name.
    // @param {string} objectName - object name
    // @return {SurfaceType} - the from name
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
    // @description - Of.
    // @param {Collider} c - value of type Collider
    // @return {SurfaceType} - the of
    public static SurfaceType Of(Collider c) { if (!c) return SurfaceType.Unknown; var t = c.GetComponent<SurfaceTag>(); return t ? t.type : FromName(c.name); }
}
