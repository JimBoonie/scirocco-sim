// World-space panels for VR: the IMGUI debug HUD and settings menu are rendered into RenderTextures and shown on quads in the cockpit.
// Active when VR is on (or with -vrpanels for flat-screen checking). The menu is navigable with the bindable Menu* actions.
using UnityEngine;
public static class UiSurface
{
    public const float DesignHeight = 720f;   // UI layout reference height (px); larger screens scale the IMGUI up, never down
    public static void Begin(out Matrix4x4 old) { old = GUI.matrix; float s = Mathf.Max(1f, Screen.height / DesignHeight); GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f)); W = Mathf.RoundToInt(Screen.width / s); H = Mathf.RoundToInt(Screen.height / s); }
    public static void End(Matrix4x4 old) { GUI.matrix = old; W = H = 0; }
    public static int W = 0, H = 0; public static int Width => W > 0 ? W : Screen.width; public static int Height => H > 0 ? H : Screen.height; }
public class VrPanel : MonoBehaviour
{
    public static bool Active => VrManager.Active || forced;
    static bool forced;
    public DebugHUD hud; public InputRouter router; public Cockpit cockpit;
    public bool showHud = true;
    RenderTexture hudRT, menuRT; Transform hudQuad, menuQuad; Material hudMat, menuMat;
    // panel size/placement in the cockpit (m; layout choice for legibility at ~0.8-1 m, not physics)
    const int TexW = 1280, TexH = 720; const float UiScale = 1.5f;   // IMGUI drawn at 853x480 logical px, scaled 1.5x: ~13-14 px text at Index pixel density (~14 px/deg) at 0.6 m
    void Awake() { forced = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-vrpanels") >= 0; }
    Transform Quad(string n, out Material m, RenderTexture rt)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = n; Destroy(g.GetComponent<Collider>()); g.layer = 2;
        m = new Material(Shader.Find("Unlit/Texture")) { mainTexture = rt }; g.GetComponent<MeshRenderer>().sharedMaterial = m; return g.transform;
    }
    void Ensure()
    {
        if (hudRT || !cockpit || !cockpit.eyeAnchor) return;
        hudRT = new RenderTexture(TexW, TexH, 0, RenderTextureFormat.ARGB32) { name = "HudRT" }; menuRT = new RenderTexture(TexW, TexH, 0, RenderTextureFormat.ARGB32) { name = "MenuRT" };
        hudQuad = Quad("HudPanel", out hudMat, hudRT); menuQuad = Quad("MenuPanel", out menuMat, menuRT);
        // HUD: right of the steering wheel over the centre console, angled to the driver; menu: straight ahead at eye height
        hudQuad.SetParent(cockpit.eyeAnchor, false); hudQuad.localPosition = new Vector3(0.36f, -0.16f, 0.6f); hudQuad.localRotation = Quaternion.Euler(10f, 30f, 0f); hudQuad.localScale = new Vector3(0.48f, 0.27f, 1f);
        menuQuad.SetParent(cockpit.eyeAnchor, false); menuQuad.localPosition = new Vector3(0.05f, 0.04f, 0.6f); menuQuad.localRotation = Quaternion.identity; menuQuad.localScale = new Vector3(0.8f, 0.45f, 1f);
    }
    void LateUpdate()
    {
        bool on = Active; if (on) Ensure();
        if (hudQuad) hudQuad.gameObject.SetActive(on && showHud && !(router && router.menuOpen));
        if (menuQuad) menuQuad.gameObject.SetActive(on && router && router.menuOpen);
    }
    void OnGUI()
    {
        if (!Active || !hudRT) return;
        GUI.depth = -20; bool repaint = Event.current.type == EventType.Repaint; var prev = RenderTexture.active;
        UiSurface.W = Mathf.RoundToInt(TexW / UiScale); UiSurface.H = Mathf.RoundToInt(TexH / UiScale); var m0 = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));
        if (router && router.menuOpen)
        {
            Vector2 sp = default; bool mouse = VrMouse.Enabled(VrManager.Active || forced, true) && VrMouse.TryScreenPos(out sp);
            Vector2 mp = mouse ? VrMouse.MapToPanel(sp, Screen.width, Screen.height, TexW, TexH, UiScale) : default;
            if (mouse && VrMouse.Dispatch(pointer, mp, router.DrawMenu)) { }   // a queued click/drag/scroll replaced this Repaint: menu texture keeps last frame
            else if (mouse && VrMouse.IsPointerEvent(Event.current.type)) { }  // native desktop-window mouse events: ignored, the queue carries the clicks
            else
            {
                if (repaint) { RenderTexture.active = menuRT; GL.Clear(true, true, new Color(0.1f, 0.1f, 0.12f, 1f)); }
                if (mouse) VrMouse.WithMouse(mp, () => { router.DrawMenu(); VrMouse.DrawCursor(mp); });   // hover/keyboard with the mouse in panel space; cursor on top
                else router.DrawMenu();   // cursor on top, inside the menu texture
                if (repaint) RenderTexture.active = prev;
            }
        }
        else if (showHud && hud)
        {
            if (repaint) { RenderTexture.active = hudRT; GL.Clear(true, true, new Color(0f, 0f, 0f, 1f)); }
            hud.Draw(); if (repaint) RenderTexture.active = prev;
        }
        GUI.matrix = m0; UiSurface.W = UiSurface.H = 0;
    }
    readonly VrPointerQueue pointer = new VrPointerQueue();
    void Update()
    {   // sample the mouse through the Input System every frame (works while SteamVR owns window focus; OS clicks then never reach IMGUI)
        var ms = UnityEngine.InputSystem.Mouse.current;
        if (!Active || !router || !router.menuOpen || ms == null || !VrMouse.Enabled(VrManager.Active || forced, true)) { pointer.Reset(); return; }
        var mp = VrMouse.MapToPanel(ms.position.ReadValue(), Screen.width, Screen.height, TexW, TexH, UiScale);
        pointer.Sample(ms.leftButton.isPressed, mp, ms.scroll.ReadValue().y);
    }
    void OnDestroy() { if (hudRT) hudRT.Release(); if (menuRT) menuRT.Release(); }
}

