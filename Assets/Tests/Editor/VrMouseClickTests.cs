// VR menu mouse: Input System button state -> queued IMGUI events -> real IMGUI controls (button onClick, slider drag), with the panel's
// 1.5x GUI.matrix, without OS focus / native mouse events (the headset case). IMGUI is driven headlessly through GUIUtility.BeginGUI/EndGUI.
using NUnit.Framework; using UnityEngine; using System; using System.Reflection;
public class VrMouseClickTests
{
    static readonly MethodInfo Begin = typeof(GUIUtility).GetMethod("BeginGUI", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo End = typeof(GUIUtility).GetMethod("EndGUI", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo BeginC = typeof(GUIUtility).GetMethod("BeginContainer", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo EndC = typeof(GUIUtility).GetMethod("Internal_EndContainer", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly object State = Activator.CreateInstance(typeof(GUIUtility).Assembly.GetType("UnityEngine.ObjectGUIState"), true);   // what IMGUIContainer uses to enter OnGUI
    const int Id = 424242;
    // one OnGUI call as Unity makes it: given event type, panel matrix, VrPanel's dispatch logic
    static void Pass(EventType t, VrPointerQueue q, Vector2 cursor, Action draw)
    {
        Event.current = new Event { type = t, mousePosition = new Vector2(-999f, -999f) };   // native position is meaningless (desktop window)
        BeginC.Invoke(null, new[] { State }); Begin.Invoke(null, new object[] { 0, Id, 1 });
        try
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(1.5f, 1.5f, 1f));
            if (VrMouse.Dispatch(q, cursor, draw)) { }
            else if (VrMouse.IsPointerEvent(Event.current.type)) { }
            else VrMouse.WithMouse(cursor, draw);
        }
        finally { GUI.matrix = Matrix4x4.identity; End.Invoke(null, new object[] { 1 }); EndC.Invoke(null, null); }
    }
    static void Frame(VrPointerQueue q, Vector2 cursor, Action draw) { Pass(EventType.Layout, q, cursor, draw); Pass(EventType.Repaint, q, cursor, draw); }

    [Test] public void Queue_Down_Drag_Up_Scroll()
    {
        var q = new VrPointerQueue();
        q.Sample(true, new Vector2(10, 10), 0f); q.Sample(true, new Vector2(20, 10), 0f); q.Sample(true, new Vector2(30, 10), 0f); q.Sample(false, new Vector2(30, 10), -120f);
        Assert.AreEqual(EventType.MouseDown, q.Dequeue().type);
        var d = q.Dequeue(); Assert.AreEqual(EventType.MouseDrag, d.type); Assert.AreEqual(20f, d.delta.x, 1e-4);   // two drags coalesced
        Assert.AreEqual(EventType.MouseUp, q.Dequeue().type);
        var s = q.Dequeue(); Assert.AreEqual(EventType.ScrollWheel, s.type); Assert.Greater(s.delta.y, 0f);
        Assert.AreEqual(0, q.Count);
    }
    [Test] public void Click_Fires_Layout_Button_Through_Queue()
    {
        var q = new VrPointerQueue(); int clicks = 0; Rect btn = default;
        void Draw()
        {
            GUILayout.BeginArea(new Rect(30, 24, 500, 300));
            GUILayout.Label("SETTINGS");
            if (GUILayout.Button("Settings", GUILayout.Width(120))) clicks++;
            if (Event.current.type == EventType.Repaint) btn = GUILayoutUtility.GetLastRect();
            GUILayout.EndArea();
        }
        Frame(q, Vector2.zero, Draw); Assert.Greater(btn.width, 0f);
        var c = new Vector2(30 + btn.center.x, 24 + btn.center.y);   // panel logical coords (what MapToPanel returns)
        Frame(q, c, Draw); Assert.AreEqual(0, clicks, "hover alone must not click");
        q.Sample(true, c, 0f); Frame(q, c, Draw);
        q.Sample(false, c, 0f); Frame(q, c, Draw);
        Assert.AreEqual(1, clicks, "VR cursor click must fire the button");
        var off = new Vector2(c.x, c.y + 200f); q.Sample(true, off, 0f); Frame(q, off, Draw); q.Sample(false, off, 0f); Frame(q, off, Draw);
        Assert.AreEqual(1, clicks, "click elsewhere must not fire it");
    }
    [Test] public void Drag_Moves_Slider_Through_Queue()
    {
        var q = new VrPointerQueue(); float v = 0f; var r = new Rect(50, 50, 200, 20);
        var sv = Vector2.zero;
        void Draw() { GUI.BeginGroup(new Rect(20, 16, 600, 400)); sv = GUI.BeginScrollView(new Rect(0, 0, 600, 400), sv, new Rect(0, 0, 580, 800)); v = GUI.HorizontalSlider(r, v, 0f, 1f); GUI.EndScrollView(); GUI.EndGroup(); }   // menu-like nesting
        var a = new Vector2(20 + r.x + 4f, 16 + r.center.y); var b = new Vector2(20 + r.xMax - 4f, 16 + r.center.y);
        q.Sample(true, a, 0f); Frame(q, a, Draw);
        q.Sample(true, b, 0f); Frame(q, b, Draw);
        q.Sample(false, b, 0f); Frame(q, b, Draw);
        Assert.Greater(v, 0.8f, "slider must follow the VR cursor drag");
    }
    [Test] public void MapToPanel_Corners()
    {
        Assert.AreEqual(new Vector2(0f, 480f), VrMouse.MapToPanel(Vector2.zero, 1920, 1080, 1280, 720, 1.5f));
        var m = VrMouse.MapToPanel(new Vector2(960, 540), 1920, 1080, 1280, 720, 1.5f); Assert.AreEqual(426.67f, m.x, 0.01f); Assert.AreEqual(240f, m.y, 0.01f);
    }
}
