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
            router.DrawMenu(); if (repaint) RenderTexture.active = prev;
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
