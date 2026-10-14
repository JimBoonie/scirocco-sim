    // ---- off-track at speed: start on the road 150 m upstream at 132 km/h (wheel spin matched), coast in neutral, steer off the asphalt onto runoff/grass/terrain.
    // Checks every physics step: car never below the heightmap (fall-through), a collider always under the car, no fall-safety reset.
    int oIdx = -1, oFails, oResets0; float oT, oMinAboveDem, oMinV, oMaxOff; int oNoSurf, oSteps; string oName; readonly System.Collections.Generic.List<string> oRows = new System.Collections.Generic.List<string>();
    readonly System.Collections.Generic.Dictionary<string, int> oSurf = new System.Collections.Generic.Dictionary<string, int>();
    static readonly (float s, float side, string name)[] OffSpots = {   // s of the run-off point, side +1 right / -1 left
        (2440f, -1f, "wide LEFT before the Corkscrew (T8 approach)"), (3330f, 1f, "outside RIGHT of T11 exit"), (260f, 1f, "outside RIGHT of T1"),
        (2600f, 1f, "Corkscrew exit RIGHT (T8A)"), (1990f, 1f, "outside RIGHT of T6"), (2440f, 1f, "RIGHT before the Corkscrew") };
    void LagunaOff(float v)
    {
        var tm = TrackManager.Instance; var ti = tm.info; var c = ti.Centreline; int n = c.Count;
        if (oIdx < 0 || t - oT > 7f)
        {
            if (oIdx >= 0) OffReport1();
            oIdx++; if (oIdx >= OffSpots.Length) { foreach (var r in oRows) Debug.Log(r); Debug.Log($"LAGUNAOFF RESULT {(oFails == 0 ? "PASS" : "FAIL")} {OffSpots.Length - oFails}/{OffSpots.Length} runs, fall resets {tm.FallResets}"); Next(99); return; }
            var sp = OffSpots[oIdx]; int i0 = ((int)((sp.s - 150f) / 2f) + n) % n; var f = c[(i0 + 1) % n] - c[i0]; f.y = 0f;
            tm.Place(c[i0], Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
            rb.velocity = car.transform.forward * (132f / 3.6f); foreach (var w in new[] { car.FL, car.FR, car.RL, car.RR }) w.omega = 132f / 3.6f / car.p.wheelRadius;
            oT = t; oMinAboveDem = 99f; oNoSurf = 0; oSteps = 0; oMaxOff = 0f; oMinV = 999f; oName = sp.name; oResets0 = tm.FallResets; oSurf.Clear();
        }
        var spot = OffSpots[oIdx]; Vector3 p = rb.position; int idx = ti.Nearest(p, out float lat);
        // target line: centreline until 120 m before the spot, then ramps out to edge + 25 m on the chosen side by the spot, stays there
        float ds = (idx * 2f - spot.s + 3600f + 1800f) % 3600f - 1800f;   // signed distance to the spot along the lap (m)
        float k = Mathf.Clamp01((ds + 120f) / 120f); int ti0 = (idx + 12) % n; var f0 = c[(ti0 + 1) % n] - c[ti0]; f0.y = 0f; f0.Normalize(); var right = new Vector3(f0.z, 0f, -f0.x);
        float edge = spot.side > 0 ? ti.HalfWidths[ti0].y : ti.HalfWidths[ti0].x; var tgt = c[ti0] + right * spot.side * k * (edge + 25f);
        Vector3 loc = car.transform.InverseTransformPoint(new Vector3(tgt.x, p.y, tgt.z)); float alpha = Mathf.Atan2(loc.x, loc.z);
        car.steer = Mathf.Clamp(Mathf.Atan(2f * car.p.wheelbase * Mathf.Sin(alpha) / Mathf.Max(1f, loc.magnitude)) * Mathf.Rad2Deg, -car.p.maxSteerAngle, car.p.maxSteerAngle);
        car.throttle = 0f; car.brake = 0f; car.handbrake = false; car.dt.SelectGear(0);
        oSteps++; float edgeHere = lat > 0 ? ti.HalfWidths[idx].y : ti.HalfWidths[idx].x; oMaxOff = Mathf.Max(oMaxOff, Mathf.Abs(lat) - edgeHere);
        var tc = tm.Terrain; if (tc) { var td = tc.terrainData; var o = tc.transform.position; float hT = o.y + td.GetInterpolatedHeight((p.x - o.x) / td.size.x, (p.z - o.z) / td.size.z); oMinAboveDem = Mathf.Min(oMinAboveDem, p.y - hT); }
        if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var h, 10f)) { var st = SurfaceTag.Of(h.collider).ToString(); oSurf[st] = oSurf.TryGetValue(st, out var q) ? q + 1 : 1; } else oNoSurf++;
        if (Mathf.Abs(lat) - edgeHere > 2f) oMinV = Mathf.Min(oMinV, v);
    }
    void OffReport1()
    {
        var tm = TrackManager.Instance; int resets = tm.FallResets - oResets0; float ride = car.p.hubDropDesign + car.p.wheelRadius;
        bool ok = resets == 0 && oNoSurf == 0 && oMinAboveDem > ride - 0.5f && oMaxOff > 5f;
        if (!ok) oFails++;
        oRows.Add($"OFF {(ok ? "PASS" : "FAIL")} {oName}: entered at 132 km/h, max {oMaxOff:0.0} m beyond the asphalt edge, slowest off-track {oMinV * 3.6f:0} km/h, min (car origin - heightmap) {oMinAboveDem:0.00} m (ride height {ride:0.00}), steps with no collider under car {oNoSurf}/{oSteps}, fall resets {resets}, surfaces under car: {string.Join(", ", System.Linq.Enumerable.Select(oSurf, kv => $"{kv.Key} {100f * kv.Value / Mathf.Max(1, oSteps):0}%"))}");
    }
