using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using System;
using System.Linq;
using UnityEngine;

namespace AdrenalineSprint
{
    [BepInPlugin("com.flomods.adrenalinesprint", "Adrenaline Sprint", "2.8.0")]
    public class AdrenalinePlugin : BaseUnityPlugin
    {
        internal static ManualLogSource? Log;
        public static bool IsAdrenalineActive = false;

        public static ConfigEntry<bool>? EnableMod;
        public static ConfigEntry<float>? SpeedMultiplier;
        public static ConfigEntry<float>? MaxAdrenalineDuration;
        public static ConfigEntry<float>? CooldownDuration;
        public static ConfigEntry<float>? LegDamagePerStep;
        public static ConfigEntry<float>? HydrationEnergyDrain;
        public static ConfigEntry<bool>? EnableTremor;
        public static ConfigEntry<bool>? EnableTunnelVision;
        public static ConfigEntry<bool>? EnablePain;
        public static ConfigEntry<float>? ContusionIntensity;

        private void Awake()
        {
            Log = Logger;
            EnableMod = Config.Bind("1. General", "Enable Mod", true, "Enables or disables Adrenaline Sprint.");
            SpeedMultiplier = Config.Bind("1. General", "Sprint Speed Multiplier", 0.70f, new ConfigDescription("Speed ratio compared to normal sprint", new AcceptableValueRange<float>(0.40f, 1.0f)));
            MaxAdrenalineDuration = Config.Bind("1. General", "Max Adrenaline Duration (sec)", 7.0f, new ConfigDescription("Max seconds to force-sprint", new AcceptableValueRange<float>(3.0f, 30.0f)));
            CooldownDuration = Config.Bind("1. General", "Cooldown Duration (sec)", 15.0f, new ConfigDescription("Recovery time before using again", new AcceptableValueRange<float>(5.0f, 60.0f)));

            LegDamagePerStep = Config.Bind("2. Side Effects", "Leg Strain Damage", 1.0f, new ConfigDescription("Total silent HP drain split between both legs", new AcceptableValueRange<float>(0.0f, 10.0f)));
            HydrationEnergyDrain = Config.Bind("2. Side Effects", "Hydration & Energy Drain", 1.5f, new ConfigDescription("Hydration and energy lost per interval", new AcceptableValueRange<float>(0.0f, 10.0f)));
            EnableTremor = Config.Bind("2. Side Effects", "Hand Tremor", true, "Causes hand tremor for 30s.");
            EnableTunnelVision = Config.Bind("2. Side Effects", "Tunnel Vision Effect", true, "Darkens screen edges (vignette) for 30s.");
            EnablePain = Config.Bind("2. Side Effects", "Pain Effect", true, "Applies Pain debuff for 30s.");
            ContusionIntensity = Config.Bind("2. Side Effects", "Dizziness / Blur Intensity", 0.25f, new ConfigDescription("0 = Off, 0.25 = Subtle, 1.0 = Heavy Grenade Blur", new AcceptableValueRange<float>(0.0f, 1.0f)));

            var harmony = new Harmony("com.flomods.adrenalinesprint");
            harmony.PatchAll();
            Log.LogInfo("Adrenaline Sprint v2.8.0 by flomods loaded!");
        }
    }

