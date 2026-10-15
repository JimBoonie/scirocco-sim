// Speed-dependent drag of the box on both ports: bulk spin loss (churning, windage, bearings, anchored to a measured unloaded box), drag of the loose gears 3-5 against
// the input shaft, rattle of the loose gears 1 and 2 (lash, impacts, drag), and the output-side bearing preload drag. All loads oppose motion and never create energy.
using UnityEngine;

public struct DragLoads
{
    public double inputTorque, carrierTorque, heatPowerWatts;   // N m on the input node, N m on the carrier, W into the oil
}

public class GearboxDragLoads
{
    readonly TransmissionParameters parameters; readonly GearSetGeometry geometry; readonly GearboxOilModel oil;
    readonly LooseGearRattle[] rattleGears = new LooseGearRattle[2];   // loose 1st and 2nd driven gears on the output shaft
    const int FirstRattleGear = 1, RattleGearCount = 2;
    public double NeutralDragInputReferredNewtonMeters { get; private set; }   // last computed sum of the loose-gear drags referred to the input shaft (for tests)

    public LooseGearRattle RattleGear(int gear) => rattleGears[gear - FirstRattleGear];

    // @description - builds the loads with a rattle element for each of the first two gears
    // @param {TransmissionParameters} parameters - drag and lash parameters
    // @param {GearSetGeometry} geometry - gear set
    // @param {GearboxOilModel} oil - oil model
    // @return {void} - constructor
    public GearboxDragLoads(TransmissionParameters parameters, GearSetGeometry geometry, GearboxOilModel oil)
    {
        this.parameters = parameters; this.geometry = geometry; this.oil = oil;
        for (int index = 0; index < RattleGearCount; index++)
        {
            int gear = FirstRattleGear + index;
            double halfLashAtGear = 0.5 * parameters.normalBacklashMillimeters * 1e-3 / System.Math.Cos(parameters.normalPressureAngleDegrees * Mathf.Deg2Rad) / geometry.PitchRadius(parameters.drivenToothCount[gear - 1]);
            rattleGears[index] = new LooseGearRattle(geometry.LooseGearInertia(gear), geometry.GearboxRatio(gear), halfLashAtGear, parameters.impactRestitution);
        }
    }

    // @description - relative speed of a loose gear against its shaft and the factor that refers its drag torque to the input shaft
    // @param {int} gear - 1..5
    // @param {double} inputSpeed - rad/s
    // @param {double} pinionShaftSpeed - rad/s
    // @param {double} inputReferenceFactor - (out) torque at the gear times this = torque at the input shaft
    // @return {double} - relative speed in rad/s (signed, gear minus shaft)
    double RelativeSpeed(int gear, double inputSpeed, double pinionShaftSpeed, out double inputReferenceFactor)
    {
        double ratio = geometry.GearboxRatio(gear);
        if (geometry.LooseOnInputShaft(gear)) { inputReferenceFactor = 1.0; return ratio * pinionShaftSpeed - inputSpeed; }   // gear speed i w_p against the input shaft w_in
        inputReferenceFactor = 1.0 / ratio; return inputSpeed / ratio - pinionShaftSpeed;                                     // gear speed w_in / i against the output shaft w_p
    }

    // @description - sum of the drag torques of all five loose gears referred to the input shaft (the part of the anchored spin loss that the loose gears own)
    // @param {double} inputSpeed - rad/s
    // @param {double} pinionShaftSpeed - rad/s
    // @return {double} - N m magnitude at the input shaft
    public double LooseGearDragSumAtInput(double inputSpeed, double pinionShaftSpeed)
    {
        double sum = 0.0;
        for (int gear = 1; gear <= GearSetGeometry.ForwardGearCount; gear++) { double relative = RelativeSpeed(gear, inputSpeed, pinionShaftSpeed, out double factor); sum += oil.LooseGearDragMagnitude(relative) * factor; }
        return sum;
    }

    // @description - bulk spin torque at the input shaft: the anchored unloaded total minus what the five loose gears already account for
    // @param {double} inputSpeed - rad/s
    // @param {double} pinionShaftSpeed - rad/s
    // @return {double} - N m magnitude
    public double BulkSpinTorque(double inputSpeed, double pinionShaftSpeed) => System.Math.Max(0.0, oil.SpinTorqueMagnitude(inputSpeed) - LooseGearDragSumAtInput(inputSpeed, pinionShaftSpeed));

