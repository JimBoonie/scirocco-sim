// Per-track data placed in each track scene (spawn, credits, centerline). Unity frame: +X East, +Y Up, +Z North, 1 unit = 1 m.
using System.Collections.Generic; using UnityEngine;
public class TrackInfo : MonoBehaviour
{
    public string displayName = "Track";
    public string credit = "";                 // shown in-game (HUD footer + menu) while the track is loaded
    public Vector3 spawnPosition;              // on the start/finish line (centerline)
    public float spawnYawDeg;                  // race direction, degrees clockwise from +Z (north)
    public TextAsset centerlineCsv;
    public float minY = -100f;                 // lowest terrain height (m): below this the car has fallen out of the world            // E,N,z,halfLeft,halfRight per line (2 m steps, race direction)
    [System.NonSerialized] List<Vector3> pts; [System.NonSerialized] List<Vector2> halfW;
    public List<Vector3> Centerline { get { Parse(); return pts; } }
    public List<Vector2> HalfWidths { get { Parse(); return halfW; } }   // x = left, y = right (m)
    // @description - Parses the centerline CSV once (columns East, North, height, half-width left, half-width right; meters). Converts to the Unity frame: x = East, y = height, z = North.
    // @return {Void} - nothing; fills the centerline and half-width lists
    void Parse()
    {
        if (pts != null) return; pts = new List<Vector3>(); halfW = new List<Vector2>(); if (!centerlineCsv) return;
        var invariantCulture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var line in centerlineCsv.text.Split('\n'))
        {
            var trimmedLine = line.Trim(); if (trimmedLine.Length == 0 || trimmedLine[0] == '#') continue; var columns = trimmedLine.Split(',');
            pts.Add(new Vector3(float.Parse(columns[0], invariantCulture), float.Parse(columns[2], invariantCulture), float.Parse(columns[1], invariantCulture)));   // E,N,z -> x=E, y=z, z=N
            halfW.Add(new Vector2(float.Parse(columns[3], invariantCulture), float.Parse(columns[4], invariantCulture)));
        }
    }
    // @description - Length of the closed centerline measured in the horizontal plane.
    // @return {float} - lap length in m
    public float LapLength() { var points = Centerline; float length = 0f; for (int i = 0; i < points.Count; i++) { var from = points[i]; var to = points[(i + 1) % points.Count]; from.y = to.y = 0f; length += Vector3.Distance(from, to); } return length; }
    // @description - Finds the centerline point closest to a world position (horizontal distance) and the signed lateral offset from the line there.
    // @param {Vector3} position - world position in m
    // @param {float} lateral - receives the offset in m (+ = right of the race direction)
    // @return {int} - index of the nearest centerline point
    public int Nearest(Vector3 position, out float lateral)
    {
        var points = Centerline; int best = 0; float bestDistanceSquared = float.MaxValue;
        for (int i = 0; i < points.Count; i++) { float deltaX = points[i].x - position.x, deltaZ = points[i].z - position.z, distanceSquared = deltaX * deltaX + deltaZ * deltaZ; if (distanceSquared < bestDistanceSquared) { bestDistanceSquared = distanceSquared; best = i; } }
        var nearestPoint = points[best]; var forward = points[(best + 1) % points.Count] - nearestPoint; forward.y = 0f; forward.Normalize(); var relative = position - nearestPoint; relative.y = 0f;
        lateral = Vector3.Dot(relative, new Vector3(forward.z, 0f, -forward.x)); return best;
    }
}
