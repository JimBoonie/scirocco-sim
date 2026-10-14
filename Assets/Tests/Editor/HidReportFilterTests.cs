// Twitching steering/paddles on DirectInput FFB wheels: a second HID input report (USB PID state report) overwrote the joystick state.
using System.Runtime.InteropServices; using NUnit.Framework; using UnityEngine.InputSystem; using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.HID; using UnityEngine.InputSystem.Layouts; using UnityEngine.InputSystem.LowLevel; using UnityEngine.InputSystem.Utilities;
public class HidReportFilterTests : InputTestFixture
{
    [StructLayout(LayoutKind.Explicit, Size = 6)] struct Rep : IInputStateTypeInfo
    { [FieldOffset(0)] public byte id; [FieldOffset(1)] public ushort x; [FieldOffset(3)] public ushort y; [FieldOffset(5)] public byte buttons; public FourCC format => new FourCC('H', 'I', 'D'); }
    static string Json(bool twoReports)
    {
        var b = new HID.HIDDeviceDescriptorBuilder(HID.GenericDesktop.Joystick).StartReport(HID.HIDReportType.Input, 1)
            .AddElement(HID.GenericDesktop.X, 16).WithLogicalMinMax(0, 65535).AddElement(HID.GenericDesktop.Y, 16).WithLogicalMinMax(0, 65535).AddElement(HID.UsagePage.Button, 1, 1).WithLogicalMinMax(0, 1);
        if (twoReports) b = b.StartReport(HID.HIDReportType.Input, 2).AddElement((HID.UsagePage)0x0F, 0x92, 8).WithLogicalMinMax(0, 255).AddElement((HID.UsagePage)0x0F, 0x22, 32).WithLogicalMinMax(0, 255);   // PID state report
        var d = b.Finish(); d.vendorId = 0x16D0; d.productId = 0x0D61; return d.ToJson();
    }
    InputDevice Wheel(bool two) => InputSystem.AddDevice(new InputDeviceDescription { interfaceName = "HID", manufacturer = "Granite Devices", product = "Simucube 2 Pro", capabilities = Json(two) });
    public override void Setup() { base.Setup(); HidReportFilter.Uninstall(); }
    public override void TearDown() { HidReportFilter.Uninstall(); base.TearDown(); }
    static AxisControl X(InputDevice d) => ((Joystick)d).stick.x;
    static ButtonControl Btn(InputDevice d) { foreach (var c in d.allControls) if (c is ButtonControl b && !(c.parent is StickControl) && !c.synthetic && !(c.parent is DpadControl)) return b; return null; }
    void Feed(InputDevice d, bool pid) { InputSystem.QueueStateEvent(d, pid ? new Rep { id = 2, x = 0, buttons = 0 } : new Rep { id = 1, x = 65535, y = 32768, buttons = 1 }); InputSystem.Update(); }

    [Test] public void Analyse_Finds_Joystick_Report_When_Multiple_Input_Reports()
    {
        var i = HidReportFilter.Analyse(Json(true)); Assert.AreEqual(1, i.expectedId); CollectionAssert.AreEqual(new[] { 1, 2 }, i.inputIds);
        Assert.AreEqual(-1, HidReportFilter.Analyse(Json(false)).expectedId, "single-report devices are left alone");
    }
    [Test] public void Without_Filter_PID_Report_Corrupts_Steering_And_Buttons()   // reproduces the user's bug (proves the mechanism)
    {
        var d = Wheel(true); Feed(d, false); float a = X(d).ReadUnprocessedValue(); bool p1 = Btn(d).isPressed; Feed(d, true);
        Assert.Greater(a, 0.9f); Assert.IsTrue(p1); Assert.Less(X(d).ReadUnprocessedValue(), 0f, "foreign report landed on the steering axis"); Assert.IsFalse(Btn(d).isPressed, "foreign report released the paddle");
    }
    [Test] public void Filter_Drops_Foreign_Reports_No_Twitch()
    {
        HidReportFilter.Install(); var d = Wheel(true);
        int changes = 0; float last = float.NaN; bool lastB = false;
        for (int k = 0; k < 200; k++)
        {
            Feed(d, k % 2 == 1);   // joystick and PID reports interleaved, like an FFB wheel with an effect running
            float v = X(d).ReadUnprocessedValue(); bool b = Btn(d).isPressed;
            if (k > 0 && (v != last || b != lastB)) changes++; last = v; lastB = b;
        }
        Assert.AreEqual(0, changes, "steering/paddle must be steady"); Assert.Greater(last, 0.9f); Assert.IsTrue(lastB);
        var info = HidReportFilter.Get(d); Assert.AreEqual(100, info.dropped); Assert.AreEqual(100, info.passed);
        StringAssert.Contains("Simucube 2 Pro", HidReportFilter.DeviceReport());
    }
    [Test] public void Filter_Leaves_Single_Report_Devices_Untouched()
    {
        HidReportFilter.Install(); var d = Wheel(false); Feed(d, false); Assert.Greater(X(d).ReadUnprocessedValue(), 0.9f); Assert.AreEqual(0, HidReportFilter.Get(d).dropped);
    }
}
