// Twitching steering/paddles on DirectInput FFB wheels: a second HID input report (USB PID state report) overwrote the joystick state.
using System.Runtime.InteropServices; using NUnit.Framework; using UnityEngine.InputSystem; using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.HID; using UnityEngine.InputSystem.Layouts; using UnityEngine.InputSystem.LowLevel; using UnityEngine.InputSystem.Utilities;
public class HidReportFilterTests : InputTestFixture
{
    [StructLayout(LayoutKind.Explicit, Size = 6)] struct Rep : IInputStateTypeInfo
    { [FieldOffset(0)] public byte id; [FieldOffset(1)] public ushort axisX; [FieldOffset(3)] public ushort axisY; [FieldOffset(5)] public byte buttons; public FourCC format => new FourCC('H', 'I', 'D'); }
    // @description - Builds the JSON HID descriptor of a Simucube-like wheel: a joystick report (id 1) with 16-bit X and Y axes and one button, plus optionally a second USB PID state report (id 2).
    // @param {bool} twoReports - true to add the second (PID state) input report, false for the joystick report only
    // @return {string} - the device capabilities as JSON for InputDeviceDescription
    static string BuildDescriptorJson(bool twoReports)
    {
        var builder = new HID.HIDDeviceDescriptorBuilder(HID.GenericDesktop.Joystick).StartReport(HID.HIDReportType.Input, 1)
            .AddElement(HID.GenericDesktop.X, 16).WithLogicalMinMax(0, 65535).AddElement(HID.GenericDesktop.Y, 16).WithLogicalMinMax(0, 65535).AddElement(HID.UsagePage.Button, 1, 1).WithLogicalMinMax(0, 1);
        if (twoReports) builder = builder.StartReport(HID.HIDReportType.Input, 2).AddElement((HID.UsagePage)0x0F, 0x92, 8).WithLogicalMinMax(0, 255).AddElement((HID.UsagePage)0x0F, 0x22, 32).WithLogicalMinMax(0, 255);   // PID state report
        var descriptor = builder.Finish(); descriptor.vendorId = 0x16D0; descriptor.productId = 0x0D61; return descriptor.ToJson();
    }
    // @description - Adds a simulated Simucube 2 Pro HID device to the test input system.
    // @param {bool} two - true if the device reports both the joystick and the PID state report
    // @return {InputDevice} - the added device
    InputDevice AddWheelDevice(bool two) => InputSystem.AddDevice(new InputDeviceDescription { interfaceName = "HID", manufacturer = "Granite Devices", product = "Simucube 2 Pro", capabilities = BuildDescriptorJson(two) });
    // @description - Starts each test with the HID report filter uninstalled, after the base fixture has reset the input system.
    // @return {Void} - nothing
    public override void Setup() { base.Setup(); HidReportFilter.Uninstall(); }
    // @description - Uninstalls the HID report filter after each test, then lets the base fixture restore the input system.
    // @return {Void} - nothing
    public override void TearDown() { HidReportFilter.Uninstall(); base.TearDown(); }
    // @description - Gets the steering (X) axis control of a joystick device.
    // @param {InputDevice} device - joystick device to read
    // @return {AxisControl} - the X axis control
    static AxisControl SteeringAxis(InputDevice device) => ((Joystick)device).stick.x;
    // @description - Finds the first real button control of a device (skipping stick, d-pad and synthetic buttons).
    // @param {InputDevice} device - device whose controls are searched
    // @return {ButtonControl} - the first button, or null if none
    static ButtonControl FirstPaddleButton(InputDevice device) { foreach (var control in device.allControls) if (control is ButtonControl button && !(control.parent is StickControl) && !control.synthetic && !(control.parent is DpadControl)) return button; return null; }
    // @description - Queues one state event, either the joystick report (full right, centred Y, button pressed) or the PID report (all zero), and processes it.
    // @param {InputDevice} device - device that receives the report
    // @param {bool} pid - true to send the PID state report (id 2), false to send the joystick report (id 1)
    // @return {Void} - nothing
    void FeedReport(InputDevice device, bool pid) { InputSystem.QueueStateEvent(device, pid ? new Rep { id = 2, axisX = 0, buttons = 0 } : new Rep { id = 1, axisX = 65535, axisY = 32768, buttons = 1 }); InputSystem.Update(); }

