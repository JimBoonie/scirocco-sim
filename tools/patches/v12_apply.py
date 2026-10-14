# Re-applies the Laguna v1.2 + off-track/camera round (2026-10-12 22:xx PT) on top of the v1.1-round project state.
import os, shutil, pickle, numpy as np
P='/workspace/scirocco_sim/unity/SciroccoSimDemo'; S='/workspace/tracks/laguna_seca/output'; A=P+'/Assets/Tracks/LagunaSeca'; R=P+'/Assets/Scripts'
for f in ['LagunaSeca_Visual.fbx','LagunaSeca_Colliders.fbx','LagunaSeca_Heightmap_513_16bit_LE.raw','LagunaSeca_Heightmap_meta.json']: shutil.copy(f'{S}/{f}', f'{A}/{f}')
for f in os.listdir(S+'/textures'): shutil.copy(f'{S}/textures/{f}', f'{A}/textures/{f}')
g=pickle.load(open(S+'/geo.pkl','rb')); c=np.array(g['centre']); hl=np.array(g['hl']); hr=np.array(g['hr'])
with open(A+'/centreline.csv','w') as f:
    f.write('# E_m,N_m,z_m,half_left_m,half_right_m  (Blender Bot v1.2 geo.pkl centre/hl/hr: asphalt edges; s=0 at S/F, 2 m steps, race direction)\n')
    for (x,y,z),l,r in zip(c,hl,hr): f.write(f'{-x:.3f},{-y:.3f},{z:.3f},{l:.2f},{r:.2f}\n')
def ed(p,a,b):
    p=R+'/'+p; s=open(p).read()
    if b in s: return
    if a not in s: raise SystemExit('MISSING in '+p+': '+a[:90])
    open(p,'w').write(s.replace(a,b,1))
ed('Runtime/CarController.cs','        rb = GetComponent<Rigidbody>();\n        ApplyParams();','        rb = GetComponent<Rigidbody>(); rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // chassis box vs thin track MeshColliders at speed: no tunnelling\n        ApplyParams();')
ed('Runtime/CarController.cs','foreach (var w in new[] { FL, FR, RL, RR }) if (w) w.ResetState(); }','foreach (var w in new[] { FL, FR, RL, RR }) if (w) w.ResetState(); Teleported?.Invoke(this); }\n    /// raised after any reset / teleport (R, reset to start, fall-safety respawn): cameras snap, look offsets cleared\n    public static event System.Action<CarController> Teleported; public static void RaiseTeleported(CarController c) => Teleported?.Invoke(c);')
ed('Runtime/TrackManager.cs','        if (car.dt != null) car.dt.SelectGear(0);\n    }','        if (car.dt != null) car.dt.SelectGear(0);\n        CarController.RaiseTeleported(car);\n    }')
p=R+'/Runtime/TrackManager.cs'; s=open(p).read()
if 'public TerrainCollider Terrain' not in s:
    i=s.rfind('}'); open(p,'w').write(s[:i]+'    TerrainCollider terrain; public TerrainCollider Terrain { get { if (!terrain) terrain = FindObjectOfType<TerrainCollider>(); return terrain; } }\n}\n')
