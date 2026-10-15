using UnityEngine;
public class DebugHUD : MonoBehaviour
{
    public CarController car; GUIStyle mono, big;
    void OnGUI() { if (VrPanel.Active) return; UiSurface.Begin(out var m); Draw(); UiSurface.End(m); }
    public void Draw()
    {
        if (!car || car.engine == null) return;
        if (mono == null) { mono = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) }; mono.normal.textColor = Color.white; }
        bool info = HudVisible(car);   // H toggles the grey info boxes (telemetry / FFB / joints); the bottom input strip is always shown
        var rb = car.GetComponent<Rigidbody>();
        string g = car.dt.gear == 0 ? "N" : car.dt.gear < 0 ? "R" : car.dt.gear.ToString();
        string s = $"handling baseline (single)  tire {car.p.tirePreset} {(car.p.tireCompound == CarParams.TireCompound.ExperimentalViscoelastic ? "[Experimental viscoelastic]" : "")}\n" + $"{rb.velocity.magnitude * MphPerMs,3:0} mph  {car.engine.rpm,4:0} rpm  gear {g}  MAP {car.engine.manifoldPressureRatio:0.00}  thr {car.engine.throttleArea:0.000}\n" +
                   $"T {car.throttle:0.00} B {car.brake:0.00} C {car.clutch:0.00}  starter {car.engine.starterTorque,3:0}  cslip {car.dt.clutchSlip:0.0}  steer {car.steer:0.0}°\n";
        foreach (var w in new[] { car.FL, car.FR, car.RL, car.RR })
            s += $"{w.name}: Fz {w.Fz,5:0} SR {w.slipRatio,6:0.000} SA {w.alphaRelaxed * Mathf.Rad2Deg,5:0.0} Fx {w.force.x,5:0} Fy {w.force.y,5:0} trv {w.travel * 1000f,3:0}mm\n";
        if (car.FL.thermal != null)
        {   // tire thermal model: tread surface inner/mid/outer (F), hot pressure (psi), grip factor from temperature+pressure
            s += "Tires  tread I / M / O °F   bulk  gas   psi   grip\n";
            foreach (var w in new[] { car.FL, car.FR, car.RL, car.RR })
            {
                var th = w.thermal; if (th == null) continue;
                s += $"{w.name}: {F(th.Ts[0]),4:0}/{F(th.Ts[1]),4:0}/{F(th.Ts[2]),4:0}  {F(th.Tb),4:0} {F(th.Tg),4:0}  {th.pGauge / TireThermal.PaPerPsi,4:0.0}  {w.ts.muy * 100f,3:0}%\n";
            }
        }

        if (VrPanel.Active && TrackManager.Instance && TrackManager.Instance.Credit.Length > 0) s += TrackManager.Instance.Credit + "\n";   // VR: credit on the HUD panel
        if (info) { s = s.TrimEnd('\n'); Box(8f, 8f, s); }
        if (info) DrawWheel(car);
        if (car.router && car.router.LookSource.Length > 0) GUI.Label(new Rect(UiSurface.Width * 0.5f - 200f, 64f, 400f, 16f), $"LOOK {car.router.LookX * 120f:0}° from {car.router.LookSource}", mono);   // look offset active (shown even with info hidden)
        // driver strip: gear / pedals / ignition, large, bottom centre
        if (big == null) { big = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; big.normal.textColor = Color.white; }
        var e = car.engine; float W = UiSurface.Width, H = UiSurface.Height;
        string strip = $"GEAR {g}    THROTTLE {car.throttle * 100f,3:0}%    CLUTCH {car.clutch * 100f,3:0}%    BRAKE {car.brake * 100f,3:0}%    HANDBRAKE {car.handbrakeInput * 100f,3:0}%    IGN {(e.ignition ? "ON" : "OFF")}  ENGINE {e.State} {e.rpm,4:0} rpm" + (car.autoClutch && car.autoClutchPedal > 0f ? $"  (auto clutch {car.autoClutchPedal * 100f:0}%)" : "");
        float sw = Mathf.Min(W - 20f, 1240f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(W * 0.5f - sw * 0.5f, H - 92f, sw, 36f), Texture2D.whiteTexture); GUI.color = Color.white;
        GUI.Label(new Rect(W * 0.5f - sw * 0.5f, H - 92f, sw, 36f), strip, big);
        string hint = car.router && car.router.hintNeutral ? "Neutral - press E for 1st" : !e.ignition ? "Ignition off - press I, then hold T to start" : (!e.combusting && !e.starter ? (car.dt.gear != 0 && car.GetComponent<Rigidbody>() && car.GetComponent<Rigidbody>().velocity.magnitude > 2f ? "STALLED - rolling in gear: release the clutch to bump-start, or hold T (starter)" : "STALLED - hold T (starter) to restart  (I = ignition on/off)") : "");
        var pr0 = car.router && car.router.map != null ? car.router.map : null; int unc = pr0 != null ? pr0.UncalibratedCount() : 0;
        if (unc > 0) hint += (hint.Length > 0 ? "    " : "") + $"{unc} pedal axis not calibrated (read from rest) - F1 > Calibrate";
        if (hint.Length > 0) { var hs = new GUIStyle(big) { fontSize = 16 }; hs.normal.textColor = e.ignition && !e.combusting && !e.starter ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.85f, 0.3f); if (e.ignition && !e.combusting && !e.starter) hs.fontSize = 24; GUI.Label(new Rect(W * 0.5f - sw * 0.5f, H - 130f, sw, 34f), hint, hs); }
        var f = car.GetComponent<FfbController>();
        if (f && info)
        {
            float x = UiSurface.Width - 238f, y = 70f; var pr = car.router && car.router.map != null ? car.router.map.profile : null; float cap = pr != null ? pr.ffbMaxOutputPct * 0.01f : 1f;
            string ft = $"FFB {f.status}\ncol {f.totalTorque,5:0.0} Nm (tire {car.columnTorque,4:0.0} stop {f.stopTorque,3:0.0})\ncmd {f.command * 100f,4:0}%  clip {f.clipPct,3:0}%"; var sz = mono.CalcSize(new GUIContent(ft));
            GUI.Box(new Rect(x, y, 230f, sz.y + 18f), GUIContent.none); GUI.Label(new Rect(x + 4f, y + 2f, 224f, sz.y), ft, mono); y += sz.y + 6f;
            float n = pr != null && pr.wheelbaseMaxTorqueNm > 0f ? Mathf.Clamp(f.totalTorque / pr.wheelbaseMaxTorqueNm * pr.ffbStrengthPct * 0.01f, -1.5f, 1.5f) : 0f;   // clipping meter
            float cx = x + 115f, bw = 100f; GUI.color = Color.gray; GUI.DrawTexture(new Rect(x + 10f, y, 210f, 7f), Texture2D.whiteTexture);
            GUI.color = Mathf.Abs(n) > cap ? Color.red : Color.green; GUI.DrawTexture(n >= 0 ? new Rect(cx, y, Mathf.Min(n, 1f) * bw, 7f) : new Rect(cx + Mathf.Max(n, -1f) * bw, y, -Mathf.Max(n, -1f) * bw, 7f), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.DrawTexture(new Rect(cx - 1f, y - 2f, 2f, 11f), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx + cap * bw, y - 2f, 2f, 11f), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx - cap * bw, y - 2f, 2f, 11f), Texture2D.whiteTexture);
        }
        if (info) { Box(8f, UiSurface.Height - 230f, JointText(car).TrimEnd('\n')); GUI.Label(new Rect(8, UiSurface.Height - 56f, 1200, 16), "W/S throttle/brake | A/D steer | E/Q shift | LShift clutch | Space handbrake | I ign | hold T starter | R reset | Backspace reset to start | C cam | H hide info | F1/Esc settings", mono); }
    }
    public const float MphPerMs = 2.236936f;   // display only: 1 m/s = 2.236936 mph (exact by definition of the mile); physics stays SI
    static double F(double c) => TireThermal.CToF(c);
    void Box(float x, float y, string t) { var sz = mono.CalcSize(new GUIContent(t)); GUI.Box(new Rect(x, y, sz.x + 10f, sz.y + 6f), GUIContent.none); GUI.Label(new Rect(x + 5f, y + 3f, sz.x + 2f, sz.y), t, mono); }
    static bool HudVisible(CarController c) => !(c.router && c.router.map != null && c.router.map.profile != null) || c.router.map.profile.hudVisible;

    // Steering readout (numbers only). The on-screen wheel ring graphic was removed: GUIUtility.RotateAroundPivot under the UiSurface
    // scale matrix drew it oversized as a giant arc down the right side. Car's steering-wheel (column) angle; road-wheel angles from the linkage kinematics (SteeringLinkage).
    static void DrawWheel(CarController car)
    {
        float cx = UiSurface.Width - 110f;
        GUI.Label(new Rect(cx - 100f, 20f, 200f, 40f), $"wheel {car.handwheelDeg,6:0}°\nL/R {car.FL.steerDeg,5:0.0}/{car.FR.steerDeg,5:0.0}°");
    }
    // Joint reaction forces on the car body (tire-to-body force path), from the linear maps in compliance.json (linearised at design
    // ride height, bush rates ESTIMATED). Shown as |F| in N with the vertical component; front corners use previous-step tire forces.
    static readonly string[] FJ = { "armF", "armR", "tieRod", "topMnt", "ballJ" }, RJ = { "pivL", "pivR", "dampL", "dampR" };
    public static Vector3[] FrontJoints(SuspensionTables k, WheelCorner w)
    {
        var r = new Vector3[5]; if (k == null || k.fcJoint == null || k.fcJoint.Length < 60) return r;
        float sy = w.left ? 1f : -1f, fy = w.left ? -w.force.y : w.force.y;   // outward lateral, left-corner frame
        bool brk = w.brakeTorque > 0f && w.force.x < 0f;
        float[] L = { brk ? w.force.x : 0f, brk ? 0f : w.force.x, fy, w.Fz };
        for (int l = 0; l < 4; l++) for (int jn = 0; jn < 5; jn++)
        { int b = (l * 5 + jn) * 3; r[jn] += L[l] * new Vector3(k.fcJoint[b], sy * k.fcJoint[b + 1], k.fcJoint[b + 2]); }
        return r;
    }
    public static Vector3[] RearJoints(SuspensionTables k, WheelCorner wl, WheelCorner wr)
    {
        var r = new Vector3[4]; if (k == null || k.rbJoint == null || k.rbJoint.Length < 36) return r;
        int[] swap = { 1, 0, 3, 2 };
        foreach (var w in new[] { wl, wr })
        {
            float fy = w.left ? -w.force.y : w.force.y; float[] L = { w.force.x, fy, w.Fz };
            for (int l = 0; l < 3; l++) for (int jn = 0; jn < 4; jn++)
            {
                int b = (l * 4 + jn) * 3; var v = new Vector3(k.rbJoint[b], k.rbJoint[b + 1], k.rbJoint[b + 2]) * L[l];
                if (w.left) r[jn] += v; else { v.y = -v.y; r[swap[jn]] += v; }
            }
        }
        return r;
    }
    static string JointText(CarController car)
    {
        var k = car.p.kin; if (!k) return "";
        string s = "Joint reactions on body |F| (Fz) N  [linear maps, bushes E]\n";
        foreach (var w in new[] { car.FL, car.FR })
        {
            var f = FrontJoints(k, w); s += w.name + ": ";
            for (int i = 0; i < 5; i++) s += $"{FJ[i]} {f[i].magnitude,5:0} ({f[i].z,5:0})  ";
            s += "\n";
        }
        var rr = RearJoints(k, car.RL, car.RR); s += "Rear beam: ";
        for (int i = 0; i < 4; i++) s += $"{RJ[i]} {rr[i].magnitude,5:0} ({rr[i].z,5:0})  ";
        return s + "\n(antiCoef F " + car.FL.antiCoef.ToString("0.000") + " R " + car.RL.antiCoef.ToString("0.000") + $")  unsprung {(car.p.useUnsprungMass ? "on" : "off")}  front {car.p.frontModel}  rear {car.p.rearModel}";
    }
}
