using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Light2D))]
public class TimeOfDayLightingController : MonoBehaviour
{
    [Serializable]
    public struct LightingKeyframe
    {
        public LightingKeyframe(float hour, Color color, float intensity)
        {
            this.hour = hour;
            this.color = color;
            this.intensity = intensity;
        }

        [Range(0f, 24f)] public float hour;
        public Color color;
        [Min(0f)] public float intensity;
    }

    [Header("References")]
    [SerializeField] private WorldInfoSystem worldInfoSystem;
    [SerializeField] private Light2D globalLight;

    [Header("Lighting Timeline")]
    [SerializeField] private LightingKeyframe[] lightingKeyframes = CreateDefaultKeyframes();

    [Header("Natural Time Transition")]
    [SerializeField, Min(0f), Tooltip("Velocidade da suavizacao entre passos naturais do relogio.")]
    private float smoothingSpeed = 3f;

    private Color targetColor = Color.white;
    private float targetIntensity = 1f;

    public Light2D GlobalLight => globalLight;

    private void Reset()
    {
        ResolveReferences();
        lightingKeyframes = CreateDefaultKeyframes();
        ConfigureGlobalLight();
        RefreshTarget(applyImmediately: true);
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureValidKeyframes();
        ConfigureGlobalLight();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureValidKeyframes();
        ConfigureGlobalLight();

        if (worldInfoSystem != null)
            worldInfoSystem.TimeChanged += HandleTimeChanged;

        RefreshTarget(applyImmediately: true);
    }

    private void OnDisable()
    {
        if (worldInfoSystem != null)
            worldInfoSystem.TimeChanged -= HandleTimeChanged;
    }

    private void OnValidate()
    {
        ResolveReferences();
        EnsureValidKeyframes();
        ConfigureGlobalLight();
        RefreshTarget(applyImmediately: true);
    }

    private void Update()
    {
        if (!Application.isPlaying || globalLight == null || smoothingSpeed <= 0f)
            return;

        float blend = 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);
        globalLight.color = Color.Lerp(globalLight.color, targetColor, blend);
        globalLight.intensity = Mathf.Lerp(globalLight.intensity, targetIntensity, blend);
    }

    private void HandleTimeChanged(WorldInfoSystem.TimeChange change)
    {
        // Consulta novamente o relogio autoritativo. Uma transicao de dia pode
        // ocorrer durante a notificacao que detectou 02:00.
        RefreshTarget(change.IsJump);
    }

    private void RefreshTarget(bool applyImmediately)
    {
        if (worldInfoSystem == null || globalLight == null || lightingKeyframes == null ||
            lightingKeyframes.Length < 2)
        {
            return;
        }

        float hour = worldInfoSystem.CurrentHour24 + worldInfoSystem.CurrentMinute / 60f;
        EvaluateLighting(hour, out targetColor, out targetIntensity);

        if (applyImmediately || !Application.isPlaying || smoothingSpeed <= 0f)
        {
            globalLight.color = targetColor;
            globalLight.intensity = targetIntensity;
        }
    }

    private void EvaluateLighting(float hour, out Color color, out float intensity)
    {
        float sampleHour = Mathf.Repeat(hour, 24f);
        int previousIndex;
        int nextIndex;
        float previousHour;
        float nextHour;

        int firstLaterKey = -1;
        for (int i = 0; i < lightingKeyframes.Length; i++)
        {
            if (lightingKeyframes[i].hour > sampleHour)
            {
                firstLaterKey = i;
                break;
            }
        }

        if (firstLaterKey < 0)
        {
            previousIndex = lightingKeyframes.Length - 1;
            nextIndex = 0;
            previousHour = lightingKeyframes[previousIndex].hour;
            nextHour = lightingKeyframes[nextIndex].hour + 24f;
            sampleHour += 24f;
        }
        else
        {
            nextIndex = firstLaterKey;
            previousIndex = nextIndex == 0 ? lightingKeyframes.Length - 1 : nextIndex - 1;
            previousHour = lightingKeyframes[previousIndex].hour;
            nextHour = lightingKeyframes[nextIndex].hour;

            if (previousIndex == lightingKeyframes.Length - 1)
                previousHour -= 24f;
        }

        float duration = nextHour - previousHour;
        float interpolation = duration > 0.0001f
            ? Mathf.Clamp01((sampleHour - previousHour) / duration)
            : 0f;

        LightingKeyframe previous = lightingKeyframes[previousIndex];
        LightingKeyframe next = lightingKeyframes[nextIndex];
        color = Color.Lerp(previous.color, next.color, interpolation);
        intensity = Mathf.Lerp(previous.intensity, next.intensity, interpolation);
    }

    private void EnsureValidKeyframes()
    {
        if (lightingKeyframes == null || lightingKeyframes.Length < 2)
            lightingKeyframes = CreateDefaultKeyframes();

        for (int i = 0; i < lightingKeyframes.Length; i++)
        {
            LightingKeyframe keyframe = lightingKeyframes[i];
            keyframe.hour = Mathf.Clamp(keyframe.hour, 0f, 24f);
            keyframe.intensity = Mathf.Max(0f, keyframe.intensity);
            lightingKeyframes[i] = keyframe;
        }

        Array.Sort(lightingKeyframes, (left, right) => left.hour.CompareTo(right.hour));
    }

    private void ConfigureGlobalLight()
    {
        if (globalLight == null)
            return;

        globalLight.lightType = Light2D.LightType.Global;
        globalLight.blendStyleIndex = 0;

        SortingLayer[] sortingLayers = SortingLayer.layers;
        int[] currentLayers = globalLight.targetSortingLayers;
        bool needsUpdate = currentLayers == null || currentLayers.Length != sortingLayers.Length;

        if (!needsUpdate)
        {
            for (int i = 0; i < sortingLayers.Length; i++)
            {
                if (currentLayers[i] != sortingLayers[i].id)
                {
                    needsUpdate = true;
                    break;
                }
            }
        }

        if (!needsUpdate)
            return;

        int[] allSortingLayerIds = new int[sortingLayers.Length];
        for (int i = 0; i < sortingLayers.Length; i++)
            allSortingLayerIds[i] = sortingLayers[i].id;

        globalLight.targetSortingLayers = allSortingLayerIds;
    }

    private void ResolveReferences()
    {
        if (worldInfoSystem == null)
            worldInfoSystem = GetComponent<WorldInfoSystem>();

        if (globalLight == null)
            globalLight = GetComponent<Light2D>();
    }

    private static LightingKeyframe[] CreateDefaultKeyframes()
    {
        return new[]
        {
            new LightingKeyframe(0f, new Color(0.18f, 0.25f, 0.48f, 1f), 0.42f),
            new LightingKeyframe(2f, new Color(0.12f, 0.17f, 0.34f, 1f), 0.34f),
            new LightingKeyframe(6f, new Color(0.62f, 0.72f, 0.90f, 1f), 0.68f),
            new LightingKeyframe(8f, new Color(0.90f, 0.94f, 1.00f, 1f), 0.88f),
            new LightingKeyframe(11f, new Color(1.00f, 1.00f, 0.98f, 1f), 1.00f),
            new LightingKeyframe(15f, new Color(1.00f, 0.95f, 0.82f, 1f), 0.98f),
            new LightingKeyframe(17f, new Color(1.00f, 0.70f, 0.35f, 1f), 0.82f),
            new LightingKeyframe(19f, new Color(0.72f, 0.42f, 0.32f, 1f), 0.62f),
            new LightingKeyframe(21f, new Color(0.32f, 0.42f, 0.68f, 1f), 0.48f)
        };
    }
}