    // @description - Verifies that HID descriptor analysis picks report id 1 as the joystick report when the device has two input reports (ids 1 and 2), and leaves single-report devices alone (expected id -1).
    // @return {Void} - nothing
    [Test] public void Analyse_Finds_Joystick_Report_When_Multiple_Input_Reports()
    {
        var analysis = HidReportFilter.Analyse(BuildDescriptorJson(true)); Assert.AreEqual(1, analysis.expectedId); CollectionAssert.AreEqual(new[] { 1, 2 }, analysis.inputIds);
        Assert.AreEqual(-1, HidReportFilter.Analyse(BuildDescriptorJson(false)).expectedId, "single-report devices are left alone");
    }
    // @description - Reproduces the twitch bug without the filter: after a joystick report (steering above 0.9, paddle pressed) a PID report lands on the same state, driving steering below 0 and releasing the paddle.
    // @return {Void} - nothing
    [Test] public void Without_Filter_PID_Report_Corrupts_Steering_And_Buttons()   // reproduces the user's bug (proves the mechanism)
    {
        var wheel = AddWheelDevice(true); FeedReport(wheel, false); float steeringValue = SteeringAxis(wheel).ReadUnprocessedValue(); bool paddleWasPressed = FirstPaddleButton(wheel).isPressed; FeedReport(wheel, true);
        Assert.Greater(steeringValue, 0.9f); Assert.IsTrue(paddleWasPressed); Assert.Less(SteeringAxis(wheel).ReadUnprocessedValue(), 0f, "foreign report landed on the steering axis"); Assert.IsFalse(FirstPaddleButton(wheel).isPressed, "foreign report released the paddle");
    }
    // @description - Verifies that with the filter installed 200 interleaved joystick and PID reports leave the steering axis and paddle steady (no changes), that the 100 PID reports are dropped and the 100 joystick reports passed, and that the device report names the wheel.
    // @return {Void} - nothing
    [Test] public void Filter_Drops_Foreign_Reports_No_Twitch()
    {
        HidReportFilter.Install(); var wheel = AddWheelDevice(true);
        int changes = 0; float lastSteering = float.NaN; bool lastPaddle = false;
        for (int k = 0; k < 200; k++)
        {
            FeedReport(wheel, k % 2 == 1);   // joystick and PID reports interleaved, like an FFB wheel with an effect running
            float steeringValue = SteeringAxis(wheel).ReadUnprocessedValue(); bool paddlePressed = FirstPaddleButton(wheel).isPressed;
            if (k > 0 && (steeringValue != lastSteering || paddlePressed != lastPaddle)) changes++; lastSteering = steeringValue; lastPaddle = paddlePressed;
        }
        Assert.AreEqual(0, changes, "steering/paddle must be steady"); Assert.Greater(lastSteering, 0.9f); Assert.IsTrue(lastPaddle);
        var info = HidReportFilter.Get(wheel); Assert.AreEqual(100, info.dropped); Assert.AreEqual(100, info.passed);
        StringAssert.Contains("Simucube 2 Pro", HidReportFilter.DeviceReport());
    }
    // @description - Verifies that with the filter installed a device with a single input report passes its state through (steering above 0.9) and nothing is dropped.
    // @return {Void} - nothing
    [Test] public void Filter_Leaves_Single_Report_Devices_Untouched()
    {
        HidReportFilter.Install(); var wheel = AddWheelDevice(false); FeedReport(wheel, false); Assert.Greater(SteeringAxis(wheel).ReadUnprocessedValue(), 0.9f); Assert.AreEqual(0, HidReportFilter.Get(wheel).dropped);
    }
}
