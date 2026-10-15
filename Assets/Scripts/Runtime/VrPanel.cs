// World-space panels for VR: the IMGUI debug HUD and settings menu are rendered into RenderTextures and shown on quads in the cockpit.
// Active when VR is on (or with -vrpanels for flat-screen checking). The menu is navigable with the bindable Menu* actions.
using UnityEngine;
public static class UiSurface
{
    public const float DesignHeight = 720f;   // UI layout reference height (px); larger screens scale the IMGUI up, never down
    // @description - Starts drawing IMGUI at the design scale: larger screens scale the UI up (never down) relative to 720 px height, and the logical width and height are published for layout.
    // @param {Matrix4x4} previousMatrix - receives the previous GUI matrix, to be passed to End
    // @return {Void} - nothing
    public static void Begin(out Matrix4x4 previousMatrix) { previousMatrix = GUI.matrix; float scale = Mathf.Max(1f, Screen.height / DesignHeight); GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f)); LogicalWidth = Mathf.RoundToInt(Screen.width / scale); LogicalHeight = Mathf.RoundToInt(Screen.height / scale); }
    // @description - Ends drawing at the design scale: restores the GUI matrix and clears the logical size.
    // @param {Matrix4x4} previousMatrix - GUI matrix returned by Begin
    // @return {Void} - nothing
    public static void End(Matrix4x4 previousMatrix) { GUI.matrix = previousMatrix; LogicalWidth = LogicalHeight = 0; }
    public static int LogicalWidth = 0, LogicalHeight = 0; public static int Width => LogicalWidth > 0 ? LogicalWidth : Screen.width; public static int Height => LogicalHeight > 0 ? LogicalHeight : Screen.height; }
