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
            if (repaint) { RenderTexture.active = menuRT; GL.Clear(true, true, new Color(0.1f, 0.1f, 0.12f, 1f)); }
            Vector2 sp = default; bool mouse = VrMouse.Enabled(VrManager.Active || forced, true) && VrMouse.TryScreenPos(out sp);
            Vector2 mp = default; if (mouse) { mp = VrMouse.MapToPanel(sp, Screen.width, Screen.height, TexW, TexH, UiScale); Event.current.mousePosition = mp; }   // IMGUI hover/click/drag hit-test in panel space
            router.DrawMenu();
            if (mouse) { Event.current.mousePosition = mp; VrMouse.DrawCursor(mp); }   // cursor on top, inside the menu texture
            if (repaint) RenderTexture.active = prev;
        }
        else if (showHud && hud)
        {
            if (repaint) { RenderTexture.active = hudRT; GL.Clear(true, true, new Color(0f, 0f, 0f, 1f)); }
            hud.Draw(); if (repaint) RenderTexture.active = prev;
        }
        GUI.matrix = m0; UiSurface.W = UiSurface.H = 0;
    }
    void OnDestroy() { if (hudRT) hudRT.Release(); if (menuRT) menuRT.Release(); }
}

// In-headset mouse pointer for the VR menu panel (iRacing-style): the OS cursor is not visible in the headset, so the desktop mouse
// position is mapped proportionally onto the menu texture (whole game window -> whole panel), a cursor is drawn into the texture,
// and IMGUI events get the mapped position so hover / click / slider drag / scroll work. Only while XR (or -vrpanels) is active and
// the menu is open (VrPanel only draws the menu then). The settings menu is IMGUI, so IMGUI's own event handling is the "UI event system" here.
public static class VrMouse
{
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