    [HarmonyPatch(typeof(MovementContext), "CanSprint", MethodType.Getter)]
    public class CanSprintPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result) { if (AdrenalinePlugin.IsAdrenalineActive) __result = true; }
    }

    [HarmonyPatch(typeof(MovementContext), "SprintSpeed", MethodType.Getter)]
    public class SprintSpeedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result) { if (AdrenalinePlugin.IsAdrenalineActive && AdrenalinePlugin.SpeedMultiplier != null) __result *= AdrenalinePlugin.SpeedMultiplier.Value; }
    }

    [HarmonyPatch(typeof(Stamina), "NormalValue", MethodType.Getter)]
    public class StaminaBarVisualPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result) { if (AdrenalinePlugin.IsAdrenalineActive) __result = 0f; }
    }
    [HarmonyPatch(typeof(Player), "LateUpdate")]
    public class AdrenalineSprintPatch
    {
        private static float stepTimer = 0f;
        private static float activeDuration = 0f;
        private static float cooldownTimer = 0f;

        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            if (AdrenalinePlugin.EnableMod != null && !AdrenalinePlugin.EnableMod.Value) return;
            if (__instance == null || !__instance.IsYourPlayer) return;
            var physical = __instance.Physical;
            if (physical == null || physical.Stamina == null) return;

            if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

            float maxDuration = AdrenalinePlugin.MaxAdrenalineDuration?.Value ?? 7.0f;
            float cooldown = AdrenalinePlugin.CooldownDuration?.Value ?? 15.0f;

            if (!AdrenalinePlugin.IsAdrenalineActive && cooldownTimer <= 0f && physical.Stamina.Current < 15f && Input.GetKeyDown(KeyCode.LeftShift))
            {
                AdrenalinePlugin.IsAdrenalineActive = true;
                activeDuration = 0f;
                stepTimer = 0f;

                ApplyInitialVisualEffects(__instance, 30.0f);
                ApplyStepDrain(__instance);
            }

            if (AdrenalinePlugin.IsAdrenalineActive && Input.GetKey(KeyCode.LeftShift) && activeDuration < maxDuration)
            {
                activeDuration += Time.deltaTime;
                if (physical.Stamina.Current < 25f) physical.Stamina.Current = 25f;
                __instance.MovementContext?.EnableSprint(true);

                stepTimer += Time.deltaTime;
                if (stepTimer >= 0.65f)
                {
                    stepTimer = 0f;
                    ApplyStepDrain(__instance);
                }
            }
            else if (AdrenalinePlugin.IsAdrenalineActive)
            {
                AdrenalinePlugin.IsAdrenalineActive = false;
                stepTimer = 0f;
                activeDuration = 0f;
                cooldownTimer = cooldown;
                physical.Stamina.Current = 0f;
            }
        }

        private static void ApplyInitialVisualEffects(Player player, float effectDuration)
        {
            if (player.HealthController is ActiveHealthController healthController)
            {
                // 1. Real Screen Vignette / Tunnel Vision via Camera EffectsController
                if (AdrenalinePlugin.EnableTunnelVision != null && AdrenalinePlugin.EnableTunnelVision.Value)
                {
                    try
                    {
                        var cam = Camera.main;
                        if (cam != null)
                        {
                            var effectsCtrl = cam.GetComponent("EffectsController");
                            if (effectsCtrl != null)
                            {
                                var addEffectMethod = effectsCtrl.GetType().GetMethods()
                                    .FirstOrDefault(m => m.Name == "AddEffect" || m.Name.Contains("Tunnel"));
                                // Trigger dehydration/exhaustion vignette state on HealthController if available
                            }
                        }

                        // Trigger EFT's built-in Stamina/Exhaustion tunnel vision via DoDisorientation or AddEffect
                        var effectMethods = typeof(ActiveHealthController).GetMethods()
                            .Where(m => m.Name.StartsWith("Do") || m.Name == "AddEffect");

                        foreach (var m in effectMethods)
                        {
                            if (m.Name.Contains("Tunnel") || m.Name.Contains("Disorientation") || m.Name.Contains("Intoxication"))
                            {
                                var p = m.GetParameters();
                                if (p.Length == 1 && p[0].ParameterType == typeof(float))
                                {
                                    m.Invoke(healthController, new object[] { effectDuration });
                                    break;
                                }
                                else if (p.Length == 2 && p[0].ParameterType == typeof(float) && p[1].ParameterType == typeof(float))
                                {
                                    m.Invoke(healthController, new object[] { 0.6f, effectDuration });
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                // 2. Adjustable Subtle Contusion / Blur (Default 0.25f instead of heavy 1.0f)
                float contusion = AdrenalinePlugin.ContusionIntensity?.Value ?? 0.25f;
                if (contusion > 0f)
                {
                    try { healthController.DoContusion(effectDuration, contusion); } catch { }
                }

                // 3. Hand Tremor (30s)
                if (AdrenalinePlugin.EnableTremor != null && AdrenalinePlugin.EnableTremor.Value)
                {
                    try
                    {
                        var tremorMethod = typeof(ActiveHealthController).GetMethods().FirstOrDefault(m => m.Name == "DoTremor");
                        if (tremorMethod != null)
                        {
                            var p = tremorMethod.GetParameters();
                            if (p.Length == 1) tremorMethod.Invoke(healthController, new object[] { effectDuration });
                            else if (p.Length == 2) tremorMethod.Invoke(healthController, new object[] { 1.0f, effectDuration });
                        }
                    }
                    catch { }
                }

                // 4. Pain Debuff (30s)
                if (AdrenalinePlugin.EnablePain != null && AdrenalinePlugin.EnablePain.Value)
                {
                    try
                    {
                        var painMethod = typeof(ActiveHealthController).GetMethods().FirstOrDefault(m => m.Name == "DoPain");
                        if (painMethod != null)
                        {
                            var p = painMethod.GetParameters();
                            if (p.Length == 1) painMethod.Invoke(healthController, new object[] { effectDuration });
                            else if (p.Length == 2) painMethod.Invoke(healthController, new object[] { 1.0f, effectDuration });
                        }
                    }
                    catch { }
                }
            }
        }

        private static void ApplyStepDrain(Player player)
        {
            if (player.HealthController is ActiveHealthController healthController)
            {
                float drain = AdrenalinePlugin.HydrationEnergyDrain?.Value ?? 1.5f;
                float totalStepDamage = AdrenalinePlugin.LegDamagePerStep?.Value ?? 1.0f;

                if (drain > 0f) { healthController.ChangeHydration(-drain); healthController.ChangeEnergy(-drain); }
                if (totalStepDamage > 0f)
                {
                    float half = totalStepDamage / 2f;
                    ApplySilentLegDrain(healthController, EBodyPart.LeftLeg, half);
                    ApplySilentLegDrain(healthController, EBodyPart.RightLeg, half);
                }
            }
        }

        private static void ApplySilentLegDrain(ActiveHealthController controller, EBodyPart bodyPart, float amount)
        {
            var method = typeof(ActiveHealthController).GetMethods().FirstOrDefault(m => m.Name == "ChangeHealth" && m.GetParameters().Length == 3);
            if (method != null)
            {
                try
                {
                    var lastParam = method.GetParameters().LastOrDefault();
                    if (lastParam != null) method.Invoke(controller, new object?[] { bodyPart, -amount, Activator.CreateInstance(lastParam.ParameterType) });
                }
                catch { }
            }
        }
    }
}
