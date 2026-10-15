// Placeholder interior (primitives only) + driver eye point + seat adjustment + recenter anchor.
// Car-local frame: x right, y up, z forward, origin mid-wheelbase. Eye point derived from H-point + anthropometry (all E, see CarParams).
using System.Collections.Generic; using UnityEngine;
public class Cockpit : MonoBehaviour
{
    public CarController car;
    public Transform eyeAnchor, recenter, wheelSpin, speedNeedle, rpmNeedle, root; public TextMesh speedText, rpmText, gearText;
    public Vector3 eyeDefault, hPoint, wheelCenter;
    public float seatForwardOffset, seatUpOffset;   // user offsets (m), saved in the input profile
    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();

    // @description - Height of the ground plane in the car frame at design ride height (wheel-center drop plus tire radius, below the corner origin).
    // @param {CarParams} carParams - car parameters
    // @return {float} - ground height in m (negative = below the car origin)
    public static float GroundY(CarParams carParams) => -(carParams.hubDropDesign + carParams.wheelRadius);
    // @description - Hip point (SAE H-point) of the driver in the car frame: laterally on the driver side, at the seat hip height above ground, behind the front axle by the configured distance. All values are estimates (E).
    // @param {CarParams} carParams - car parameters
    // @return {Vector3} - H-point position in m (x = right, y = up, z = forward)
    public static Vector3 HPoint(CarParams carParams) => new Vector3(-carParams.driverLateral, GroundY(carParams) + carParams.hipPointHeight, carParams.wheelbase * 0.5f - carParams.hipPointBehindFrontAxle);
    // @description - Eye position of the seated driver. The eye lies along the reclined torso above the H-point: torso length = sitting-eye ratio x stature - H-point height above the seat (E, anthropometric tables); the torso leans back by the seat back angle.
    // @param {CarParams} carParams - car parameters
    // @return {Vector3} - eye position in m in the car frame
    public static Vector3 EyePoint(CarParams carParams)
    {
        float torsoLength = carParams.sittingEyeRatio * carParams.driverStature - carParams.hipPointAboveSeat, seatBackAngleRad = carParams.seatBackAngle * Mathf.Deg2Rad;   // H-point -> eye along the reclined torso
        return HPoint(carParams) + new Vector3(0f, torsoLength * Mathf.Cos(seatBackAngleRad), -torsoLength * Mathf.Sin(seatBackAngleRad));
    }
    // @description - Center of the steering wheel relative to the H-point (ahead and above it, E).
    // @param {CarParams} carParams - car parameters
    // @return {Vector3} - steering-wheel center in m in the car frame
    public static Vector3 WheelCenter(CarParams carParams) => HPoint(carParams) + new Vector3(0f, carParams.wheelCenterAboveHipPoint, carParams.wheelCenterAheadOfHipPoint);
    // @description - Moves the driver eye anchor by the user seat adjustment, limited to the seat travel range of the car.
    // @param {float} forwardOffset - seat movement forward in m (+ = forward)
    // @param {float} upOffset - seat height offset in m (+ = up; a VR viewing offset)
    // @return {Void} - nothing; stores the clamped offsets
    public void ApplySeat(float forwardOffset, float upOffset)
    {
        var carParams = car.carParams; seatForwardOffset = Mathf.Clamp(forwardOffset, -carParams.seatTravelAft, carParams.seatTravelFore); seatUpOffset = Mathf.Clamp(upOffset, -carParams.seatTravelDown, carParams.seatTravelUp);
        if (eyeAnchor) eyeAnchor.localPosition = eyeDefault + new Vector3(0f, seatUpOffset, seatForwardOffset);
    }
    // @description - Re-centers a tracked VR head: rotates and shifts the recenter transform so the current head pose lands on the eye point, facing forward.
    // @param {Transform} head - the tracked camera (child of the recenter transform)
    // @return {Void} - nothing
    public void Recenter(Transform head)   // head = tracked camera under 'recenter'; puts the current head pose on the eye point, facing forward
    {
        if (!head || !recenter) return;
        float headYawDeg = head.localEulerAngles.y; recenter.localRotation = Quaternion.Euler(0f, -headYawDeg, 0f);
        recenter.localPosition = -(recenter.localRotation * head.localPosition);
    }
    // @description - Returns a shared simple opaque material for a color (one material per color is created and cached).
    // @param {Color} color - albedo color
    // @return {Material} - the cached material
    static Material Mat(Color color)
    {
        if (mats.TryGetValue(color, out var material) && material) return material;
        material = new Material(Shader.Find("Standard")) { color = color }; material.SetFloat("_Glossiness", 0.2f); mats[color] = material; return material;   // one shared simple material per color
    }
    // @description - Creates a collider-free unlit-shadow cube used as placeholder cockpit geometry.
    // @param {string} objectName - name of the new object
    // @param {Transform} parent - transform to attach to
    // @param {Vector3} position - local position in m
    // @param {Vector3} scale - local size in m
    // @param {Color} color - albedo color
    // @param {Quaternion?} rotation - optional local rotation (identity if null)
    // @return {Transform} - the new object transform
    Transform Box(string objectName, Transform parent, Vector3 position, Vector3 scale, Color color, Quaternion? rotation = null)
    {
        var cubeObject = GameObject.CreatePrimitive(PrimitiveType.Cube); cubeObject.name = objectName; Destroy(cubeObject.GetComponent<Collider>()); cubeObject.layer = 2;
        cubeObject.transform.SetParent(parent, false); cubeObject.transform.localPosition = position; cubeObject.transform.localScale = scale; cubeObject.transform.localRotation = rotation ?? Quaternion.identity;
        var meshRenderer = cubeObject.GetComponent<MeshRenderer>(); meshRenderer.sharedMaterial = Mat(color); meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; meshRenderer.receiveShadows = false; return cubeObject.transform;   // the placeholder body box would otherwise shade the whole interior
    }
    // @description - Creates a thin box stretched between two points (pillars, wheel rim segments, spokes).
    // @param {string} objectName - name of the new object
    // @param {Transform} parent - transform to attach to
    // @param {Vector3} startPoint - first end point, local m
    // @param {Vector3} endPoint - second end point, local m
    // @param {float} thickness - cross-section thickness in m
    // @param {Color} color - albedo color
    // @return {Transform} - the new strut transform
    Transform Strut(string objectName, Transform parent, Vector3 startPoint, Vector3 endPoint, float thickness, Color color)
    {
        var direction = endPoint - startPoint; return Box(objectName, parent, (startPoint + endPoint) * 0.5f, new Vector3(thickness, thickness, direction.magnitude), color, Quaternion.LookRotation(direction));
    }
    // @description - Creates a centered 3D text label for the gauges.
    // @param {string} objectName - name of the new object
    // @param {Transform} parent - transform to attach to
    // @param {Vector3} position - local position in m
    // @param {float} size - character size in m
    // @param {Color} color - text color
    // @return {TextMesh} - the text mesh
    TextMesh Text(string objectName, Transform parent, Vector3 position, float size, Color color)
    {
        var textObject = new GameObject(objectName); textObject.transform.SetParent(parent, false); textObject.transform.localPosition = position;
        var textMesh = textObject.AddComponent<TextMesh>(); var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textMesh.font = font; textMesh.GetComponent<MeshRenderer>().sharedMaterial = font.material; textMesh.fontSize = 64; textMesh.characterSize = size; textMesh.anchor = TextAnchor.MiddleCenter; textMesh.color = color; return textMesh;
    }
    // @description - Builds the placeholder cockpit from the car parameters: dash, bonnet, roof, A/B pillars, doors, floor, seat, a steering wheel that turns with the real column angle, and the speed/rpm gauges. Dimensions are estimates (E) except the 1.28 m overall height (S).
    // @return {Void} - nothing
    public void Build()
    {
        if (root) return; var carParams = car.carParams;
        eyeDefault = EyePoint(carParams); hPoint = HPoint(carParams); wheelCenter = WheelCenter(carParams);
        root = new GameObject("Cockpit").transform; root.SetParent(car.transform, false);
        float groundY = GroundY(carParams), roofY = groundY + 1.28f - 0.07f;   // overall height 1.28 m (S) minus roof skin + headliner (E)
        Color trim = new Color(0.30f, 0.29f, 0.27f), rimColor = new Color(0.07f, 0.07f, 0.07f), paint = new Color(0.55f, 0.08f, 0.06f), glassFrame = new Color(0.05f, 0.05f, 0.05f);
        Vector3 wheelCenterPosition = wheelCenter; float cowlZ = wheelCenterPosition.z + 0.55f, cowlY = wheelCenterPosition.y + 0.17f, halfW = 0.68f;   // cowl/windscreen base and interior half width (E)
        float windscreenRake = 57f * Mathf.Deg2Rad, roofFrontZ = cowlZ - (roofY - cowlY) * Mathf.Tan(windscreenRake);   // windscreen rake from vertical (E, wedge coupe)
        Box("Dash", root, new Vector3(0f, wheelCenterPosition.y + 0.06f, wheelCenterPosition.z + 0.33f), new Vector3(2f * halfW, 0.16f, 0.36f), trim);
        Box("Binnacle", root, new Vector3(-carParams.driverLateral, wheelCenterPosition.y + 0.15f, wheelCenterPosition.z + 0.24f), new Vector3(0.42f, 0.04f, 0.2f), trim);
        Box("Bonnet", root, new Vector3(0f, cowlY - 0.04f, (cowlZ + carParams.wheelbase * 0.5f + 0.8f) * 0.5f), new Vector3(1.5f, 0.02f, carParams.wheelbase * 0.5f + 0.8f - cowlZ), paint);
        Box("Roof", root, new Vector3(0f, roofY, roofFrontZ - 0.65f), new Vector3(2f * halfW, 0.03f, 1.3f), trim);
        foreach (float side in new[] { -1f, 1f })
        {
            Strut("APillar", root, new Vector3(side * halfW, cowlY, cowlZ), new Vector3(side * (halfW - 0.06f), roofY, roofFrontZ), 0.06f, glassFrame);
            Box("Door", root, new Vector3(side * (halfW + 0.02f), (groundY + 0.3f + cowlY) * 0.5f, hPoint.z + 0.1f), new Vector3(0.04f, cowlY - groundY - 0.3f, 1.3f), trim);
            Strut("BPillar", root, new Vector3(side * (halfW + 0.02f), cowlY, hPoint.z - 0.55f), new Vector3(side * (halfW - 0.05f), roofY, hPoint.z - 0.75f), 0.09f, glassFrame);
        }
        Box("Floor", root, new Vector3(0f, groundY + 0.18f, hPoint.z + 0.3f), new Vector3(2f * halfW, 0.02f, 1.8f), trim);
        Box("Seat", root, hPoint + new Vector3(0f, -0.08f, 0.05f), new Vector3(0.5f, 0.1f, 0.5f), new Color(0.2f, 0.2f, 0.22f));
        Box("SeatBack", root, hPoint + new Vector3(0f, 0.3f, -0.2f), new Vector3(0.5f, 0.6f, 0.08f), new Color(0.2f, 0.2f, 0.22f), Quaternion.Euler(-carParams.seatBackAngle, 0f, 0f));
        // steering wheel: tilted column, spin child rotates with the real column angle (1:1, full 2 x lock)
        var steeringWheelTilt = new GameObject("SteeringWheel").transform; steeringWheelTilt.SetParent(root, false); steeringWheelTilt.localPosition = wheelCenterPosition; steeringWheelTilt.localRotation = Quaternion.Euler(carParams.wheelTiltDeg, 0f, 0f);
        wheelSpin = new GameObject("Spin").transform; wheelSpin.SetParent(steeringWheelTilt, false);
        int rimSegmentCount = 24; float wheelRadius = carParams.wheelDiameter * 0.5f;
        for (int i = 0; i < rimSegmentCount; i++)
        {
            float segmentStartAngle = i * 2f * Mathf.PI / rimSegmentCount, segmentEndAngle = (i + 1) * 2f * Mathf.PI / rimSegmentCount;
            Strut("Rim", wheelSpin, new Vector3(wheelRadius * Mathf.Sin(segmentStartAngle), wheelRadius * Mathf.Cos(segmentStartAngle), 0f), new Vector3(wheelRadius * Mathf.Sin(segmentEndAngle), wheelRadius * Mathf.Cos(segmentEndAngle), 0f), 0.028f, rimColor);
        }
        foreach (float spokeAngleDeg in new[] { 90f, 180f, 270f }) { float spokeAngleRad = spokeAngleDeg * Mathf.Deg2Rad; Strut("Spoke", wheelSpin, Vector3.zero, new Vector3(wheelRadius * Mathf.Sin(spokeAngleRad), wheelRadius * Mathf.Cos(spokeAngleRad), 0f), 0.025f, new Color(0.65f, 0.65f, 0.68f)); }
        Box("TopMark", wheelSpin, new Vector3(0f, wheelRadius, -0.005f), new Vector3(0.02f, 0.035f, 0.035f), new Color(0.9f, 0.6f, 0.1f));
        Strut("Column", steeringWheelTilt, new Vector3(0f, 0f, 0.02f), new Vector3(0f, 0f, 0.35f), 0.05f, trim);
        // gauges (face the driver, same tilt as the binnacle top)
        var gaugePanel = new GameObject("Gauges").transform; gaugePanel.SetParent(root, false); gaugePanel.localPosition = new Vector3(-carParams.driverLateral, wheelCenterPosition.y + 0.08f, wheelCenterPosition.z + 0.2f); gaugePanel.localRotation = Quaternion.Euler(carParams.wheelTiltDeg, 0f, 0f);
        speedNeedle = Gauge(gaugePanel, -0.09f, "mph", out speedText); rpmNeedle = Gauge(gaugePanel, 0.09f, "rpm", out rpmText);
        gearText = Text("Gear", gaugePanel, new Vector3(0f, -0.02f, -0.006f), 0.006f, new Color(1f, 0.8f, 0.2f));
        eyeAnchor = new GameObject("EyeAnchor").transform; eyeAnchor.SetParent(car.transform, false);
        recenter = new GameObject("RecenterOffset").transform; recenter.SetParent(eyeAnchor, false);
        ApplySeat(seatForwardOffset, seatUpOffset);
    }
    // @description - Builds one round dial: face, needle pivot with needle, label and value text.
    // @param {Transform} transform - parent (the gauge panel)
    // @param {float} centerX - horizontal position of the dial center in m
    // @param {string} label - unit label shown under the dial
    // @param {TextMesh} valueText - receives the numeric value text
    // @return {Transform} - the needle pivot to rotate
    Transform Gauge(Transform transform, float centerX, string label, out TextMesh valueText)
    {
        Box("Face", transform, new Vector3(centerX, 0f, 0f), new Vector3(0.14f, 0.14f, 0.005f), new Color(0.02f, 0.02f, 0.02f));
        var pivot = new GameObject("NeedlePivot").transform; pivot.SetParent(transform, false); pivot.localPosition = new Vector3(centerX, 0f, -0.004f);
        Box("Needle", pivot, new Vector3(0f, 0.03f, 0f), new Vector3(0.004f, 0.06f, 0.002f), new Color(1f, 0.35f, 0.1f));
        Text(label, transform, new Vector3(centerX, -0.045f, -0.004f), 0.0022f, Color.white);
        valueText = Text(label + "Value", transform, new Vector3(centerX, -0.022f, -0.004f), 0.0035f, Color.white); return pivot;
    }
    // @description - Animates the cockpit each frame: hides the 3D text behind the pause menu, turns the wheel with the handwheel angle and sweeps the dials. Dial sweep is 240 deg over 0-150 mph and 0-8000 rpm (E).
    // @return {Void} - nothing
    void LateUpdate()
    {
        if (!root || car == null || car.engine == null) return;
        bool menu = car.router && car.router.menuOpen; speedText.gameObject.SetActive(!menu); rpmText.gameObject.SetActive(!menu); gearText.gameObject.SetActive(!menu);   // TextMesh ignores depth: hide behind the menu panel
        wheelSpin.localRotation = Quaternion.Euler(0f, 0f, -car.handwheelDeg);   // + handwheel = clockwise as seen by the driver
        float speedMph = car.GetComponent<Rigidbody>().velocity.magnitude * DebugHUD.MphPerMs, engineRpm = car.engine.rpm;
        // dial sweep: 240 deg over 0-150 mph and 0-8000 rpm (E; display in mph per Taylor)
        speedNeedle.localRotation = Quaternion.Euler(0f, 0f, 120f - Mathf.Clamp(speedMph, 0f, 150f) * 240f / 150f);
        rpmNeedle.localRotation = Quaternion.Euler(0f, 0f, 120f - Mathf.Clamp(engineRpm, 0f, 8000f) * 240f / 8000f);
        speedText.text = speedMph.ToString("0"); rpmText.text = engineRpm.ToString("0");
        gearText.text = car.drivetrain.gear == 0 ? "N" : car.drivetrain.gear < 0 ? "R" : car.drivetrain.gear.ToString();
    }
}
