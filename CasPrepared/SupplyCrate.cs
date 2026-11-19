using CUCoreLib.Data;
using CUCoreLib.Helpers;
using CUCoreLib.Registries;
using HarmonyLib;
using Together;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CasPrepared
{
    /// <summary>
    /// Temporary supply crate: unlimited storage, cannot be picked up, worn or put into other containers.
    /// Breaks after CrateLifetimeSeconds; vanilla Container.ContainerBroke() then drops the contents on the floor.
    /// </summary>
    internal static class SupplyCrate
    {
        public const string ItemId = "casprep_supplycrate";
        private const float HugeCapacity = 1000000f;

        public static void Register()
        {
            var info = new CustomItemInfo
            {
                fullName = "Supply Crate",
                description = "A very heavy crate made of quickly disposable materials. Contains your belongings",
                category = "container",
                slotRotation = 0f,
                usable = false,
                usableOnLimb = false,
                weight = 999f,
                value = 0,
                untradeable = true,
                destroyAtZeroCondition = true,
                // 0 = no natural decay. Lifetime is driven by the host timer in SupplyCrateBehaviour.
                decayMinutes = 0f,
                tags = "noautopickup",
                rec = new Recognition(0),
                SpawnFrequency = 0,
                Container = new ContainerProperties
                {
                    Capacity = HugeCapacity,
                    MaxWeightPerItem = HugeCapacity,
                    EncumbranceReduction = 1f,
                    ItemsVisible = false
                }
            }.AddSpawnComponent<SupplyCrateBehaviour>();

            ItemRegistry.Register(ItemId, info, AssetLoader.LoadEmbeddedSprite("images.supplycrate.png"));
        }

        public static bool IsCrate(Item item)
        {
            return item != null && item.GetComponent<SupplyCrateBehaviour>() != null;
        }
    }

    public sealed class SupplyCrateBehaviour : MonoBehaviour
    {
        private Item _item;
        private float _elapsed;
        private Light2D _glow;

        private void Start()
        {
            _item = GetComponent<Item>();
            // Resume from condition so a save/load keeps the remaining lifetime.
            if (_item != null)
                _elapsed = (1f - Mathf.Clamp01(_item.condition)) * Lifetime;
            // Visual only, so every machine adds its own.
            _glow = CrateGlow.Attach(transform);
        }

        private static float Lifetime => Mathf.Max(1f, ModConfig.CrateLifetimeSeconds.Value);

        private void Update()
        {
            if (_item == null) return;
            CrateGlow.Animate(_glow, _item.condition);
            // Host is authoritative; clients just follow the synced destruction.
            if (Net.IsRunning && !Net.IsServer) return;

            _elapsed += Time.deltaTime;
            // Condition doubles as a visible countdown ("Supply Crate (42%)").
            // At 0 vanilla Item.Update calls ContainerBroke() and destroys the crate.
            _item.condition = Mathf.Clamp01(1f - _elapsed / Lifetime);
        }
    }

    /// <summary>Faint blue point light around the crate. Settings are copied from a light the game already uses
    /// so it lands on the right sorting layers; colour, radius and intensity are ours.</summary>
    internal static class CrateGlow
    {
        private static readonly Color GlowColor = new Color(0.45f, 0.75f, 1f);
        private const float BaseIntensity = 0.55f;
        private const float OuterRadius = 2.6f;
        private const float InnerRadius = 0.25f;

        private static readonly FieldInfo SortingLayersField =
            typeof(Light2D).GetField("m_ApplyToSortingLayers", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Light2D _template;

        public static Light2D Attach(Transform parent)
        {
            try
            {
                var go = new GameObject("CasPrepGlow");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
                var light = go.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Point;

                var template = FindTemplate();
                int[] layers = template != null ? SortingLayersField?.GetValue(template) as int[] : null;
                if (layers == null || layers.Length == 0) layers = SortingLayer.layers.Select(l => l.id).ToArray();
                SortingLayersField?.SetValue(light, layers.ToArray());
                if (template != null) light.blendStyleIndex = template.blendStyleIndex;

                light.color = GlowColor;
                light.intensity = BaseIntensity;
                light.pointLightOuterRadius = OuterRadius;
                light.pointLightInnerRadius = InnerRadius;
                light.falloffIntensity = 0.7f;
                light.shadowsEnabled = false;
                return light;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Supply crate glow unavailable: " + e.Message);
                return null;
            }
        }

        /// <summary>Slow pulse; flickers during the last 15% of the crate's life as a warning.</summary>
        public static void Animate(Light2D light, float condition)
        {
            if (light == null) return;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2f);
            if (condition < 0.15f && Mathf.PerlinNoise(Time.time * 6f, 0.3f) < 0.35f) pulse *= 0.35f;
            light.intensity = BaseIntensity * pulse;
        }

        private static Light2D FindTemplate()
        {
            if (_template != null) return _template;
            foreach (var l in Resources.FindObjectsOfTypeAll<Light2D>())
            {
                if (l == null || l.lightType != Light2D.LightType.Point) continue;
                if (l.gameObject.name == "CasPrepGlow") continue;
                var layers = SortingLayersField?.GetValue(l) as int[];
                if (layers == null || layers.Length == 0) continue;
                _template = l;
                break;
            }
            return _template;
        }
    }

    [HarmonyPatch(typeof(Body), nameof(Body.PickUpItem))]
    internal static class Body_PickUpItem_CratePatch
    {
        private static bool Prefix(Body __instance, Item item)
        {
            if (!SupplyCrate.IsCrate(item)) return true;
            if (PlayerCamera.main != null && PlayerCamera.main.body == __instance)
                PlayerCamera.main.DoAlert("The supply crate is far too heavy to lift.");
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.LoadItem))]
    internal static class Container_LoadItem_CratePatch
    {
        private static bool Prefix(Item item)
        {
            return !SupplyCrate.IsCrate(item);
        }
    }
}
