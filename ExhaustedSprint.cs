using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AdrenalineSprint
{
    [BepInPlugin("com.thoughred.exhaustedsprint", "Exhausted Sprint", "1.0.0")]
    public class ExhaustedSprint : BaseUnityPlugin
    {
        internal static ManualLogSource? Log;
        public static bool IsAdrenalineActive = false;

        public static ConfigEntry<bool>? EnableMod;
        public static ConfigEntry<float>? SpeedMultiplier;
        public static ConfigEntry<float>? MaxAdrenalineDuration;
        public static ConfigEntry<float>? CooldownDuration;
        public static ConfigEntry<float>? EffectDurationMultiplier;
        public static ConfigEntry<float>? EffectStartDelay;
        public static ConfigEntry<float>? LegDamagePerStep;
        public static ConfigEntry<float>? HydrationEnergyDrain;
        public static ConfigEntry<bool>? EnableFatigueAndVignette;
        public static ConfigEntry<bool>? EnableTremor;
        public static ConfigEntry<bool>? EnablePain;

        private void Awake()
        {
            Log = Logger;
            EnableMod = Config.Bind("1. General", "Enable Mod", true, "Enables or disables Adrenaline Sprint.");
            SpeedMultiplier = Config.Bind("1. General", "Sprint Speed Multiplier", 0.70f, new ConfigDescription("Speed ratio compared to normal sprint", new AcceptableValueRange<float>(0.40f, 1.0f)));
            MaxAdrenalineDuration = Config.Bind("1. General", "Max Adrenaline Duration (sec)", 7.0f, new ConfigDescription("Max seconds to force-sprint", new AcceptableValueRange<float>(3.0f, 30.0f)));
            CooldownDuration = Config.Bind("1. General", "Cooldown Duration (sec)", 15.0f, new ConfigDescription("Recovery time before using again", new AcceptableValueRange<float>(5.0f, 60.0f)));

            EffectDurationMultiplier = Config.Bind("2. Side Effects", "Effect Duration Multiplier", 1.50f, new ConfigDescription("Side effect duration relative to max sprint duration (1.50 = 1.5x)", new AcceptableValueRange<float>(0.50f, 3.0f)));
            EffectStartDelay = Config.Bind("2. Side Effects", "Effect Start Delay (sec)", 2.0f, new ConfigDescription("Delay before side effects fade in after pressing Shift", new AcceptableValueRange<float>(0.0f, 5.0f)));
            LegDamagePerStep = Config.Bind("2. Side Effects", "Leg Strain Damage", 1.0f, new ConfigDescription("Total silent HP drain split between both legs", new AcceptableValueRange<float>(0.0f, 10.0f)));
            HydrationEnergyDrain = Config.Bind("2. Side Effects", "Hydration & Energy Drain", 1.5f, new ConfigDescription("Hydration and energy lost per interval", new AcceptableValueRange<float>(0.0f, 10.0f)));
            EnableFatigueAndVignette = Config.Bind("2. Side Effects", "Fatigue & Tunnel Vision", true, "Applies Fatigue & screen edge darkening.");
            EnableTremor = Config.Bind("2. Side Effects", "Hand Tremor", true, "Causes hand tremor.");
            EnablePain = Config.Bind("2. Side Effects", "Pain Effect", true, "Applies Pain debuff.");

            var harmony = new Harmony("com.thoughred.adrenalinesprint");
            harmony.PatchAll();
            Log.LogInfo("Exhausted Sprint has been loaded boii");
        }
    }

    [HarmonyPatch(typeof(MovementContext), "CanSprint", MethodType.Getter)]
    public class CanSprintPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (ExhaustedSprint.IsAdrenalineActive) __result = true;
        }
    }

    [HarmonyPatch(typeof(MovementContext), "SprintSpeed", MethodType.Getter)]
    public class SprintSpeedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result)
        {
            if (ExhaustedSprint.IsAdrenalineActive && ExhaustedSprint.SpeedMultiplier != null)
                __result *= ExhaustedSprint.SpeedMultiplier.Value;
        }
    }

    [HarmonyPatch(typeof(Stamina), "NormalValue", MethodType.Getter)]
    public class StaminaBarVisualPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result)
        {
            if (ExhaustedSprint.IsAdrenalineActive) __result = 0f;
        }
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
            if (ExhaustedSprint.EnableMod != null && !ExhaustedSprint.EnableMod.Value) return;
            if (__instance == null || !__instance.IsYourPlayer) return;
            var physical = __instance.Physical;
            if (physical == null || physical.Stamina == null) return;

            if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

            float maxDuration = ExhaustedSprint.MaxAdrenalineDuration?.Value ?? 7.0f;
            float cooldown = ExhaustedSprint.CooldownDuration?.Value ?? 15.0f;
            float effectMult = ExhaustedSprint.EffectDurationMultiplier?.Value ?? 1.50f;
            float startDelay = ExhaustedSprint.EffectStartDelay?.Value ?? 2.0f;

            if (!ExhaustedSprint.IsAdrenalineActive && cooldownTimer <= 0f && physical.Stamina.Current < 15f && Input.GetKeyDown(KeyCode.LeftShift))
            {
                ExhaustedSprint.IsAdrenalineActive = true;
                activeDuration = 0f;
                stepTimer = 0f;

                float calculatedEffectDuration = maxDuration * effectMult;
                TriggerExactHealthEffects(__instance, startDelay, calculatedEffectDuration);
                ApplyStepDrain(__instance);
            }

            if (ExhaustedSprint.IsAdrenalineActive && Input.GetKey(KeyCode.LeftShift) && activeDuration < maxDuration)
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
            else if (ExhaustedSprint.IsAdrenalineActive)
            {
                ExhaustedSprint.IsAdrenalineActive = false;
                stepTimer = 0f;
                activeDuration = 0f;
                cooldownTimer = cooldown;
                physical.Stamina.Current = 0f;
            }
        }

        private static void TriggerExactHealthEffects(Player player, float delay, float duration)
        {
            if (player.HealthController is ActiveHealthController healthController)
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
                var addEffectMethod = typeof(ActiveHealthController).GetMethods(flags)
                    .FirstOrDefault(m => m.Name == "AddEffect" && m.IsGenericMethod);

                if (ExhaustedSprint.EnableFatigueAndVignette != null && ExhaustedSprint.EnableFatigueAndVignette.Value)
                {
                    try { healthController.AddFatigue(); } catch { }
                    if (addEffectMethod != null)
                    {
                        ApplyNestedEffect(addEffectMethod, healthController, "ChronicStaminaFatigue", EBodyPart.Common, delay, duration);
                        ApplyNestedEffect(addEffectMethod, healthController, "TunnelVision", EBodyPart.Common, delay, duration);
                    }
                }

                if (ExhaustedSprint.EnableTremor != null && ExhaustedSprint.EnableTremor.Value && addEffectMethod != null)
                {
                    ApplyNestedEffect(addEffectMethod, healthController, "Tremor", EBodyPart.Common, delay, duration);
                }

                if (ExhaustedSprint.EnablePain != null && ExhaustedSprint.EnablePain.Value && addEffectMethod != null)
                {
                    ApplyNestedEffect(addEffectMethod, healthController, "Pain", EBodyPart.LeftLeg, delay, duration);
                }
            }
        }

        private static void ApplyNestedEffect(MethodInfo genericAddEffect, ActiveHealthController controller, string nestedTypeName, EBodyPart bodyPart, float delay, float duration)
        {
            try
            {
                var nestedType = typeof(ActiveHealthController).GetNestedType(nestedTypeName, BindingFlags.Public | BindingFlags.NonPublic);
                if (nestedType != null)
                {
                    var concreteMethod = genericAddEffect.MakeGenericMethod(nestedType);
                    var parameters = concreteMethod.GetParameters();
                    var args = new object?[parameters.Length];

                    int floatIndex = 0;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var pType = parameters[i].ParameterType;
                        if (pType == typeof(EBodyPart))
                        {
                            args[i] = bodyPart;
                        }
                        else if (pType == typeof(float?) || pType == typeof(float))
                        {
                            if (floatIndex == 0) args[i] = delay;
                            else if (floatIndex == 1) args[i] = duration;
                            else if (floatIndex == 2) args[i] = 3.0f;
                            else if (floatIndex == 3) args[i] = 1.0f;
                            floatIndex++;
                        }
                        else
                        {
                            args[i] = null;
                        }
                    }

                    concreteMethod.Invoke(controller, args);
                }
            }
            catch { }
        }

        private static void ApplyStepDrain(Player player)
        {
            if (player.HealthController is ActiveHealthController healthController)
            {
                float drain = ExhaustedSprint.HydrationEnergyDrain?.Value ?? 1.5f;
                float totalStepDamage = ExhaustedSprint.LegDamagePerStep?.Value ?? 1.0f;

                if (drain > 0f)
                {
                    healthController.ChangeHydration(-drain);
                    healthController.ChangeEnergy(-drain);
                }
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
                    if (lastParam != null)
                        method.Invoke(controller, new object?[] { bodyPart, -amount, Activator.CreateInstance(lastParam.ParameterType) });
                }
                catch { }
            }
        }
    }
}
