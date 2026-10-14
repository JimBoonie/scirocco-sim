using UnityEngine;
// Surface class of a track collider (from Blender Bot's COL_* / Walls_* object names). Tag only: the tire model does NOT use it yet.
// Future work: per-surface grip / rolling drag / bump spectrum, from sourced data. No friction values are invented here.
public enum SurfaceType { Unknown, Asphalt, Kerb, RunoffPaved, Gravel, Grass, Terrain, Wall }
public class SurfaceTag : MonoBehaviour
{
    public SurfaceType type;
    public static SurfaceType FromName(string n)
    {
        if (n.StartsWith("COL_Road") || n.StartsWith("COL_PitLane")) return SurfaceType.Asphalt;
        if (n.StartsWith("COL_Kerb")) return SurfaceType.Kerb;
        if (n.StartsWith("COL_Runoff")) return SurfaceType.RunoffPaved;
        if (n.StartsWith("COL_Gravel")) return SurfaceType.Gravel;
        if (n.StartsWith("COL_Grass")) return SurfaceType.Grass;
        if (n.StartsWith("Walls_")) return SurfaceType.Wall;
        if (n.StartsWith("COL_Barriers")) return SurfaceType.Wall;   // v1.3.1 closed vertical barrier boxes
        if (n.StartsWith("TerrainCollider")) return SurfaceType.Terrain;
        return SurfaceType.Unknown;
    }
    public static SurfaceType Of(Collider c) { if (!c) return SurfaceType.Unknown; var t = c.GetComponent<SurfaceTag>(); return t ? t.type : FromName(c.name); }
}
