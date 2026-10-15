// Placeholder interior (primitives only) + driver eye point + seat adjustment + recenter anchor.
// Car-local frame: x right, y up, z forward, origin mid-wheelbase. Eye point derived from H-point + anthropometry (all E, see CarParams).
using System.Collections.Generic; using UnityEngine;
public class Cockpit : MonoBehaviour
{
    public CarController car;
    public Transform eyeAnchor, recenter, wheelSpin, speedNeedle, rpmNeedle, root; public TextMesh speedText, rpmText, gearText;
    public Vector3 eyeDefault, hPoint, wheelCentre;
    public float seatForwardOffset, seatUpOffset;   // user offsets (m), saved in the input profile
    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();

    // @description - Ground y.
    // @param {CarParams} carParams - car params
    // @return {float} - the ground y
    public static float GroundY(CarParams carParams) => -(carParams.hubDropDesign + carParams.wheelRadius);
    // @description - H point.
    // @param {CarParams} carParams - car params
    // @return {Vector3} - the h point
    public static Vector3 HPoint(CarParams carParams) => new Vector3(-carParams.driverLateral, GroundY(carParams) + carParams.hipPointHeight, carParams.wheelbase * 0.5f - carParams.hipPointBehindFrontAxle);
    // @description - Eye point.
    // @param {CarParams} carParams - car params
    // @return {Vector3} - the eye point
    public static Vector3 EyePoint(CarParams carParams)
    {
        float torso = carParams.sittingEyeRatio * carParams.driverStature - carParams.hipPointAboveSeat, a = carParams.seatBackAngle * Mathf.Deg2Rad;   // H-point -> eye along the reclined torso
        return HPoint(carParams) + new Vector3(0f, torso * Mathf.Cos(a), -torso * Mathf.Sin(a));
    }
    // @description - Wheel centre.
    // @param {CarParams} carParams - car params
    // @return {Vector3} - the wheel centre
    public static Vector3 WheelCentre(CarParams carParams) => HPoint(carParams) + new Vector3(0f, carParams.wheelCentreAboveHipPoint, carParams.wheelCentreAheadOfHipPoint);
    // @description - Applies the seat.
    // @param {float} forwardOffset - forward offset
    // @param {float} upOffset - up offset
    // @return {Void} - nothing
    public void ApplySeat(float forwardOffset, float upOffset)
    {
        var p = car.carParams; seatForwardOffset = Mathf.Clamp(forwardOffset, -p.seatTravelAft, p.seatTravelFore); seatUpOffset = Mathf.Clamp(upOffset, -p.seatTravelDown, p.seatTravelUp);
        if (eyeAnchor) eyeAnchor.localPosition = eyeDefault + new Vector3(0f, seatUpOffset, seatForwardOffset);
    }
    // @description - Recenter.
    // @param {Transform} head - head
    // @return {Void} - nothing
    public void Recenter(Transform head)   // head = tracked camera under 'recenter'; puts the current head pose on the eye point, facing forward
    {
        if (!head || !recenter) return;
        float yaw = head.localEulerAngles.y; recenter.localRotation = Quaternion.Euler(0f, -yaw, 0f);
        recenter.localPosition = -(recenter.localRotation * head.localPosition);
    }
    // @description - Mat.
    // @param {Color} c - value of type Color
    // @return {Material} - the mat
    static Material Mat(Color c)
    {
        if (mats.TryGetValue(c, out var m) && m) return m;
        m = new Material(Shader.Find("Standard")) { color = c }; m.SetFloat("_Glossiness", 0.2f); mats[c] = m; return m;   // one shared simple material per colour
    }
    // @description - Box.
    // @param {string} objectName - object name
    // @param {Transform} parent - parent
    // @param {Vector3} pos - pos
    // @param {Vector3} scale - scale
    // @param {Color} color - color
    // @param {Quaternion?} rot - rot
    // @return {Transform} - the box
    Transform Box(string objectName, Transform parent, Vector3 pos, Vector3 scale, Color color, Quaternion? rot = null)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = objectName; Destroy(g.GetComponent<Collider>()); g.layer = 2;
        g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localRotation = rot ?? Quaternion.identity;
        var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = Mat(color); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; return g.transform;   // the placeholder body box would otherwise shade the whole interior
    }
    // @description - Strut.
    // @param {string} objectName - object name
    // @param {Transform} parent - parent
    // @param {Vector3} a - value of type Vector3
    // @param {Vector3} b - value of type Vector3
    // @param {float} t - value of type float
    // @param {Color} color - color
    // @return {Transform} - the strut
    Transform Strut(string objectName, Transform parent, Vector3 a, Vector3 b, float t, Color color)
    {
        var d = b - a; return Box(objectName, parent, (a + b) * 0.5f, new Vector3(t, t, d.magnitude), color, Quaternion.LookRotation(d));
    }
    // @description - Text.
    // @param {string} objectName - object name
    // @param {Transform} parent - parent
    // @param {Vector3} pos - pos
    // @param {float} size - size
    // @param {Color} color - color
    // @return {TextMesh} - the text
    TextMesh Text(string objectName, Transform parent, Vector3 pos, float size, Color color)
    {
        var g = new GameObject(objectName); g.transform.SetParent(parent, false); g.transform.localPosition = pos;
        var t = g.AddComponent<TextMesh>(); var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.font = f; t.GetComponent<MeshRenderer>().sharedMaterial = f.material; t.fontSize = 64; t.characterSize = size; t.anchor = TextAnchor.MiddleCenter; t.color = color; return t;
    }
    // @description - Build (see the body for details).
    // @return {Void} - nothing
    public void Build()
    {
        if (root) return; var p = car.carParams;
        eyeDefault = EyePoint(p); hPoint = HPoint(p); wheelCentre = WheelCentre(p);
        root = new GameObject("Cockpit").transform; root.SetParent(car.transform, false);
        float gy = GroundY(p), roofY = gy + 1.28f - 0.07f;   // overall height 1.28 m (S) minus roof skin + headliner (E)
        Color trim = new Color(0.30f, 0.29f, 0.27f), rimC = new Color(0.07f, 0.07f, 0.07f), paint = new Color(0.55f, 0.08f, 0.06f), glassFrame = new Color(0.05f, 0.05f, 0.05f);
        Vector3 W = wheelCentre; float cowlZ = W.z + 0.55f, cowlY = W.y + 0.17f, halfW = 0.68f;   // cowl/windscreen base and interior half width (E)
        float rake = 57f * Mathf.Deg2Rad, roofFrontZ = cowlZ - (roofY - cowlY) * Mathf.Tan(rake);   // windscreen rake from vertical (E, wedge coupe)
        Box("Dash", root, new Vector3(0f, W.y + 0.06f, W.z + 0.33f), new Vector3(2f * halfW, 0.16f, 0.36f), trim);
        Box("Binnacle", root, new Vector3(-p.driverLateral, W.y + 0.15f, W.z + 0.24f), new Vector3(0.42f, 0.04f, 0.2f), trim);
        Box("Bonnet", root, new Vector3(0f, cowlY - 0.04f, (cowlZ + p.wheelbase * 0.5f + 0.8f) * 0.5f), new Vector3(1.5f, 0.02f, p.wheelbase * 0.5f + 0.8f - cowlZ), paint);
        Box("Roof", root, new Vector3(0f, roofY, roofFrontZ - 0.65f), new Vector3(2f * halfW, 0.03f, 1.3f), trim);
        foreach (float s in new[] { -1f, 1f })
        {
            Strut("APillar", root, new Vector3(s * halfW, cowlY, cowlZ), new Vector3(s * (halfW - 0.06f), roofY, roofFrontZ), 0.06f, glassFrame);
            Box("Door", root, new Vector3(s * (halfW + 0.02f), (gy + 0.3f + cowlY) * 0.5f, hPoint.z + 0.1f), new Vector3(0.04f, cowlY - gy - 0.3f, 1.3f), trim);
            Strut("BPillar", root, new Vector3(s * (halfW + 0.02f), cowlY, hPoint.z - 0.55f), new Vector3(s * (halfW - 0.05f), roofY, hPoint.z - 0.75f), 0.09f, glassFrame);
        }
        Box("Floor", root, new Vector3(0f, gy + 0.18f, hPoint.z + 0.3f), new Vector3(2f * halfW, 0.02f, 1.8f), trim);
        Box("Seat", root, hPoint + new Vector3(0f, -0.08f, 0.05f), new Vector3(0.5f, 0.1f, 0.5f), new Color(0.2f, 0.2f, 0.22f));
        Box("SeatBack", root, hPoint + new Vector3(0f, 0.3f, -0.2f), new Vector3(0.5f, 0.6f, 0.08f), new Color(0.2f, 0.2f, 0.22f), Quaternion.Euler(-p.seatBackAngle, 0f, 0f));
        // steering wheel: tilted column, spin child rotates with the real column angle (1:1, full 2 x lock)
        var sw = new GameObject("SteeringWheel").transform; sw.SetParent(root, false); sw.localPosition = W; sw.localRotation = Quaternion.Euler(p.wheelTiltDeg, 0f, 0f);
        wheelSpin = new GameObject("Spin").transform; wheelSpin.SetParent(sw, false);
        int n = 24; float R = p.wheelDiameter * 0.5f;
        for (int i = 0; i < n; i++)
        {
            float a0 = i * 2f * Mathf.PI / n, a1 = (i + 1) * 2f * Mathf.PI / n;
            Strut("Rim", wheelSpin, new Vector3(R * Mathf.Sin(a0), R * Mathf.Cos(a0), 0f), new Vector3(R * Mathf.Sin(a1), R * Mathf.Cos(a1), 0f), 0.028f, rimC);
        }
        foreach (float ang in new[] { 90f, 180f, 270f }) { float a = ang * Mathf.Deg2Rad; Strut("Spoke", wheelSpin, Vector3.zero, new Vector3(R * Mathf.Sin(a), R * Mathf.Cos(a), 0f), 0.025f, new Color(0.65f, 0.65f, 0.68f)); }
        Box("TopMark", wheelSpin, new Vector3(0f, R, -0.005f), new Vector3(0.02f, 0.035f, 0.035f), new Color(0.9f, 0.6f, 0.1f));
        Strut("Column", sw, new Vector3(0f, 0f, 0.02f), new Vector3(0f, 0f, 0.35f), 0.05f, trim);
        // gauges (face the driver, same tilt as the binnacle top)
        var g = new GameObject("Gauges").transform; g.SetParent(root, false); g.localPosition = new Vector3(-p.driverLateral, W.y + 0.08f, W.z + 0.2f); g.localRotation = Quaternion.Euler(p.wheelTiltDeg, 0f, 0f);
        speedNeedle = Gauge(g, -0.09f, "mph", out speedText); rpmNeedle = Gauge(g, 0.09f, "rpm", out rpmText);
        gearText = Text("Gear", g, new Vector3(0f, -0.02f, -0.006f), 0.006f, new Color(1f, 0.8f, 0.2f));
        eyeAnchor = new GameObject("EyeAnchor").transform; eyeAnchor.SetParent(car.transform, false);
        recenter = new GameObject("RecenterOffset").transform; recenter.SetParent(eyeAnchor, false);
        ApplySeat(seatForwardOffset, seatUpOffset);
    }
    // @description - Gauge.
    // @param {Transform} transform - transform
    // @param {float} x - value of type float
    // @param {string} label - label
    // @param {out TextMesh} txt - txt
    // @return {Transform} - the gauge
    Transform Gauge(Transform transform, float x, string label, out TextMesh txt)
    {
        Box("Face", transform, new Vector3(x, 0f, 0f), new Vector3(0.14f, 0.14f, 0.005f), new Color(0.02f, 0.02f, 0.02f));
        var pivot = new GameObject("NeedlePivot").transform; pivot.SetParent(transform, false); pivot.localPosition = new Vector3(x, 0f, -0.004f);
        Box("Needle", pivot, new Vector3(0f, 0.03f, 0f), new Vector3(0.004f, 0.06f, 0.002f), new Color(1f, 0.35f, 0.1f));
        Text(label, transform, new Vector3(x, -0.045f, -0.004f), 0.0022f, Color.white);
        txt = Text(label + "Value", transform, new Vector3(x, -0.022f, -0.004f), 0.0035f, Color.white); return pivot;
    }
    // @description - Late update.
    // @return {Void} - nothing
    void LateUpdate()
    {
        if (!root || car == null || car.engine == null) return;
        bool menu = car.router && car.router.menuOpen; speedText.gameObject.SetActive(!menu); rpmText.gameObject.SetActive(!menu); gearText.gameObject.SetActive(!menu);   // TextMesh ignores depth: hide behind the menu panel
        wheelSpin.localRotation = Quaternion.Euler(0f, 0f, -car.handwheelDeg);   // + handwheel = clockwise as seen by the driver
        float v = car.GetComponent<Rigidbody>().velocity.magnitude * DebugHUD.MphPerMs, rpm = car.engine.rpm;
        // dial sweep: 240 deg over 0-150 mph and 0-8000 rpm (E; display in mph per Taylor)
        speedNeedle.localRotation = Quaternion.Euler(0f, 0f, 120f - Mathf.Clamp(v, 0f, 150f) * 240f / 150f);
        rpmNeedle.localRotation = Quaternion.Euler(0f, 0f, 120f - Mathf.Clamp(rpm, 0f, 8000f) * 240f / 8000f);
        speedText.text = v.ToString("0"); rpmText.text = rpm.ToString("0");
        gearText.text = car.drivetrain.gear == 0 ? "N" : car.drivetrain.gear < 0 ? "R" : car.drivetrain.gear.ToString();
    }
}
