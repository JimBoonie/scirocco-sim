// Loose gear that is driven through a lash by the input shaft (the 1st and 2nd driven gears on the stationary output shaft in neutral): one DOF with lash, an impact law
// (restitution) and drag. Rattle onset condition (derived): the gear separates from the drive flank when J_g |alpha| / i > T_drag. Lash is owned here and nowhere else.
using UnityEngine;

public class LooseGearRattle
{
    readonly double gearInertia, gearRatio, halfLash, restitution;
    double gearSpeed, relativeAngle, previousInputSpeed;
    bool initialized;
    public int ImpactCount { get; private set; }
    public double LastImpactImpulseNewtonMeterSeconds { get; private set; }
    public bool InContact { get; private set; }

    // @description - builds one loose gear
    // @param {double} gearInertia - kg m^2 at the gear
    // @param {double} gearRatio - input turns per gear turn (the gearbox ratio of the pair)
    // @param {double} halfLashAtGear - half of the mesh lash in rad at the gear
    // @param {double} restitution - coefficient of restitution of the tooth impact
    public LooseGearRattle(double gearInertia, double gearRatio, double halfLashAtGear, double restitution)
    { this.gearInertia = gearInertia; this.gearRatio = gearRatio; halfLash = halfLashAtGear; this.restitution = restitution; }

    // @description - forgets the gear state (the gear became rigid with its shaft; the next Advance restarts it from the measured speeds)
    // @return {Void}
    public void Reset() { initialized = false; InContact = false; relativeAngle = 0.0; }

    // @description - torque from the gear's inertia the pinion has to supply to make it follow, and the onset test of the rattle condition
    // @param {double} inputAngularAcceleration - rad/s^2 at the input shaft
    // @param {double} dragTorque - N m drag at the gear
    // @return {bool} - true when J_g |alpha| / i exceeds the drag, i.e. the gear leaves the flank
    public bool RattleOnset(double inputAngularAcceleration, double dragTorque) => gearInertia * System.Math.Abs(inputAngularAcceleration) / gearRatio > dragTorque;

    // @description - advances the gear one step against the input shaft speed and returns the torque the contact applies back on the input shaft (average over the step)
    // @param {double} inputSpeed - input shaft speed in rad/s
    // @param {double} shaftSpeed - speed of the shaft the gear rides on (output shaft) in rad/s
    // @param {double} dragMagnitude - drag torque magnitude at the gear in N m (relative to its shaft)
    // @param {double} inputInertia - kg m^2 inertia of the input node (for the impact impulse)
    // @param {double} dt - step in s
    // @return {double} - torque on the input shaft in N m
    public double Advance(double inputSpeed, double shaftSpeed, double dragMagnitude, double inputInertia, double dt)
    {
        if (!initialized) { gearSpeed = inputSpeed / gearRatio; previousInputSpeed = inputSpeed; initialized = true; }
        double inputAcceleration = (inputSpeed - previousInputSpeed) / dt; previousInputSpeed = inputSpeed;
        double gearDrag = -System.Math.Sign(gearSpeed - shaftSpeed) * dragMagnitude, reaction;
        if (InContact) reaction = ContactStep(inputSpeed, inputAcceleration, gearDrag, dt);
        else reaction = FreeStep(inputSpeed, shaftSpeed, gearDrag, inputInertia, dt);
        return reaction;
    }

    // @description - contact step: the gear follows the input; it separates when the torque the flank would have to carry changes sign
    // @param {double} inputSpeed - rad/s
    // @param {double} inputAcceleration - rad/s^2
    // @param {double} gearDrag - signed drag torque at the gear in N m
    // @param {double} dt - s
    // @return {double} - torque on the input shaft in N m
    double ContactStep(double inputSpeed, double inputAcceleration, double gearDrag, double dt)
    {
        double requiredTorque = gearInertia * inputAcceleration / gearRatio - gearDrag, flankSign = System.Math.Sign(relativeAngle);
        if (requiredTorque * flankSign < 0.0) { InContact = false; gearSpeed = inputSpeed / gearRatio; return 0.0; }   // the flank cannot pull: the gear separates and coasts
        gearSpeed = inputSpeed / gearRatio;
        return -requiredTorque / gearRatio;
    }

    // @description - free-flight step: drag only, then the lash gap closes and the impact law acts
    // @param {double} inputSpeed - rad/s
    // @param {double} shaftSpeed - rad/s
    // @param {double} gearDrag - signed drag torque at the gear in N m
    // @param {double} inputInertia - kg m^2
    // @param {double} dt - s
    // @return {double} - average torque on the input shaft from an impact in this step in N m
    double FreeStep(double inputSpeed, double shaftSpeed, double gearDrag, double inputInertia, double dt)
    {
        double maximumDragChange = System.Math.Abs(gearSpeed - shaftSpeed);
        double dragSpeedChange = System.Math.Min(System.Math.Abs(gearDrag) * dt / gearInertia, maximumDragChange);
        gearSpeed += System.Math.Sign(gearDrag) * dragSpeedChange;
        relativeAngle += (inputSpeed / gearRatio - gearSpeed) * dt;
        if (System.Math.Abs(relativeAngle) < halfLash) return 0.0;
        relativeAngle = System.Math.Sign(relativeAngle) * halfLash;
        double closingSpeed = (inputSpeed / gearRatio - gearSpeed) * System.Math.Sign(relativeAngle);
        if (closingSpeed <= 0.0) return 0.0;
        double latchSpeed = System.Math.Abs(gearDrag) * dt / gearInertia;   // D: the largest speed change drag alone can produce in one step; slower closing sticks (Karnopp dead band)
        return Impact(closingSpeed * System.Math.Sign(relativeAngle), latchSpeed, inputInertia, dt);
    }

    // @description - impact of the flank: momentum exchange with restitution between the gear and the input node (reduced inertia), returned as a torque averaged over the step
    // @param {double} relativeSpeed - signed closing speed at the gear in rad/s
    // @param {double} latchSpeed - closing speed in rad/s below which the gear sticks to the flank
    // @param {double} inputInertia - kg m^2
    // @param {double} dt - s
    // @return {double} - torque on the input shaft in N m
    double Impact(double relativeSpeed, double latchSpeed, double inputInertia, double dt)
    {
        double inputReflected = inputInertia / (gearRatio * gearRatio), reduced = gearInertia * inputReflected / (gearInertia + inputReflected);
        double impulseAtGear = (1.0 + restitution) * reduced * relativeSpeed;
        gearSpeed += impulseAtGear / gearInertia; ImpactCount++; LastImpactImpulseNewtonMeterSeconds = System.Math.Abs(impulseAtGear);
        InContact = restitution <= 0.0 || System.Math.Abs(relativeSpeed) <= latchSpeed;
        return -impulseAtGear / gearRatio / dt;
    }
}
