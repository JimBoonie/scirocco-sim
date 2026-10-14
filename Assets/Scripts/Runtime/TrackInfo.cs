// Per-track data placed in each track scene (spawn, credits, centreline). Unity frame: +X East, +Y Up, +Z North, 1 unit = 1 m.
using System.Collections.Generic; using UnityEngine;
public class TrackInfo : MonoBehaviour
{
    public string displayName = "Track";
    public string credit = "";                 // shown in-game (HUD footer + menu) while the track is loaded
    public Vector3 spawnPosition;              // on the start/finish line (centreline)
    public float spawnYawDeg;                  // race direction, degrees clockwise from +Z (north)
    public TextAsset centrelineCsv;
    public float minY = -100f;                 // lowest terrain height (m): below this the car has fallen out of the world            // E,N,z,halfLeft,halfRight per line (2 m steps, race direction)
    [System.NonSerialized] List<Vector3> pts; [System.NonSerialized] List<Vector2> halfW;
    public List<Vector3> Centreline { get { Parse(); return pts; } }
    public List<Vector2> HalfWidths { get { Parse(); return halfW; } }   // x = left, y = right (m)
    void Parse()
    {
        if (pts != null) return; pts = new List<Vector3>(); halfW = new List<Vector2>(); if (!centrelineCsv) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var line in centrelineCsv.text.Split('\n'))
        {
            var l = line.Trim(); if (l.Length == 0 || l[0] == '#') continue; var f = l.Split(',');
            pts.Add(new Vector3(float.Parse(f[0], inv), float.Parse(f[2], inv), float.Parse(f[1], inv)));   // E,N,z -> x=E, y=z, z=N
            halfW.Add(new Vector2(float.Parse(f[3], inv), float.Parse(f[4], inv)));
        }
    }
    public float LapLength() { var c = Centreline; float s = 0f; for (int i = 0; i < c.Count; i++) { var a = c[i]; var b = c[(i + 1) % c.Count]; a.y = b.y = 0f; s += Vector3.Distance(a, b); } return s; }
    /// nearest centreline index and signed lateral offset (+ = right of race direction) of a world position
    public int Nearest(Vector3 p, out float lateral)
    {
        var c = Centreline; int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < c.Count; i++) { float dx = c[i].x - p.x, dz = c[i].z - p.z, d = dx * dx + dz * dz; if (d < bd) { bd = d; best = i; } }
        var a = c[best]; var fwd = c[(best + 1) % c.Count] - a; fwd.y = 0f; fwd.Normalize(); var rel = p - a; rel.y = 0f;
        lateral = Vector3.Dot(rel, new Vector3(fwd.z, 0f, -fwd.x)); return best;
    }
}