open(R+'/Runtime/FollowCam.cs','w').write('''using UnityEngine;
public class FollowCam : MonoBehaviour
{ public Transform target; [HideInInspector] public float lookYaw, lookPitch;   // look axes (deg) from InputRouter
  void OnEnable() { CarController.Teleported += OnTeleport; } void OnDisable() { CarController.Teleported -= OnTeleport; }
  void OnTeleport(CarController c) { if (target && c && target == c.transform) Snap(); }
  /// jump straight behind the car (no lerp from the old position: after a respawn the camera was swinging in from up to ~1 km away)
  public void Snap() { lookYaw = lookPitch = 0f; if (!target) return; transform.position = target.position - target.forward * 6f + Vector3.up * 2.2f; transform.LookAt(target.position + Vector3.up * 0.6f); }
  void LateUpdate() { if (!target) return;
  Vector3 back = Quaternion.AngleAxis(lookYaw, Vector3.up) * Quaternion.AngleAxis(-lookPitch, target.right) * (-target.forward);
  Vector3 want = target.position + back * 6f + Vector3.up * 2.2f;
  transform.position = Vector3.Lerp(transform.position, want, 5f * Time.deltaTime); transform.LookAt(target.position + Vector3.up * 0.6f); } }
''')
ed('Runtime/VrManager.cs','    public void FlatLook(float yaw, float pitch)','    void OnEnable() { CarController.Teleported += OnTeleport; } void OnDisable() { CarController.Teleported -= OnTeleport; }\n    void OnTeleport(CarController c) { if (!Active && cam && mode == CamMode.Cockpit) cam.transform.localRotation = Quaternion.identity; }   // flat: straight ahead (VR head pose stays the HMD\'s)\n    public void FlatLook(float yaw, float pitch)')
ed('Runtime/InputBindings.cs','    public float Axis(InAction a) => Axis(a, out _, out _, out _);','''    public float Axis(InAction a) => Axis(a, out _, out _, out _);
    /// which binding currently drives an axis action (strongest |value|), e.g. "Gamepad rightStick/x = 0.75" (diagnostics: look snaps)
    public string Source(InAction a)
    {
        if (dirty) Resolve(); string s = ""; float best = 0f; bool bip = IsBipolar(a);
        foreach (var b in profile.Get(a)) { if (b.resolved == null) continue; float v = b.cal.Normalize(Raw(b.resolved), bip && !(b.resolved is ButtonControl)); if (b.negative) v = -v;
            if (Mathf.Abs(v) > Mathf.Abs(best)) { best = v; s = $"{(string.IsNullOrEmpty(b.product) ? b.layout : b.product)} {b.control} = {v:0.00}"; } }
        return s;
    }
    /// look axes: Input System default stick deadzone (InputSystem.settings.defaultDeadzoneMin, Unity's own default), rescaled; raw reads skip layout processors
    public static float LookDeadzone(float v) { float dz = InputSystem.settings.defaultDeadzoneMin; float m = Mathf.Abs(v); return m <= dz ? 0f : Mathf.Sign(v) * Mathf.Clamp01((m - dz) / (1f - dz)); }''')
ed('Runtime/InputRouter.cs','''        if (vr) vr.FlatLook(map.Axis(InAction.LookX) * 120f, map.Axis(InAction.LookY) * 45f);   // flat cockpit head look (deg)
        if (cam) { cam.lookYaw = map.Axis(InAction.LookX) * 180f; cam.lookPitch = map.Axis(InAction.LookY) * 30f; }   // camera orbit only (deg)''','''        float lx = InputMapper.LookDeadzone(map.Axis(InAction.LookX)), ly = InputMapper.LookDeadzone(map.Axis(InAction.LookY)); LookX = lx;
        bool lookOn = Mathf.Abs(lx) > 0.3f || Mathf.Abs(ly) > 0.3f;
        if (lookOn && !lookWasOn) { LookSource = map.Source(Mathf.Abs(lx) >= Mathf.Abs(ly) ? InAction.LookX : InAction.LookY); Debug.Log($"[Input] look active: yaw {lx * 120f:0} deg cockpit / {lx * 180f:0} deg chase, from {LookSource}"); }
        if (!lookOn && lookWasOn) Debug.Log("[Input] look released"); lookWasOn = lookOn; if (!lookOn) LookSource = "";
        if (vr) vr.FlatLook(lx * 120f, ly * 45f);   // flat cockpit head look (deg)
        if (cam) { cam.lookYaw = lx * 180f; cam.lookPitch = ly * 30f; }   // camera orbit only (deg)''')