    // @description - computes all drag loads for one step
    // @param {double} inputSpeed - input node speed in rad/s
    // @param {double} carrierSpeed - carrier speed in rad/s
    // @param {int} lockedGear - gear whose dogs or stuck cone tie input and output (0 = none); its loose gear is rigid with the shaft and does not rattle or drag
    // @param {double} inputInertia - input node inertia in kg m^2
    // @param {double} dt - step in s
    // @return {DragLoads} - torques on both ports and the heat into the oil
    public DragLoads Compute(double inputSpeed, double carrierSpeed, int lockedGear, double inputInertia, double dt)
    {
        double pinionSpeed = carrierSpeed * geometry.FinalDriveRatio, finalDrive = geometry.FinalDriveRatio;
        var loads = new DragLoads(); double bulk = BulkSpinTorque(inputSpeed, pinionSpeed);
        loads.inputTorque -= System.Math.Sign(inputSpeed) * bulk; loads.heatPowerWatts += bulk * System.Math.Abs(inputSpeed);
        double drySum = 0.0;
        for (int gear = 1; gear <= GearSetGeometry.ForwardGearCount; gear++)
        {
            double relative = RelativeSpeed(gear, inputSpeed, pinionSpeed, out double factor), drag = oil.LooseGearDragMagnitude(relative);
            drySum += drag * factor;
            if (gear == lockedGear) { if (gear <= RattleGearCount) RattleGear(gear).Reset(); continue; }
            if (geometry.LooseOnInputShaft(gear)) AddInputShaftGearDrag(ref loads, gear, relative, drag, finalDrive);
            else AddOutputShaftGearRattle(ref loads, gear, inputSpeed, pinionSpeed, drag, inputInertia, finalDrive, dt);
        }
        NeutralDragInputReferredNewtonMeters = drySum;
        double bearing = oil.OutputBearingDragMagnitude(finalDrive), clamp = parameters.carrierSideDragInertiaKilogramMetersSquared * System.Math.Abs(carrierSpeed) / dt;
        loads.carrierTorque -= System.Math.Sign(carrierSpeed) * System.Math.Min(bearing, clamp); loads.heatPowerWatts += System.Math.Min(bearing, clamp) * System.Math.Abs(carrierSpeed);
        return loads;
    }

    // @description - drag between a loose gear on the input shaft (3rd..5th) and the input shaft: opposes the relative motion on the shaft, and drags the gear and so the output
    // @param {DragLoads} loads - accumulated loads (modified)
    // @param {int} gear - 3..5
    // @param {double} gearMinusShaftSpeed - rad/s, gear speed minus input shaft speed
    // @param {double} drag - drag magnitude in N m at the gear
    // @param {double} finalDrive - final drive ratio
    // @return {Void}
    void AddInputShaftGearDrag(ref DragLoads loads, int gear, double gearMinusShaftSpeed, double drag, double finalDrive)
    {
        double direction = System.Math.Sign(gearMinusShaftSpeed);
        loads.inputTorque += direction * drag; loads.carrierTorque -= direction * drag * geometry.GearboxRatio(gear) * finalDrive;
        loads.heatPowerWatts += drag * System.Math.Abs(gearMinusShaftSpeed);
    }

    // @description - rattle element of a loose gear on the output shaft (1st, 2nd): impacts and contact torque on the input, and the drag the gear exerts on its shaft
    // @param {DragLoads} loads - accumulated loads (modified)
    // @param {int} gear - 1 or 2
    // @param {double} inputSpeed - rad/s
    // @param {double} pinionSpeed - rad/s
    // @param {double} drag - drag magnitude in N m at the gear
    // @param {double} inputInertia - kg m^2
    // @param {double} finalDrive - final drive ratio
    // @param {double} dt - s
    // @return {Void}
    void AddOutputShaftGearRattle(ref DragLoads loads, int gear, double inputSpeed, double pinionSpeed, double drag, double inputInertia, double finalDrive, double dt)
    {
        var rattle = RattleGear(gear); double gearSpeedBefore = inputSpeed / geometry.GearboxRatio(gear);
        loads.inputTorque += rattle.Advance(inputSpeed, pinionSpeed, drag, inputInertia, dt);
        loads.carrierTorque += System.Math.Sign(gearSpeedBefore - pinionSpeed) * drag * finalDrive; loads.heatPowerWatts += drag * System.Math.Abs(gearSpeedBefore - pinionSpeed);
    }
}
