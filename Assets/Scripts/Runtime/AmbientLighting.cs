// Visual only (no physics): Trilight ambient approximating sky + bounce off the dry-grass/aerial terrain, plus a weak shadowless fill
// light opposite the sun, so shadow sides of buildings aren't near-black. "Ambient brightness" (Quick tab, saved in the profile) scales both.
using UnityEngine;
public static class AmbientLighting
{
    // gradient at brightness 1.0 (perceptual choices, linear-ish colours): sky = clear Monterey sky, equator = haze, ground = sunlit dry grass bounce
    static readonly Color Sky = new Color(0.50f, 0.58f, 0.72f), Equator = new Color(0.46f, 0.45f, 0.40f), Ground = new Color(0.33f, 0.30f, 0.23f);
    const float FillFraction = 0.12f;   // fill intensity = 12 % of the sun x brightness, no shadows
    static float applied = -1f; static Light fill;
    public static void Apply(float brightness)
    {
        brightness = Mathf.Clamp(brightness, 0.5f, 2f);
        if (Mathf.Abs(brightness - applied) < 1e-4f && fill) return;
        applied = brightness;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Sky * brightness; RenderSettings.ambientEquatorColor = Equator * brightness; RenderSettings.ambientGroundColor = Ground * brightness;
        Light sun = RenderSettings.sun; if (!sun) foreach (var l in Object.FindObjectsOfType<Light>()) if (l.type == LightType.Directional && l != fill) { sun = l; break; }
        if (!fill)
        {
            var go = new GameObject("FillLight (ambient)"); fill = go.AddComponent<Light>(); fill.type = LightType.Directional; fill.shadows = LightShadows.None;
            fill.color = new Color(0.80f, 0.85f, 1.0f);   // skylight-tinted
        }
        if (sun) { var d = sun.transform.forward; var h = new Vector3(-d.x, 0f, -d.z); if (h.sqrMagnitude < 1e-4f) h = Vector3.forward; fill.transform.rotation = Quaternion.LookRotation(h.normalized * Mathf.Cos(30f * Mathf.Deg2Rad) + Vector3.down * Mathf.Sin(30f * Mathf.Deg2Rad)); }
        fill.intensity = (sun ? sun.intensity : 1f) * FillFraction * brightness;
        DynamicGI.UpdateEnvironment();
    }
}
