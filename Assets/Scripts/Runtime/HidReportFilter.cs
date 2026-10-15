// ROOT-CAUSE FIX for "steering + paddles twitch from startup" on DirectInput force-feedback wheels (e.g. Simucube 2) on Windows.
// The Input System's auto-generated HID layout (no device-specific layout) ignores HID report IDs: every INPUT report the device sends
// is copied onto ONE state block laid out for the joystick report. FFB wheels also send other input reports, notably the USB PID
// "PID State Report" (effect playing / device state), which starts as soon as our FFB plugin creates and starts its constant-force effect
// at startup. Those bytes then land on the steering axis and button bits -> values alternate between the real joystick report and
// garbage on every other event: steering and buttons twitch, holding a paddle reads 1/0/1/0. No filtering of values can fix that.
// Fix: for generic HID devices whose descriptor declares more than one input report ID, drop every state event whose report ID
// (byte 0 of the HID state) is not the ID of the report that carries the joystick X axis. Values are not altered, only foreign reports dropped.
using System; using System.Collections.Generic; using System.Text; using UnityEngine;
using UnityEngine.InputSystem; using UnityEngine.InputSystem.HID; using UnityEngine.InputSystem.LowLevel; using UnityEngine.InputSystem.Utilities;

public static class HidReportFilter
{
    public class Info { public int expectedId = -1; public int[] inputIds = Array.Empty<int>(); public long passed, dropped; public string note = ""; }
    static readonly Dictionary<int, Info> infos = new Dictionary<int, Info>();
    static bool installed; static readonly FourCC HidFormat = new FourCC('H', 'I', 'D');

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    // @description - Install.
    // @return {Void} - nothing
    public static void Install()
    {
        if (installed) return; installed = true;
        InputSystem.onEvent += OnEvent;
        InputSystem.onDeviceChange += (d, c) => { if (c == InputDeviceChange.Added || c == InputDeviceChange.Removed || c == InputDeviceChange.Reconnected) { infos.Remove(d.deviceId); LogDevices($"device {c}: {d.displayName}"); } };
        LogDevices("startup");
    }
    // @description - Uninstall.
    // @return {Void} - nothing
    public static void Uninstall() { if (!installed) return; installed = false; InputSystem.onEvent -= OnEvent; infos.Clear(); }

    // @description - Analyse a HID descriptor: returns the report ID to keep (joystick X axis report), or -1 if the device has only one input report.
    // @param {string} capabilitiesJson - capabilities json
    // @return {Info} - the analyse
    public static Info Analyse(string capabilitiesJson)
    {
        var info = new Info();
        if (string.IsNullOrEmpty(capabilitiesJson)) { info.note = "no HID descriptor"; return info; }
        HID.HIDDeviceDescriptor desc; try { desc = HID.HIDDeviceDescriptor.FromJson(capabilitiesJson); } catch (Exception e) { info.note = "descriptor parse failed: " + e.Message; return info; }
        if (desc.elements == null) { info.note = "no elements"; return info; }
        var ids = new SortedSet<int>(); int xId = -1, firstId = -1;
        foreach (var el in desc.elements)
        {
            if (el.reportType != HID.HIDReportType.Input) continue;
            ids.Add(el.reportId); if (firstId < 0) firstId = el.reportId;
            if (xId < 0 && el.usagePage == HID.UsagePage.GenericDesktop && el.usage == (int)HID.GenericDesktop.X) xId = el.reportId;
        }
        info.inputIds = new int[ids.Count]; ids.CopyTo(info.inputIds);
        if (ids.Count > 1) { info.expectedId = xId >= 0 ? xId : firstId; info.note = $"{ids.Count} input reports -> keeping report {info.expectedId} only"; }
        else info.note = "single input report";
        return info;
    }
    // @description - Get (see the body for details).
    // @param {InputDevice} d - value of type InputDevice
    // @return {Info} - the get
    public static Info Get(InputDevice d)
    {
        if (infos.TryGetValue(d.deviceId, out var i)) return i;
        i = d.description.interfaceName == "HID" && !IsSpecificLayout(d) ? Analyse(d.description.capabilities) : new Info { note = d.description.interfaceName == "HID" ? "device-specific layout (not filtered)" : "not HID" };
        infos[d.deviceId] = i; return i;
    }
    // @description - Layouts written for a specific device (DualShock, Xbox HID, ...) handle report IDs themselves; only the auto-generated ones need this.
    // @param {InputDevice} device - device
    // @return {bool} - the is specific layout
    static bool IsSpecificLayout(InputDevice device) => !(device.layout.StartsWith("HID::") || device.layout.Contains("::"));

    // @description - Handles the event.
    // @param {InputEventPtr} inputEvent - input event
    // @param {InputDevice} device - device
    // @return {Void} - nothing
    static unsafe void OnEvent(InputEventPtr inputEvent, InputDevice device)
    {
        if (device == null || !inputEvent.IsA<StateEvent>()) return;
        var se = StateEvent.From(inputEvent); if (se->stateFormat != HidFormat || se->stateSizeInBytes < 1) return;
        var info = Get(device); if (info.expectedId < 0) return;
        byte id = *(byte*)se->state;
        if (id == info.expectedId) info.passed++; else { info.dropped++; inputEvent.handled = true; }   // foreign report (e.g. PID state): never touches the state
    }

    // @description - Device report.
    // @return {string} - the device report
    public static string DeviceReport()
    {
        var sb = new StringBuilder(); var products = new Dictionary<string, int>();
        foreach (var d in InputSystem.devices) { var p = d.description.product ?? ""; products[p] = products.TryGetValue(p, out var n) ? n + 1 : 1; }
        foreach (var d in InputSystem.devices)
        {
            if (d is Mouse || d is Pointer || d is Sensor) continue;
            var i = Get(d); var desc = d.description;
            sb.Append($"#{d.deviceId} '{d.displayName}' layout={d.layout} iface={desc.interfaceName} product='{desc.product}' mfr='{desc.manufacturer}'");
            if (i.inputIds.Length > 0) sb.Append($" inputReports=[{string.Join(",", i.inputIds)}]");
            sb.Append($" {i.note}"); if (i.expectedId >= 0) sb.Append($" passed={i.passed} dropped={i.dropped}");
            if (!string.IsNullOrEmpty(desc.product) && products[desc.product] > 1) sb.Append("  [DUPLICATE product name: same physical device may appear more than once]");
            sb.Append('\n');
        }
        return sb.ToString();
    }
    // @description - Log devices.
    // @param {string} why - why
    // @return {Void} - nothing
    public static void LogDevices(string why) { Debug.Log($"[Input] devices ({why}):\n{DeviceReport()}"); }
}