// In-headset mouse pointer for the VR menu panel (iRacing-style): the OS cursor is not visible in the headset, so the desktop mouse
// position is mapped proportionally onto the menu texture (whole game window -> whole panel), a cursor is drawn into the texture,
// and IMGUI events get the mapped position so hover / click / slider drag / scroll work. Only while XR (or -vrpanels) is active and
// the menu is open (VrPanel only draws the menu then). The settings menu is IMGUI, so IMGUI's own event handling is the "UI event system" here.
public static class VrMouse
{
    /// Run `draw` with the IMGUI mouse at a panel position (logical, under the current GUI.matrix). Writing Event.current.mousePosition
    /// alone is not enough (that was the acdb0e1 bug): IMGUI's clip stack keeps the absolute window mouse position taken when OnGUI began,
    /// and every BeginArea / BeginGroup / BeginScrollView re-derives the event position from it, so inside the settings menu's layout area
    /// the buttons and sliders were hit-tested at the desktop mouse position (scaled for the desktop window), not at the drawn VR cursor.
    /// Fix: push a parent clip whose INPUT transform maps that absolute position onto the panel point (render transform unchanged),
    /// the mechanism UIElements' IMGUIContainer uses for the same problem. Falls back to the plain write if the internal API is missing.
    static System.Reflection.MethodInfo pushParent, popParent, absMouse, popClip, clipCount; static bool resolved;
    public static void WithMouse(Vector2 panelPos, System.Action draw)
    {
        if (!resolved)
        {
            resolved = true; var clip = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip"); const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            pushParent = clip?.GetMethod("Internal_PushParentClip", F, null, new[] { typeof(Matrix4x4), typeof(Matrix4x4), typeof(Rect) }, null);
            popParent = clip?.GetMethod("Internal_PopParentClip", F); absMouse = clip?.GetMethod("Internal_GetAbsoluteMousePosition", F);
            popClip = clip?.GetMethod("Internal_Pop", F); clipCount = clip?.GetMethod("Internal_GetCount", F);
        }
        if (pushParent == null || popParent == null || absMouse == null || popClip == null || clipCount == null) { Event.current.mousePosition = panelPos; draw(); return; }
        var m = GUI.matrix; var abs = (Vector2)absMouse.Invoke(null, null);
        var d = abs - (Vector2)m.MultiplyPoint3x4(panelPos);
        var shift = Matrix4x4.Translate(d);   // IMGUI decides "mouse inside this group" (else the event reads as Ignore) with the render transform, so
        // pointer passes (nothing is drawn) shift both; the Repaint pass keeps the render transform so the menu is drawn in place
        pushParent.Invoke(null, new object[] { Event.current.type == EventType.Repaint ? Matrix4x4.identity : shift, shift, new Rect(-100000f, -100000f, 200000f, 200000f) });
        int n = (int)clipCount.Invoke(null, null);
        try { GUI.matrix = m; draw(); }
        finally
        {   // a control that Use()s a mouse event can leave its BeginArea/ScrollView group open (IMGUI cleans up at the end of OnGUI);
            // close those first so the parent clip pops cleanly
            for (int k = (int)clipCount.Invoke(null, null); k > n; k--) popClip.Invoke(null, null);
            popParent.Invoke(null, null); GUI.matrix = m;
        }
    }
    public static bool IsPointerEvent(EventType t) => t == EventType.MouseDown || t == EventType.MouseUp || t == EventType.MouseDrag || t == EventType.ScrollWheel || t == EventType.ContextClick;
    /// Clicks for the VR menu: the native IMGUI mouse events only arrive when the desktop mirror window has OS focus and the OS cursor is
    /// over it, which in VR it usually is not (SteamVR / the compositor owns focus, runInBackground keeps us running). The cursor position
    /// came from the Input System, so it moved, but MouseDown/MouseUp never reached the menu. Now the Input System button/scroll state is
    /// turned into IMGUI events here: one queued event replaces a Repaint pass (the Layout pass just before it has laid the menu out),
    /// the panel's own native mouse events are ignored (they are in desktop-window coordinates, not panel coordinates). Returns true when
    /// a queued event was dispatched (the caller must not render this pass).
    public static bool Dispatch(VrPointerQueue q, Vector2 panelPos, System.Action draw)
    {
        var e = Event.current; if (e == null || e.type != EventType.Repaint || q.Count == 0) return false;
        var pe = q.Dequeue();
        e.type = pe.type; e.delta = pe.delta; e.button = 0; e.clickCount = pe.type == EventType.MouseDown ? 1 : 0; e.modifiers = EventModifiers.None;
        WithMouse(pe.pos, draw);
        e.type = EventType.Repaint;   // other OnGUI scripts this frame still get their Repaint
        return true;
    }
    public static bool Enabled(bool xrActive, bool menuOpen) => xrActive && menuOpen;
    public static bool TryScreenPos(out Vector2 p)
    {
        var m = UnityEngine.InputSystem.Mouse.current; p = default; if (m == null) return false;
        p = m.position.ReadValue(); return true;   // Input System: pixels, origin bottom-left of the game window
    }
    /// screen pixel (bottom-left origin) -> IMGUI logical position inside the menu texture (top-left origin, divided by the UI scale)
    public static Vector2 MapToPanel(Vector2 screen, float screenW, float screenH, float texW, float texH, float uiScale)
    {
        float u = Mathf.Clamp01(screen.x / Mathf.Max(1f, screenW)), v = Mathf.Clamp01(screen.y / Mathf.Max(1f, screenH));
        return new Vector2(u * texW / uiScale, (1f - v) * texH / uiScale);
    }
    static Texture2D tex;
    public static Texture2D CursorTexture()
    {   // 16x24 arrow, white with black outline (generated: no asset to import)
        if (tex) return tex;
        const int W = 16, H = 24; tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            int r = y; bool inside = x <= r * 0.6f && r < 17 || (r >= 17 && x >= 4 && x <= 7 && r < 23 && x <= (r - 17) * 0.5f + 7);
            bool edge = inside && (x == 0 || x >= r * 0.6f - 1f && r < 17 || r == 16 || r == 22);
            tex.SetPixel(x, H - 1 - y, inside ? (edge ? Color.black : Color.white) : Color.clear);
        }
        tex.Apply(); return tex;
    }
    public static void DrawCursor(Vector2 p)
    {
        if (Event.current.type != EventType.Repaint) return;
        var t = CursorTexture(); var d = GUI.depth; GUI.DrawTexture(new Rect(p.x, p.y, t.width, t.height), t); GUI.depth = d;
    }
}