public class VrPanel : MonoBehaviour
{
    public static bool Active => VrManager.Active || forced;
    static bool forced;
    public DebugHUD hud; public InputRouter router; public Cockpit cockpit;
    public bool showHud = true;
    RenderTexture hudRT, menuRT; Transform hudQuad, menuQuad; Material hudMat, menuMat;
    // panel size/placement in the cockpit (m; layout choice for legibility at ~0.8-1 m, not physics)
    const int TexW = 1280, TexH = 720; const float UiScale = 1.5f;   // IMGUI drawn at 853x480 logical px, scaled 1.5x: ~13-14 px text at Index pixel density (~14 px/deg) at 0.6 m
    // @description - Reads the -vrpanels flag, which shows the panels on the flat screen for checking without a headset.
    // @return {Void} - nothing
    void Awake() { forced = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-vrpanels") >= 0; }
    // @description - Creates an unlit quad that shows a render texture, with its collider removed and put on the ignore-raycast layer.
    // @param {string} objectName - name of the quad object
    // @param {Material} material - receives the material created for the quad
    // @param {RenderTexture} renderTexture - render texture to display
    // @return {Transform} - the transform of the new quad
    Transform Quad(string objectName, out Material material, RenderTexture renderTexture)
    {
        var quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad); quadObject.name = objectName; Destroy(quadObject.GetComponent<Collider>()); quadObject.layer = 2;
        material = new Material(Shader.Find("Unlit/Texture")) { mainTexture = renderTexture }; quadObject.GetComponent<MeshRenderer>().sharedMaterial = material; return quadObject.transform;
    }
    // @description - Creates the HUD and menu textures and quads once the cockpit eye anchor exists. The HUD sits right of the wheel over the center console, the menu straight ahead at eye height (offsets in meters relative to the eye anchor).
    // @return {Void} - nothing
    void Ensure()
    {
        if (hudRT || !cockpit || !cockpit.eyeAnchor) return;
        hudRT = new RenderTexture(TexW, TexH, 0, RenderTextureFormat.ARGB32) { name = "HudRT" }; menuRT = new RenderTexture(TexW, TexH, 0, RenderTextureFormat.ARGB32) { name = "MenuRT" };
        hudQuad = Quad("HudPanel", out hudMat, hudRT); menuQuad = Quad("MenuPanel", out menuMat, menuRT);
        // HUD: right of the steering wheel over the center console, angled to the driver; menu: straight ahead at eye height
        hudQuad.SetParent(cockpit.eyeAnchor, false); hudQuad.localPosition = new Vector3(0.36f, -0.16f, 0.6f); hudQuad.localRotation = Quaternion.Euler(10f, 30f, 0f); hudQuad.localScale = new Vector3(0.48f, 0.27f, 1f);
        menuQuad.SetParent(cockpit.eyeAnchor, false); menuQuad.localPosition = new Vector3(0.05f, 0.04f, 0.6f); menuQuad.localRotation = Quaternion.identity; menuQuad.localScale = new Vector3(0.8f, 0.45f, 1f);
    }
    // @description - Shows the HUD quad when the HUD is enabled and the menu is closed, and the menu quad only while the menu is open.
    // @return {Void} - nothing
    void LateUpdate()
    {
        bool panelsActive = Active; if (panelsActive) Ensure();
        if (hudQuad) hudQuad.gameObject.SetActive(panelsActive && showHud && !(router && router.menuOpen));
        if (menuQuad) menuQuad.gameObject.SetActive(panelsActive && router && router.menuOpen);
    }
    // @description - Draws the HUD or the menu into its render texture. For the menu with a desktop mouse, queued pointer events (click, drag, scroll) replace the repaint pass so IMGUI controls react at the mapped cursor position, and a cursor is drawn on top.
    // @return {Void} - nothing
    void OnGUI()
    {
        if (!Active || !hudRT) return;
        GUI.depth = -20; bool repaint = Event.current.type == EventType.Repaint; var previousTarget = RenderTexture.active;
        UiSurface.LogicalWidth = Mathf.RoundToInt(TexW / UiScale); UiSurface.LogicalHeight = Mathf.RoundToInt(TexH / UiScale); var previousMatrix = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));
        if (router && router.menuOpen)
        {
            Vector2 screenPosition = default; bool hasMouse = VrMouse.Enabled(VrManager.Active || forced, true) && VrMouse.TryScreenPos(out screenPosition);
            Vector2 panelPosition = hasMouse ? VrMouse.MapToPanel(screenPosition, Screen.width, Screen.height, TexW, TexH, UiScale) : default;
            if (hasMouse && VrMouse.Dispatch(pointer, panelPosition, router.DrawMenu)) { }   // a queued click/drag/scroll replaced this Repaint: menu texture keeps last frame
            else if (hasMouse && VrMouse.IsPointerEvent(Event.current.type)) { }  // native desktop-window mouse events: ignored, the queue carries the clicks
            else
            {
                if (repaint) { RenderTexture.active = menuRT; GL.Clear(true, true, new Color(0.1f, 0.1f, 0.12f, 1f)); }
                if (hasMouse) VrMouse.WithMouse(panelPosition, () => { router.DrawMenu(); VrMouse.DrawCursor(panelPosition); });   // hover/keyboard with the mouse in panel space; cursor on top
                else router.DrawMenu();   // cursor on top, inside the menu texture
                if (repaint) RenderTexture.active = previousTarget;
            }
        }
        else if (showHud && hud)
        {
            if (repaint) { RenderTexture.active = hudRT; GL.Clear(true, true, new Color(0f, 0f, 0f, 1f)); }
            hud.Draw(); if (repaint) RenderTexture.active = previousTarget;
        }
        GUI.matrix = previousMatrix; UiSurface.LogicalWidth = UiSurface.LogicalHeight = 0;
    }
    readonly VrPointerQueue pointer = new VrPointerQueue();
    // @description - Samples the Input System mouse every frame (it keeps working while SteamVR owns window focus) and queues pointer events for the menu panel.
    // @return {Void} - nothing
    void Update()
    {   // sample the mouse through the Input System every frame (works while SteamVR owns window focus; OS clicks then never reach IMGUI)
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (!Active || !router || !router.menuOpen || mouse == null || !VrMouse.Enabled(VrManager.Active || forced, true)) { pointer.Reset(); return; }
        var panelPosition = VrMouse.MapToPanel(mouse.position.ReadValue(), Screen.width, Screen.height, TexW, TexH, UiScale);
        pointer.Sample(mouse.leftButton.isPressed, panelPosition, mouse.scroll.ReadValue().y);
    }
    // @description - Releases the render textures.
    // @return {Void} - nothing
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
    // @description - Runs an IMGUI draw call with the mouse placed at a point on the VR panel. A parent clip is pushed so that its input transform maps IMGUI's absolute mouse position onto the panel point (the fix for the VR menu click bug); falls back to writing Event.current.mousePosition if the internal GUIClip API is missing. Any clips left open by the drawing are closed before the parent clip is popped.
    // @param {Vector2} panelPos - mouse position on the panel in logical UI pixels (origin top left)
    // @param {System.Action} draw - IMGUI drawing action to run with that mouse position
    // @return {Void} - nothing
    public static void WithMouse(Vector2 panelPos, System.Action draw)
    {
        if (!resolved)
        {
            resolved = true; var guiClipType = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip"); const System.Reflection.BindingFlags bindingFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            pushParent = guiClipType?.GetMethod("Internal_PushParentClip", bindingFlags, null, new[] { typeof(Matrix4x4), typeof(Matrix4x4), typeof(Rect) }, null);
            popParent = guiClipType?.GetMethod("Internal_PopParentClip", bindingFlags); absMouse = guiClipType?.GetMethod("Internal_GetAbsoluteMousePosition", bindingFlags);
            popClip = guiClipType?.GetMethod("Internal_Pop", bindingFlags); clipCount = guiClipType?.GetMethod("Internal_GetCount", bindingFlags);
        }
        if (pushParent == null || popParent == null || absMouse == null || popClip == null || clipCount == null) { Event.current.mousePosition = panelPos; draw(); return; }
        var guiMatrix = GUI.matrix; var absoluteMouse = (Vector2)absMouse.Invoke(null, null);
        var offset = absoluteMouse - (Vector2)guiMatrix.MultiplyPoint3x4(panelPos);
        var shiftMatrix = Matrix4x4.Translate(offset);   // IMGUI decides "mouse inside this group" (else the event reads as Ignore) with the render transform, so
        // pointer passes (nothing is drawn) shift both; the Repaint pass keeps the render transform so the menu is drawn in place
        pushParent.Invoke(null, new object[] { Event.current.type == EventType.Repaint ? Matrix4x4.identity : shiftMatrix, shiftMatrix, new Rect(-100000f, -100000f, 200000f, 200000f) });
        int openClipCount = (int)clipCount.Invoke(null, null);
        try { GUI.matrix = guiMatrix; draw(); }
        finally
        {   // a control that Use()s a mouse event can leave its BeginArea/ScrollView group open (IMGUI cleans up at the end of OnGUI);
            // close those first so the parent clip pops cleanly
            for (int clipIndex = (int)clipCount.Invoke(null, null); clipIndex > openClipCount; clipIndex--) popClip.Invoke(null, null);
            popParent.Invoke(null, null); GUI.matrix = guiMatrix;
        }
    }
    // @description - Tells whether an IMGUI event type is a mouse event that the VR panel gets from its own queue instead.
    // @param {EventType} eventType - event type to test
    // @return {bool} - true for mouse down, up, drag, scroll wheel and context click
    public static bool IsPointerEvent(EventType eventType) => eventType == EventType.MouseDown || eventType == EventType.MouseUp || eventType == EventType.MouseDrag || eventType == EventType.ScrollWheel || eventType == EventType.ContextClick;
    // @description - Replaces the current Repaint event by the next queued pointer event and runs the draw call with it, so clicks, drags and scrolls reach the menu controls. The event is turned back into a Repaint afterwards so other OnGUI scripts still repaint.
    // @param {VrPointerQueue} queue - queue of pointer events
    // @param {Vector2} panelPos - current mouse position on the panel in logical UI pixels (unused: each event carries its own position)
    // @param {System.Action} draw - IMGUI drawing action to run
    // @return {bool} - true if an event was dispatched (this repaint is consumed), false if nothing was queued
    public static bool Dispatch(VrPointerQueue queue, Vector2 panelPos, System.Action draw)
    {
        var currentEvent = Event.current; if (currentEvent == null || currentEvent.type != EventType.Repaint || queue.Count == 0) return false;
        var pointerEvent = queue.Dequeue();
        currentEvent.type = pointerEvent.type; currentEvent.delta = pointerEvent.delta; currentEvent.button = 0; currentEvent.clickCount = pointerEvent.type == EventType.MouseDown ? 1 : 0; currentEvent.modifiers = EventModifiers.None;
        WithMouse(pointerEvent.pos, draw);
        currentEvent.type = EventType.Repaint;   // other OnGUI scripts this frame still get their Repaint
        return true;
    }
    // @description - Tells whether the VR mouse is active: only while XR (or -vrpanels) is on and the menu is open.
    // @param {bool} xrActive - true if VR or the -vrpanels flag is active
    // @param {bool} menuOpen - true while the settings menu is open
    // @return {bool} - true if the mouse should drive the menu
    public static bool Enabled(bool xrActive, bool menuOpen) => xrActive && menuOpen;
    // @description - Reads the desktop mouse position from the Input System.
    // @param {Vector2} screenPosition - receives the position in pixels, origin at the bottom left of the game window
    // @return {bool} - true if a mouse exists
    public static bool TryScreenPos(out Vector2 screenPosition)
    {
        var mouse = UnityEngine.InputSystem.Mouse.current; screenPosition = default; if (mouse == null) return false;
        screenPosition = mouse.position.ReadValue(); return true;   // Input System: pixels, origin bottom-left of the game window
    }
    // @description - Maps the desktop mouse position proportionally onto the menu texture: the whole game window covers the whole panel. Converts to logical UI pixels with a flipped vertical axis (origin top left).
    // @param {Vector2} screen - mouse position in pixels, origin bottom left of the window
    // @param {float} screenWidth - game window width in pixels
    // @param {float} screenHeight - game window height in pixels
    // @param {float} textureWidth - menu texture width in pixels
    // @param {float} textureHeight - menu texture height in pixels
    // @param {float} uiScale - IMGUI scale factor of the panel (dimensionless)
    // @return {Vector2} - position on the panel in logical UI pixels, origin top left
    public static Vector2 MapToPanel(Vector2 screen, float screenWidth, float screenHeight, float textureWidth, float textureHeight, float uiScale)
    {
        float horizontalFraction = Mathf.Clamp01(screen.x / Mathf.Max(1f, screenWidth)), verticalFraction = Mathf.Clamp01(screen.y / Mathf.Max(1f, screenHeight));
        return new Vector2(horizontalFraction * textureWidth / uiScale, (1f - verticalFraction) * textureHeight / uiScale);
    }
    static Texture2D tex;
    // @description - Generates (once) a 16 by 24 pixel arrow cursor, white with a black outline, so no image asset is needed.
    // @return {Texture2D} - the cursor texture
    public static Texture2D CursorTexture()
    {   // 16x24 arrow, white with black outline (generated: no asset to import)
        if (tex) return tex;
        const int width = 16, height = 24; tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
        for (int rowIndex = 0; rowIndex < height; rowIndex++) for (int column = 0; column < width; column++)
        {
            int row = rowIndex; bool inside = column <= row * 0.6f && row < 17 || (row >= 17 && column >= 4 && column <= 7 && row < 23 && column <= (row - 17) * 0.5f + 7);
            bool edge = inside && (column == 0 || column >= row * 0.6f - 1f && row < 17 || row == 16 || row == 22);
            tex.SetPixel(column, height - 1 - rowIndex, inside ? (edge ? Color.black : Color.white) : Color.clear);
        }
        tex.Apply(); return tex;
    }
    // @description - Draws the arrow cursor into the current IMGUI surface at a position, on repaint events only.
    // @param {Vector2} position - cursor position in logical UI pixels, origin top left
    // @return {Void} - nothing
    public static void DrawCursor(Vector2 position)
    {
        if (Event.current.type != EventType.Repaint) return;
        var cursorTexture = CursorTexture(); var previousDepth = GUI.depth; GUI.DrawTexture(new Rect(position.x, position.y, cursorTexture.width, cursorTexture.height), cursorTexture); GUI.depth = previousDepth;
    }
}