ed('Runtime/InputRouter.cs','    public void ResetToStart()','    bool lookWasOn; [HideInInspector] public float LookX; [HideInInspector] public string LookSource = "";\n    public void ResetToStart()')
ed('Runtime/DebugHUD.cs','        if (info) DrawWheel(car);','        if (info) DrawWheel(car);\n        if (car.router && car.router.LookSource.Length > 0) GUI.Label(new Rect(UiSurface.Width * 0.5f - 200f, 64f, 400f, 16f), $"LOOK {car.router.LookX * 120f:0}° from {car.router.LookSource}", mono);   // look offset active (shown even with info hidden)')
ed('Runtime/SurfaceTag.cs','        if (n.StartsWith("COL_Road")) return SurfaceType.Asphalt;','        if (n.StartsWith("COL_Road") || n.StartsWith("COL_PitLane")) return SurfaceType.Asphalt;')
ed('Editor/TrackSetup.cs','if (sf) info.spawnPosition = sf.position;','''if (sf) info.spawnPosition = sf.position;
        if (sf) { // v1.2: centre the spawn between the measured COL_Road asphalt edges along the S/F line (the origin is the centreline point; edges are asymmetric there)
            var hw0 = info.HalfWidths.Count > 0 ? info.HalfWidths[0] : Vector2.zero; float yaw = SpawnYaw * Mathf.Deg2Rad; var fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)); var right = new Vector3(fwd.z, 0f, -fwd.x);
            Physics.SyncTransforms(); var sfl = vis.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name.StartsWith("Track_StartFinishLine"));
            float l = 0f, r = 0f, geoOff = (hw0.y - hw0.x) * 0.5f;
            for (int it = 0; it < 3; it++)
            {
                l = r = 0f; for (float d = 0f; d < 14f; d += 0.02f) { if (RoadAt(info.spawnPosition - right * d)) l = d; else break; } for (float d = 0f; d < 14f; d += 0.02f) { if (RoadAt(info.spawnPosition + right * d)) r = d; else break; }
                if (it < 2) info.spawnPosition += right * (r - l) * 0.5f;
            }
            float totalOff = Vector3.Dot(info.spawnPosition - sf.position, right), along = sfl ? Vector3.Dot(sfl.bounds.center - info.spawnPosition, fwd) : float.NaN;
            spawnCheck = $"spawn {info.spawnPosition.ToString("F2")}: asphalt (COL_Road) left {l:0.00} m / right {r:0.00} m (centre offset {(r - l) * 0.5f:+0.00;-0.00} m); spawn is {totalOff:+0.00;-0.00} m right of the origin marker (geo.pkl hl/hr at s=0 say {geoOff:+0.00;-0.00} m); Track_StartFinishLine centre {along:+0.00;-0.00} m along the heading from spawn";
        }''')
ed('Editor/TrackSetup.cs','    static float minYFromMeta = -92.13f;','''    static float minYFromMeta = -92.13f; static string spawnCheck = "";
    static bool RoadAt(Vector3 p) => Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var h, 20f) && h.collider.name.StartsWith("COL_Road");''')
ed('Editor/TrackSetup.cs','        L($"lap length (centreline)','        L(spawnCheck);\n        L($"lap length (centreline)')
ed('Editor/TrackSetup.cs','("COL_Road", "Track_Road"), ','("COL_Road", "Track_Road"), ("COL_PitLane", "Track_PitLane"), ')
# SmokeTest: lagunaoff
ed('Runtime/SmokeTest.cs','sc == "lagunakerb" ? 90f : 70f;','sc == "lagunakerb" ? 90f : sc == "lagunaoff" ? 200f : 70f;')
ed('Runtime/SmokeTest.cs','            case "lagunakerb":','            case "lagunaoff": LagunaOff(v); return;\n            case "lagunakerb":')
ed('Runtime/SmokeTest.cs',"    void LagunaReport()",open('/workspace/scirocco_sim/patches/lagunaoff.cs').read()+"    void LagunaReport()")
# tests
T=P+'/Assets/Tests/Editor/TrackTests.cs'; s=open(T).read()
if 'COL_PitLane' not in s: s=s.replace('Assert.AreEqual(SurfaceType.Kerb, SurfaceTag.FromName("COL_Kerbs"));','Assert.AreEqual(SurfaceType.Kerb, SurfaceTag.FromName("COL_Kerbs")); Assert.AreEqual(SurfaceType.Asphalt, SurfaceTag.FromName("COL_PitLane"));')
if 'LookDeadzone_' not in s:
    i=s.rfind('}'); s=s[:i]+'''    [Test] public void LookDeadzone_UsesInputSystemDefault()
    {
        float dz = UnityEngine.InputSystem.InputSystem.settings.defaultDeadzoneMin;
        Assert.AreEqual(0f, InputMapper.LookDeadzone(dz * 0.9f)); Assert.AreEqual(0f, InputMapper.LookDeadzone(-dz * 0.9f));
        Assert.AreEqual(1f, InputMapper.LookDeadzone(1f), 1e-5f); Assert.AreEqual(-1f, InputMapper.LookDeadzone(-1f), 1e-5f); Assert.Greater(InputMapper.LookDeadzone(0.75f), 0.6f);
    }
}
'''
open(T,'w').write(s); print('applied')
