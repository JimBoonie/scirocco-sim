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
    // @description - Hooks the Input System event stream (once, before the first scene loads) so foreign HID reports can be dropped, and logs the connected devices at startup and whenever a device is added, removed or reconnected.
    // @return {Void} - nothing
    public static void Install()
    {
        if (installed) return; installed = true;
        InputSystem.onEvent += OnEvent;
        InputSystem.onDeviceChange += (device, change) => { if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed || change == InputDeviceChange.Reconnected) { infos.Remove(device.deviceId); LogDevices($"device {change}: {device.displayName}"); } };
        LogDevices("startup");
    }
    // @description - Unhooks the event filter and forgets all cached device information (used by tests).
    // @return {Void} - nothing
    public static void Uninstall() { if (!installed) return; installed = false; InputSystem.onEvent -= OnEvent; infos.Clear(); }

    // @description - Reads a HID report descriptor (JSON from the Input System) and decides which input report to keep. Devices with several input report IDs (for example force-feedback wheels that also send PID state reports) must only keep the report that carries the joystick X axis.
    // @param {string} capabilitiesJson - HID descriptor as JSON (InputDeviceDescription.capabilities); may be null or empty
    // @return {Info} - filter information: expected report ID (-1 = do not filter), all input report IDs and a human-readable note
    public static Info Analyse(string capabilitiesJson)
    {
        var info = new Info();
        if (string.IsNullOrEmpty(capabilitiesJson)) { info.note = "no HID descriptor"; return info; }
        HID.HIDDeviceDescriptor descriptor; try { descriptor = HID.HIDDeviceDescriptor.FromJson(capabilitiesJson); } catch (Exception parseException) { info.note = "descriptor parse failed: " + parseException.Message; return info; }
        if (descriptor.elements == null) { info.note = "no elements"; return info; }
        var inputReportIds = new SortedSet<int>(); int xAxisReportId = -1, firstReportId = -1;
        foreach (var element in descriptor.elements)
        {
            if (element.reportType != HID.HIDReportType.Input) continue;
            inputReportIds.Add(element.reportId); if (firstReportId < 0) firstReportId = element.reportId;
            if (xAxisReportId < 0 && element.usagePage == HID.UsagePage.GenericDesktop && element.usage == (int)HID.GenericDesktop.X) xAxisReportId = element.reportId;
        }
        info.inputIds = new int[inputReportIds.Count]; inputReportIds.CopyTo(info.inputIds);
        if (inputReportIds.Count > 1) { info.expectedId = xAxisReportId >= 0 ? xAxisReportId : firstReportId; info.note = $"{inputReportIds.Count} input reports -> keeping report {info.expectedId} only"; }
        else info.note = "single input report";
        return info;
    }
    // @description - Returns the cached filter information for a device, analysing its HID descriptor on first use. Only generic HID layouts are analysed; devices with a specific layout are never filtered.
    // @param {InputDevice} device - the input device
    // @return {Info} - filter information for the device
    public static Info Get(InputDevice device)
    {
        if (infos.TryGetValue(device.deviceId, out var info)) return info;
        info = device.description.interfaceName == "HID" && !IsSpecificLayout(device) ? Analyse(device.description.capabilities) : new Info { note = device.description.interfaceName == "HID" ? "device-specific layout (not filtered)" : "not HID" };
        infos[device.deviceId] = info; return info;
    }
    // @description - Tells whether a device uses a layout written for that specific device (DualShock, Xbox HID, ...). Those layouts handle report IDs themselves; only auto-generated HID layouts need the filter.
    // @param {InputDevice} device - the input device
    // @return {bool} - true if the layout is device-specific
    static bool IsSpecificLayout(InputDevice device) => !(device.layout.StartsWith("HID::") || device.layout.Contains("::"));

    // @description - Input System event callback: drops (marks as handled) any state event whose HID report ID differs from the expected one, so foreign reports never reach the device state. Values are never altered.
    // @param {InputEventPtr} inputEvent - the incoming input event
    // @param {InputDevice} device - the device the event belongs to, or null
    // @return {Void} - nothing
    static unsafe void OnEvent(InputEventPtr inputEvent, InputDevice device)
    {
        if (device == null || !inputEvent.IsA<StateEvent>()) return;
        var stateEvent = StateEvent.From(inputEvent); if (stateEvent->stateFormat != HidFormat || stateEvent->stateSizeInBytes < 1) return;
        var info = Get(device); if (info.expectedId < 0) return;
        byte reportId = *(byte*)stateEvent->state;
        if (reportId == info.expectedId) info.passed++; else { info.dropped++; inputEvent.handled = true; }   // foreign report (e.g. PID state): never touches the state
    }

    // @description - Builds a multi-line text listing every connected non-pointer device: id, name, layout, interface, product, manufacturer, input report IDs, filter note, pass/drop counters and a warning for duplicated product names.
    // @return {string} - the report text (one device per line)
    public static string DeviceReport()
    {
        var report = new StringBuilder(); var products = new Dictionary<string, int>();
        foreach (var device in InputSystem.devices) { var productName = device.description.product ?? ""; products[productName] = products.TryGetValue(productName, out var previousCount) ? previousCount + 1 : 1; }
        foreach (var device in InputSystem.devices)
        {
            if (device is Mouse || device is Pointer || device is Sensor) continue;
            var info = Get(device); var description = device.description;
            report.Append($"#{device.deviceId} '{device.displayName}' layout={device.layout} iface={description.interfaceName} product='{description.product}' mfr='{description.manufacturer}'");
            if (info.inputIds.Length > 0) report.Append($" inputReports=[{string.Join(",", info.inputIds)}]");
            report.Append($" {info.note}"); if (info.expectedId >= 0) report.Append($" passed={info.passed} dropped={info.dropped}");
            if (!string.IsNullOrEmpty(description.product) && products[description.product] > 1) report.Append("  [DUPLICATE product name: same physical device may appear more than once]");
            report.Append('\n');
        }
        return report.ToString();
    }
    // @description - Writes the device report to the Unity log.
    // @param {string} why - short reason shown in the log line, e.g. "startup"
    // @return {Void} - nothing
    public static void LogDevices(string why) { Debug.Log($"[Input] devices ({why}):\n{DeviceReport()}"); }
}