/// Input System mouse state (panel coordinates) -> queue of IMGUI pointer events (down / drag / up / scroll). Pure: unit-tested.
public class VrPointerQueue
{
    public struct Evt { public EventType type; public Vector2 pos, delta; }
    readonly System.Collections.Generic.Queue<Evt> q = new System.Collections.Generic.Queue<Evt>();
    bool down; Vector2 last;
    public int Count => q.Count;
    // @description - Removes and returns the oldest queued pointer event.
    // @return {Evt} - the oldest pointer event
    public Evt Dequeue() => q.Dequeue();
    // @description - Clears all queued events and the pressed state, for example when the menu closes.
    // @return {Void} - nothing
    public void Reset() { q.Clear(); down = false; }
    // @description - Turns the mouse state of one frame into IMGUI events: mouse down on press, drag while moving, up on release, scroll. Keeps at most 16 events.
    // @param {bool} pressed - true while the left button is down
    // @param {Vector2} pos - mouse position on the panel in logical UI pixels
    // @param {float} scrollY - vertical scroll amount this frame in raw wheel units (120 per notch on Windows)
    // @return {Void} - nothing
    public void Sample(bool pressed, Vector2 pos, float scrollY)
    {
        if (pressed && !down) Add(EventType.MouseDown, pos, Vector2.zero);
        else if (pressed && pos != last) Add(EventType.MouseDrag, pos, pos - last);
        if (!pressed && down) Add(EventType.MouseUp, pos, Vector2.zero);
        if (scrollY != 0f) Add(EventType.ScrollWheel, pos, new Vector2(0f, -scrollY / 40f));   // Windows: 120 per notch -> IMGUI ~3 lines, down = +
        down = pressed; last = pos;
        while (q.Count > 16) q.Dequeue();
    }
    // @description - Queues a pointer event. A drag directly after another drag is merged into it (deltas added) so a click is never dropped.
    // @param {EventType} eventType - event type
    // @param {Vector2} position - position in logical UI pixels
    // @param {Vector2} delta - movement since the previous event in logical UI pixels
    // @return {Void} - nothing
    void Add(EventType eventType, Vector2 position, Vector2 delta)
    {   // coalesce consecutive drags (one per frame is enough; a click is never dropped)
        if (eventType == EventType.MouseDrag && q.Count > 0) { var queuedEvents = q.ToArray(); var lastEvent = queuedEvents[queuedEvents.Length - 1]; if (lastEvent.type == EventType.MouseDrag) { q.Clear(); for (int i = 0; i < queuedEvents.Length - 1; i++) q.Enqueue(queuedEvents[i]); delta += lastEvent.delta; } }
        q.Enqueue(new Evt { type = eventType, pos = position, delta = delta });
    }
}