/// Input System mouse state (panel coordinates) -> queue of IMGUI pointer events (down / drag / up / scroll). Pure: unit-tested.
public class VrPointerQueue
{
    public struct Evt { public EventType type; public Vector2 pos, delta; }
    readonly System.Collections.Generic.Queue<Evt> q = new System.Collections.Generic.Queue<Evt>();
    bool down; Vector2 last;
    public int Count => q.Count;
    public Evt Dequeue() => q.Dequeue();
    public void Reset() { q.Clear(); down = false; }
    public void Sample(bool pressed, Vector2 pos, float scrollY)
    {
        if (pressed && !down) Add(EventType.MouseDown, pos, Vector2.zero);
        else if (pressed && pos != last) Add(EventType.MouseDrag, pos, pos - last);
        if (!pressed && down) Add(EventType.MouseUp, pos, Vector2.zero);
        if (scrollY != 0f) Add(EventType.ScrollWheel, pos, new Vector2(0f, -scrollY / 40f));   // Windows: 120 per notch -> IMGUI ~3 lines, down = +
        down = pressed; last = pos;
        while (q.Count > 16) q.Dequeue();
    }
    void Add(EventType t, Vector2 p, Vector2 d)
    {   // coalesce consecutive drags (one per frame is enough; a click is never dropped)
        if (t == EventType.MouseDrag && q.Count > 0) { var arr = q.ToArray(); var l = arr[arr.Length - 1]; if (l.type == EventType.MouseDrag) { q.Clear(); for (int i = 0; i < arr.Length - 1; i++) q.Enqueue(arr[i]); d += l.delta; } }
        q.Enqueue(new Evt { type = t, pos = p, delta = d });
    }
}
