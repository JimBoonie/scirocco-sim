// Visual only (no physics): Trilight ambient approximating sky + bounce off the dry-grass/aerial terrain, plus a weak shadowless fill
// light opposite the sun, so shadow sides of buildings aren't near-black. "Ambient brightness" (Quick tab, saved in the profile) scales both.
using UnityEngine;
public static class AmbientLighting
{
    // gradient at brightness 1.0 (perceptual choices, linear-ish colors): sky = clear Monterey sky, equator = haze, ground = sunlit dry grass bounce
    static readonly Color Sky = new Color(0.50f, 0.58f, 0.72f), Equator = new Color(0.46f, 0.45f, 0.40f), Ground = new Color(0.33f, 0.30f, 0.23f);
    const float FillFraction = 0.12f;   // fill intensity = 12 % of the sun x brightness, no shadows
    static float applied = -1f; static Light fill;
    // @description - Sets the tri-light ambient colors (sky, equator, ground) scaled by the brightness and keeps a weak shadowless fill light opposite the sun, so shaded sides are not black. Visual only; skipped when the brightness is unchanged.
    // @param {float} brightness - ambient brightness multiplier, clamped to 0.5..2 (1 = reference)
    // @return {Void} - nothing
    public static void Apply(float brightness)
    {
        brightness = Mathf.Clamp(brightness, 0.5f, 2f);
        if (Mathf.Abs(brightness - applied) < 1e-4f && fill) return;
        applied = brightness;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Sky * brightness; RenderSettings.ambientEquatorColor = Equator * brightness; RenderSettings.ambientGroundColor = Ground * brightness;
        Light sun = RenderSettings.sun; if (!sun) foreach (var candidateLight in Object.FindObjectsOfType<Light>()) if (candidateLight.type == LightType.Directional && candidateLight != fill) { sun = candidateLight; break; }
        if (!fill)
        {
            var fillObject = new GameObject("FillLight (ambient)"); fill = fillObject.AddComponent<Light>(); fill.type = LightType.Directional; fill.shadows = LightShadows.None;
            fill.color = new Color(0.80f, 0.85f, 1.0f);   // skylight-tinted
        }
        if (sun) { var sunDirection = sun.transform.forward; var horizontalDirection = new Vector3(-sunDirection.x, 0f, -sunDirection.z); if (horizontalDirection.sqrMagnitude < 1e-4f) horizontalDirection = Vector3.forward; fill.transform.rotation = Quaternion.LookRotation(horizontalDirection.normalized * Mathf.Cos(30f * Mathf.Deg2Rad) + Vector3.down * Mathf.Sin(30f * Mathf.Deg2Rad)); }
        fill.intensity = (sun ? sun.intensity : 1f) * FillFraction * brightness;
        DynamicGI.UpdateEnvironment();
    }
}
