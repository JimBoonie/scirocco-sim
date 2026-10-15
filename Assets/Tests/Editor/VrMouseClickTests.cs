// VR menu mouse: Input System button state -> queued IMGUI events -> real IMGUI controls (button onClick, slider drag), with the panel's
// 1.5x GUI.matrix, without OS focus / native mouse events (the headset case). IMGUI is driven headlessly through GUIUtility.BeginGUI/EndGUI.
using NUnit.Framework; using UnityEngine; using System; using System.Reflection;
public class VrMouseClickTests
{
    static readonly MethodInfo BeginGui = typeof(GUIUtility).GetMethod("BeginGUI", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo EndGui = typeof(GUIUtility).GetMethod("EndGUI", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo BeginContainer = typeof(GUIUtility).GetMethod("BeginContainer", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo EndContainer = typeof(GUIUtility).GetMethod("Internal_EndContainer", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly object State = Activator.CreateInstance(typeof(GUIUtility).Assembly.GetType("UnityEngine.ObjectGUIState"), true);   // what IMGUIContainer uses to enter OnGUI
    const int ControlId = 424242;
    // @description - Runs one OnGUI pass headlessly as Unity would for an IMGUIContainer: sets the event type, applies the panel's 1.5x GUI matrix and runs VrPanel's dispatch logic (queued VR pointer events, otherwise the plain mouse path).
    // @param {EventType} eventType - IMGUI event type of this pass (Layout, Repaint or a mouse event)
    // @param {VrPointerQueue} queue - queued VR pointer events to dispatch
    // @param {Vector2} cursor - VR cursor position in panel logical pixels
    // @param {Action} draw - the IMGUI drawing code under test
    // @return {Void} - nothing
    static void Pass(EventType eventType, VrPointerQueue queue, Vector2 cursor, Action draw)
    {
        Event.current = new Event { type = eventType, mousePosition = new Vector2(-999f, -999f) };   // native position is meaningless (desktop window)
        BeginContainer.Invoke(null, new[] { State }); BeginGui.Invoke(null, new object[] { 0, ControlId, 1 });
        try
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(1.5f, 1.5f, 1f));
            if (VrMouse.Dispatch(queue, cursor, draw)) { }
            else if (VrMouse.IsPointerEvent(Event.current.type)) { }
            else VrMouse.WithMouse(cursor, draw);
        }
        finally { GUI.matrix = Matrix4x4.identity; EndGui.Invoke(null, new object[] { 1 }); EndContainer.Invoke(null, null); }
    }
    // @description - Runs a Layout pass followed by a Repaint pass, which is one IMGUI frame.
    // @param {VrPointerQueue} queue - queued VR pointer events to dispatch
    // @param {Vector2} cursor - VR cursor position in panel logical pixels
    // @param {Action} draw - the IMGUI drawing code under test
    // @return {Void} - nothing
    static void Frame(VrPointerQueue queue, Vector2 cursor, Action draw) { Pass(EventType.Layout, queue, cursor, draw); Pass(EventType.Repaint, queue, cursor, draw); }

    // @description - Verifies that sampled pointer states become events in order: mouse down, one drag with the two drags coalesced (delta 20), mouse up, and a scroll wheel event with positive delta, leaving the queue empty.
    // @return {Void} - nothing
    [Test] public void Queue_Down_Drag_Up_Scroll()
    {
        var queue = new VrPointerQueue();
        queue.Sample(true, new Vector2(10, 10), 0f); queue.Sample(true, new Vector2(20, 10), 0f); queue.Sample(true, new Vector2(30, 10), 0f); queue.Sample(false, new Vector2(30, 10), -120f);
        Assert.AreEqual(EventType.MouseDown, queue.Dequeue().type);
        var dragEvent = queue.Dequeue(); Assert.AreEqual(EventType.MouseDrag, dragEvent.type); Assert.AreEqual(20f, dragEvent.delta.x, 1e-4);   // two drags coalesced
        Assert.AreEqual(EventType.MouseUp, queue.Dequeue().type);
        var scrollEvent = queue.Dequeue(); Assert.AreEqual(EventType.ScrollWheel, scrollEvent.type); Assert.Greater(scrollEvent.delta.y, 0f);
        Assert.AreEqual(0, queue.Count);
    }
    // @description - Verifies that a VR cursor hovering does not click a laid-out IMGUI button, a press and release on it fires it once, and a press and release 200 px away does not.
    // @return {Void} - nothing
    [Test] public void Click_Fires_Layout_Button_Through_Queue()
    {
        var queue = new VrPointerQueue(); int clicks = 0; Rect button = default;
        // @description - Draws the settings area: a header label and a Settings button whose rectangle is recorded; a click increments the click counter.
        // @return {Void} - nothing
        void Draw()
        {
            GUILayout.BeginArea(new Rect(30, 24, 500, 300));
            GUILayout.Label("SETTINGS");
            if (GUILayout.Button("Settings", GUILayout.Width(120))) clicks++;
            if (Event.current.type == EventType.Repaint) button = GUILayoutUtility.GetLastRect();
            GUILayout.EndArea();
        }
        Frame(queue, Vector2.zero, Draw); Assert.Greater(button.width, 0f);
        var cursorPoint = new Vector2(30 + button.center.x, 24 + button.center.y);   // panel logical coords (what MapToPanel returns)
        Frame(queue, cursorPoint, Draw); Assert.AreEqual(0, clicks, "hover alone must not click");
        queue.Sample(true, cursorPoint, 0f); Frame(queue, cursorPoint, Draw);
        queue.Sample(false, cursorPoint, 0f); Frame(queue, cursorPoint, Draw);
        Assert.AreEqual(1, clicks, "VR cursor click must fire the button");
        var offButtonPoint = new Vector2(cursorPoint.x, cursorPoint.y + 200f); queue.Sample(true, offButtonPoint, 0f); Frame(queue, offButtonPoint, Draw); queue.Sample(false, offButtonPoint, 0f); Frame(queue, offButtonPoint, Draw);
        Assert.AreEqual(1, clicks, "click elsewhere must not fire it");
    }
    // @description - Verifies that dragging the VR cursor across a horizontal slider inside nested group and scroll view moves it above 0.8.
    // @return {Void} - nothing
    [Test] public void Drag_Moves_Slider_Through_Queue()
    {
        var queue = new VrPointerQueue(); float sliderValue = 0f; var sliderRect = new Rect(50, 50, 200, 20);
        var scrollPosition = Vector2.zero;
        // @description - Draws a nested group and scroll view containing a horizontal slider (0..1), as in the menu layout.
        // @return {Void} - nothing
        void Draw() { GUI.BeginGroup(new Rect(20, 16, 600, 400)); scrollPosition = GUI.BeginScrollView(new Rect(0, 0, 600, 400), scrollPosition, new Rect(0, 0, 580, 800)); sliderValue = GUI.HorizontalSlider(sliderRect, sliderValue, 0f, 1f); GUI.EndScrollView(); GUI.EndGroup(); }   // menu-like nesting
        var dragStart = new Vector2(20 + sliderRect.x + 4f, 16 + sliderRect.center.y); var dragEnd = new Vector2(20 + sliderRect.xMax - 4f, 16 + sliderRect.center.y);
        queue.Sample(true, dragStart, 0f); Frame(queue, dragStart, Draw);
        queue.Sample(true, dragEnd, 0f); Frame(queue, dragEnd, Draw);
        queue.Sample(false, dragEnd, 0f); Frame(queue, dragEnd, Draw);
        Assert.Greater(sliderValue, 0.8f, "slider must follow the VR cursor drag");
    }
    // @description - Verifies VrMouse.MapToPanel for the window's bottom-left corner (0, 480) and the window centre (426.67, 240) on a 1280x720 panel at 1.5 scale.
    // @return {Void} - nothing
    [Test] public void MapToPanel_Corners()
    {
        Assert.AreEqual(new Vector2(0f, 480f), VrMouse.MapToPanel(Vector2.zero, 1920, 1080, 1280, 720, 1.5f));
        var panelPoint = VrMouse.MapToPanel(new Vector2(960, 540), 1920, 1080, 1280, 720, 1.5f); Assert.AreEqual(426.67f, panelPoint.x, 0.01f); Assert.AreEqual(240f, panelPoint.y, 0.01f);
    }
}
