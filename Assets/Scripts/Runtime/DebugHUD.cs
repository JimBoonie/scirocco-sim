using UnityEngine;
public class DebugHUD : MonoBehaviour
{
    public CarController car; GUIStyle mono, big;
    // @description - Draws the debug HUD on the flat screen at the design UI scale, unless the VR panel draws it instead.
    // @return {Void} - nothing
    void OnGUI() { if (VrPanel.Active) return; UiSurface.Begin(out var previousMatrix); Draw(); UiSurface.End(previousMatrix); }
    // @description - Draws the telemetry HUD: speed, rpm, gear, pedals, per-wheel load, slip and force, optional tire thermal table, steering wheel angle, force feedback status and clipping meter, joint reaction forces, and the bottom driver strip with hints (stalled, ignition off, uncalibrated pedals). Display only; nothing here changes the simulation.
    // @return {Void} - nothing
    public void Draw()
    {
        if (!car || car.engine == null) return;
        if (mono == null) { mono = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) }; mono.normal.textColor = Color.white; }
        bool info = HudVisible(car);   // H toggles the grey info boxes (telemetry / FFB / joints); the bottom input strip is always shown
        var body = car.GetComponent<Rigidbody>();
        string gearText = car.drivetrain.gear == 0 ? "N" : car.drivetrain.gear < 0 ? "R" : car.drivetrain.gear.ToString();
        string text = $"handling baseline (single)  tire {car.carParams.tirePreset} {(car.carParams.tireCompound == CarParams.TireCompound.ExperimentalViscoelastic ? "[Experimental viscoelastic]" : "")}\n" + $"{body.velocity.magnitude * MphPerMs,3:0} mph  {car.engine.rpm,4:0} rpm  gear {gearText}  MAP {car.engine.manifoldPressureRatio:0.00}  thr {car.engine.throttleArea:0.000}\n" +
                   $"T {car.throttle:0.00} B {car.brake:0.00} C {car.clutch:0.00}  starter {car.engine.starterTorque,3:0}  cslip {car.drivetrain.clutchSlip:0.0}  steer {car.steer:0.0}°\n";
        foreach (var wheel in new[] { car.frontLeftWheel, car.frontRightWheel, car.rearLeftWheel, car.rearRightWheel })
            text += $"{wheel.name}: Fz {wheel.verticalLoad,5:0} SR {wheel.slipRatio,6:0.000} SA {wheel.relaxedSlipAngle * Mathf.Rad2Deg,5:0.0} Fx {wheel.force.x,5:0} Fy {wheel.force.y,5:0} trv {wheel.travel * 1000f,3:0}mm\n";
        if (car.frontLeftWheel.thermal != null)
        {   // tire thermal model: tread surface inner/mid/outer (F), hot pressure (psi), grip factor from temperature+pressure
            text += "Tires  tread I / M / O °F   bulk  gas   psi   grip\n";
            foreach (var wheel in new[] { car.frontLeftWheel, car.frontRightWheel, car.rearLeftWheel, car.rearRightWheel })
            {
                var thermal = wheel.thermal; if (thermal == null) continue;
                text += $"{wheel.name}: {ToFahrenheit(thermal.surfaceTemperatures[0]),4:0}/{ToFahrenheit(thermal.surfaceTemperatures[1]),4:0}/{ToFahrenheit(thermal.surfaceTemperatures[2]),4:0}  {ToFahrenheit(thermal.bulkTemperature),4:0} {ToFahrenheit(thermal.gasTemperature),4:0}  {thermal.gaugePressure / TireThermal.PaPerPsi,4:0.0}  {wheel.thermalScale.lateralFriction * 100f,3:0}%\n";
            }
        }

        if (VrPanel.Active && TrackManager.Instance && TrackManager.Instance.Credit.Length > 0) text += TrackManager.Instance.Credit + "\n";   // VR: credit on the HUD panel
        if (info) { text = text.TrimEnd('\n'); Box(8f, 8f, text); }
        if (info) DrawWheel(car);
        if (car.router && car.router.LookSource.Length > 0) GUI.Label(new Rect(UiSurface.Width * 0.5f - 200f, 64f, 400f, 16f), $"LOOK {car.router.LookX * 120f:0}° from {car.router.LookSource}", mono);   // look offset active (shown even with info hidden)
        // driver strip: gear / pedals / ignition, large, bottom centre
        if (big == null) { big = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; big.normal.textColor = Color.white; }
        var engine = car.engine; float screenWidth = UiSurface.Width, screenHeight = UiSurface.Height;
        string strip = $"GEAR {gearText}    THROTTLE {car.throttle * 100f,3:0}%    CLUTCH {car.clutch * 100f,3:0}%    BRAKE {car.brake * 100f,3:0}%    HANDBRAKE {car.handbrakeInput * 100f,3:0}%    IGN {(engine.ignition ? "ON" : "OFF")}  ENGINE {engine.State} {engine.rpm,4:0} rpm" + (car.autoClutch && car.autoClutchPedal > 0f ? $"  (auto clutch {car.autoClutchPedal * 100f:0}%)" : "");
        float stripWidth = Mathf.Min(screenWidth - 20f, 1240f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(screenWidth * 0.5f - stripWidth * 0.5f, screenHeight - 92f, stripWidth, 36f), Texture2D.whiteTexture); GUI.color = Color.white;
        GUI.Label(new Rect(screenWidth * 0.5f - stripWidth * 0.5f, screenHeight - 92f, stripWidth, 36f), strip, big);
        string hint = car.router && car.router.hintNeutral ? "Neutral - press E for 1st" : !engine.ignition ? "Ignition off - press I, then hold T to start" : (!engine.combusting && !engine.starter ? (car.drivetrain.gear != 0 && car.GetComponent<Rigidbody>() && car.GetComponent<Rigidbody>().velocity.magnitude > 2f ? "STALLED - rolling in gear: release the clutch to bump-start, or hold T (starter)" : "STALLED - hold T (starter) to restart  (I = ignition on/off)") : "");
        var mapper = car.router && car.router.map != null ? car.router.map : null; int uncalibratedCount = mapper != null ? mapper.UncalibratedCount() : 0;
        if (uncalibratedCount > 0) hint += (hint.Length > 0 ? "    " : "") + $"{uncalibratedCount} pedal axis not calibrated (read from rest) - F1 > Calibrate";
        if (hint.Length > 0) { var hintStyle = new GUIStyle(big) { fontSize = 16 }; hintStyle.normal.textColor = engine.ignition && !engine.combusting && !engine.starter ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.85f, 0.3f); if (engine.ignition && !engine.combusting && !engine.starter) hintStyle.fontSize = 24; GUI.Label(new Rect(screenWidth * 0.5f - stripWidth * 0.5f, screenHeight - 130f, stripWidth, 34f), hint, hintStyle); }
        var forceFeedback = car.GetComponent<FfbController>();
        if (forceFeedback && info)
        {
            float panelX = UiSurface.Width - 238f, panelY = 70f; var profile = car.router && car.router.map != null ? car.router.map.profile : null; float cap = profile != null ? profile.forceFeedbackMaxOutputPercent * 0.01f : 1f;
            string forceFeedbackText = $"FFB {forceFeedback.status}\ncol {forceFeedback.totalTorque,5:0.0} Nm (tire {car.columnTorque,4:0.0} stop {forceFeedback.stopTorque,3:0.0})\ncmd {forceFeedback.command * 100f,4:0}%  clip {forceFeedback.clippingPercent,3:0}%"; var textSize = mono.CalcSize(new GUIContent(forceFeedbackText));
            GUI.Box(new Rect(panelX, panelY, 230f, textSize.y + 18f), GUIContent.none); GUI.Label(new Rect(panelX + 4f, panelY + 2f, 224f, textSize.y), forceFeedbackText, mono); panelY += textSize.y + 6f;
            float clippingMeter = profile != null && profile.wheelbaseMaxTorqueNm > 0f ? Mathf.Clamp(forceFeedback.totalTorque / profile.wheelbaseMaxTorqueNm * profile.forceFeedbackStrengthPercent * 0.01f, -1.5f, 1.5f) : 0f;   // clipping meter
            float centerX = panelX + 115f, barWidth = 100f; GUI.color = Color.gray; GUI.DrawTexture(new Rect(panelX + 10f, panelY, 210f, 7f), Texture2D.whiteTexture);
            GUI.color = Mathf.Abs(clippingMeter) > cap ? Color.red : Color.green; GUI.DrawTexture(clippingMeter >= 0 ? new Rect(centerX, panelY, Mathf.Min(clippingMeter, 1f) * barWidth, 7f) : new Rect(centerX + Mathf.Max(clippingMeter, -1f) * barWidth, panelY, -Mathf.Max(clippingMeter, -1f) * barWidth, 7f), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.DrawTexture(new Rect(centerX - 1f, panelY - 2f, 2f, 11f), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(centerX + cap * barWidth, panelY - 2f, 2f, 11f), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(centerX - cap * barWidth, panelY - 2f, 2f, 11f), Texture2D.whiteTexture);
        }
        if (info) { Box(8f, UiSurface.Height - 230f, JointText(car).TrimEnd('\n')); GUI.Label(new Rect(8, UiSurface.Height - 56f, 1200, 16), "W/S throttle/brake | A/D steer | E/Q shift | LShift clutch | Space handbrake | I ign | hold T starter | R reset | Backspace reset to start | C cam | H hide info | F1/Esc settings", mono); }
    }
    public const float MphPerMs = 2.236936f;   // display only: 1 m/s = 2.236936 mph (exact by definition of the mile); physics stays SI
    // @description - Converts a temperature from degrees Celsius to degrees Fahrenheit for the HUD tire table.
    // @param {double} celsius - temperature in degrees Celsius
    // @return {double} - temperature in degrees Fahrenheit
    static double ToFahrenheit(double celsius) => TireThermal.CelsiusToFahrenheit(celsius);
    // @description - Draws a text label on a grey box sized to fit the text.
    // @param {float} left - left edge in logical UI pixels
    // @param {float} top - top edge in logical UI pixels
    // @param {string} text - text to show
    // @return {Void} - nothing
    void Box(float left, float top, string text) { var textSize = mono.CalcSize(new GUIContent(text)); GUI.Box(new Rect(left, top, textSize.x + 10f, textSize.y + 6f), GUIContent.none); GUI.Label(new Rect(left + 5f, top + 3f, textSize.x + 2f, textSize.y), text, mono); }
    // @description - Tells whether the grey info boxes are shown (the H key setting); true when no input profile exists.
    // @param {CarController} car - car whose input router holds the profile
    // @return {bool} - true if the info boxes should be drawn
    static bool HudVisible(CarController car) => !(car.router && car.router.map != null && car.router.map.profile != null) || car.router.map.profile.hudVisible;

    // @description - Draws the steering wheel angle and the left and right front road wheel steer angles.
    // @param {CarController} car - car to read the steering angles from
    // @return {Void} - nothing
    static void DrawWheel(CarController car)
    {
        float centerX = UiSurface.Width - 110f;
        GUI.Label(new Rect(centerX - 100f, 20f, 200f, 40f), $"wheel {car.handwheelDeg,6:0}°\nL/R {car.frontLeftWheel.steerDeg,5:0.0}/{car.frontRightWheel.steerDeg,5:0.0}°");
    }
    // Joint reaction forces on the car body (tire-to-body force path), from the linear maps in compliance.json (linearised at design
    // ride height, bush rates ESTIMATED). Shown as |F| in N with the vertical component; front corners use previous-step tire forces.
    static readonly string[] FrontJointNames = { "armF", "armR", "tieRod", "topMnt", "ballJ" }, RearJointNames = { "pivL", "pivR", "dampL", "dampR" };
    // @description - Computes the reaction force at each of the five front suspension joints (lower arm front and rear, tie rod, top mount, ball joint) on the car body from the tire forces, using the linearised joint map from the suspension tables (E: bush rates estimated). The map has 4 load cases (brake longitudinal, drive longitudinal, lateral, vertical) times 5 joints times 3 axes. The lateral sign mirrors the right corner onto the left-corner frame.
    // @param {SuspensionTables} suspensionTables - suspension tables holding the front joint reaction map
    // @param {WheelCorner} wheel - front wheel corner whose tire forces are used (newtons)
    // @return {Vector3[]} - five joint force vectors in newtons (all zero if the map is missing)
    public static Vector3[] FrontJoints(SuspensionTables suspensionTables, WheelCorner wheel)
    {
        var reactions = new Vector3[5]; if (suspensionTables == null || suspensionTables.frontJointReactions == null || suspensionTables.frontJointReactions.Length < 60) return reactions;
        float lateralSign = wheel.left ? 1f : -1f, lateralForce = wheel.left ? -wheel.force.y : wheel.force.y;   // outward lateral, left-corner frame
        bool isBraking = wheel.brakeTorque > 0f && wheel.force.x < 0f;
        float[] loads = { isBraking ? wheel.force.x : 0f, isBraking ? 0f : wheel.force.x, lateralForce, wheel.verticalLoad };
        for (int loadIndex = 0; loadIndex < 4; loadIndex++) for (int jointIndex = 0; jointIndex < 5; jointIndex++)
        { int mapOffset = (loadIndex * 5 + jointIndex) * 3; reactions[jointIndex] += loads[loadIndex] * new Vector3(suspensionTables.frontJointReactions[mapOffset], lateralSign * suspensionTables.frontJointReactions[mapOffset + 1], suspensionTables.frontJointReactions[mapOffset + 2]); }
        return reactions;
    }
    // @description - Computes the reaction force at each of the four rear beam joints (left and right pivot, left and right damper) on the car body from both rear tire forces, using the linearised joint map (3 load cases: longitudinal, lateral, vertical). The right wheel is mirrored into the left frame and its result is swapped to the right-hand joints.
    // @param {SuspensionTables} suspensionTables - suspension tables holding the rear beam joint reaction map
    // @param {WheelCorner} leftWheel - rear left wheel corner
    // @param {WheelCorner} rightWheel - rear right wheel corner
    // @return {Vector3[]} - four joint force vectors in newtons (all zero if the map is missing)
    public static Vector3[] RearJoints(SuspensionTables suspensionTables, WheelCorner leftWheel, WheelCorner rightWheel)
    {
        var reactions = new Vector3[4]; if (suspensionTables == null || suspensionTables.rearBeamJointReactions == null || suspensionTables.rearBeamJointReactions.Length < 36) return reactions;
        int[] swap = { 1, 0, 3, 2 };
        foreach (var wheel in new[] { leftWheel, rightWheel })
        {
            float lateralForce = wheel.left ? -wheel.force.y : wheel.force.y; float[] loads = { wheel.force.x, lateralForce, wheel.verticalLoad };
            for (int loadIndex = 0; loadIndex < 3; loadIndex++) for (int jointIndex = 0; jointIndex < 4; jointIndex++)
            {
                int mapOffset = (loadIndex * 4 + jointIndex) * 3; var reaction = new Vector3(suspensionTables.rearBeamJointReactions[mapOffset], suspensionTables.rearBeamJointReactions[mapOffset + 1], suspensionTables.rearBeamJointReactions[mapOffset + 2]) * loads[loadIndex];
                if (wheel.left) reactions[jointIndex] += reaction; else { reaction.y = -reaction.y; reactions[swap[jointIndex]] += reaction; }
            }
        }
        return reactions;
    }
    // @description - Builds the text block of joint reaction forces (magnitude and vertical component in newtons) for the front and rear suspension, plus anti-dive/anti-squat coefficients and the active suspension model names.
    // @param {CarController} car - car whose wheels and parameters are described
    // @return {string} - multi-line text for the HUD
    static string JointText(CarController car)
    {
        var suspensionTables = car.carParams.suspensionTables; if (!suspensionTables) return "";
        string text = "Joint reactions on body |F| (Fz) N  [linear maps, bushes E]\n";
        foreach (var wheel in new[] { car.frontLeftWheel, car.frontRightWheel })
        {
            var joints = FrontJoints(suspensionTables, wheel); text += wheel.name + ": ";
            for (int i = 0; i < 5; i++) text += $"{FrontJointNames[i]} {joints[i].magnitude,5:0} ({joints[i].z,5:0})  ";
            text += "\n";
        }
        var rearJoints = RearJoints(suspensionTables, car.rearLeftWheel, car.rearRightWheel); text += "Rear beam: ";
        for (int i = 0; i < 4; i++) text += $"{RearJointNames[i]} {rearJoints[i].magnitude,5:0} ({rearJoints[i].z,5:0})  ";
        return text + "\n(antiCoef F " + car.frontLeftWheel.antiCoef.ToString("0.000") + " R " + car.rearLeftWheel.antiCoef.ToString("0.000") + $")  unsprung {(car.carParams.useUnsprungMass ? "on" : "off")}  front {car.carParams.frontModel}  rear {car.carParams.rearModel}";
    }
}
