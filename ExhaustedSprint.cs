using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ExhaustedSprint
{
    [BepInPlugin("com.thoughred.exhaustedsprint", "Exhausted Sprint", "1.0.2")]
    public class ExhaustedSprintPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource? Log;
        public static bool IsExhaustedSprinting = false;
        public static float PostSprintPenaltyTimer = 0f;

        public static ConfigEntry<bool>? EnableMod;
        public static ConfigEntry<float>? SpeedMultiplier;
        public static ConfigEntry<float>? MaxSprintDuration;
        public static ConfigEntry<float>? CooldownDuration;
        public static ConfigEntry<float>? PostSprintFatigueDuration;
        public static ConfigEntry<float>? HydrationEnergyDrain;
        public static ConfigEntry<bool>? EnableFatigue;

        private void Awake()
        {
            Log = Logger;
            EnableMod = Config.Bind("1. General", "Enable Mod", true, "Enables or disables Exhausted Sprint.");
            SpeedMultiplier = Config.Bind("1. General", "Sprint Speed Multiplier", 0.70f, new ConfigDescription("Speed ratio compared to normal sprint", new AcceptableValueRange<float>(0.40f, 1.0f)));
            MaxSprintDuration = Config.Bind("1. General", "Max Exhausted Sprint Duration (sec)", 7.0f, new ConfigDescription("Max seconds to force-sprint after stamina runs out", new AcceptableValueRange<float>(3.0f, 30.0f)));
            CooldownDuration = Config.Bind("1. General", "Cooldown Duration (sec)", 15.0f, new ConfigDescription("Recovery time before forcing a sprint again", new AcceptableValueRange<float>(5.0f, 60.0f)));

            PostSprintFatigueDuration = Config.Bind("2. Side Effects", "Post-Sprint Fatigue Duration (sec)", 10.0f, new ConfigDescription("Duration of heavy fatigue & arm shake after force-sprinting", new AcceptableValueRange<float>(0.0f, 30.0f)));
            HydrationEnergyDrain = Config.Bind("2. Side Effects", "Hydration & Energy Drain", 2.5f, new ConfigDescription("Hydration and energy lost per interval", new AcceptableValueRange<float>(0.0f, 10.0f)));
            EnableFatigue = Config.Bind("2. Side Effects", "Apply Fatigue Debuff", true, "Applies EFT Fatigue & tunnel vision when pushing past stamina.");

            var harmony = new Harmony("com.thoughred.exhaustedsprint");
            harmony.PatchAll();
            Log.LogInfo("Exhausted Sprint v1.0.2 by thoughred loaded!");
        }
    }

    [HarmonyPatch(typeof(MovementContext), "CanSprint", MethodType.Getter)]
    public class CanSprintPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (ExhaustedSprintPlugin.IsExhaustedSprinting) __result = true;
        }
    }

    [HarmonyPatch(typeof(MovementContext), "CanJump", MethodType.Getter)]
    public class CanJumpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (ExhaustedSprintPlugin.IsExhaustedSprinting) __result = false;
        }
    }

    [HarmonyPatch(typeof(MovementContext), "SprintSpeed", MethodType.Getter)]
    public class SprintSpeedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result)
        {
            if (ExhaustedSprintPlugin.IsExhaustedSprinting && ExhaustedSprintPlugin.SpeedMultiplier != null)
                __result *= ExhaustedSprintPlugin.SpeedMultiplier.Value;
        }
    }

    [HarmonyPatch(typeof(Stamina), "NormalValue", MethodType.Getter)]
    public class StaminaBarVisualPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result)
        {
            if (ExhaustedSprintPlugin.IsExhaustedSprinting) __result = 0f;
        }
    }

    [HarmonyPatch(typeof(Player), "LateUpdate")]
    public class ExhaustedSprintPatch
    {
        private static float stepTimer = 0f;
        private static float activeDuration = 0f;
        private static float cooldownTimer = 0f;

        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            if (ExhaustedSprintPlugin.EnableMod != null && !ExhaustedSprintPlugin.EnableMod.Value) return;
            if (__instance == null || !__instance.IsYourPlayer) return;
            var physical = __instance.Physical;
            if (physical == null || physical.Stamina == null) return;

            if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

            if (ExhaustedSprintPlugin.PostSprintPenaltyTimer > 0f)
            {
                ExhaustedSprintPlugin.PostSprintPenaltyTimer -= Time.deltaTime;
                if (physical.HandsStamina != null && physical.HandsStamina.Current > 5f)
                    physical.HandsStamina.Current = 5f;
                if (physical.Stamina.Current > 12f)
                    physical.Stamina.Current = 12f;
            }

            float maxDuration = ExhaustedSprintPlugin.MaxSprintDuration?.Value ?? 7.0f;
            float cooldown = ExhaustedSprintPlugin.CooldownDuration?.Value ?? 15.0f;
            float fatigueDuration = ExhaustedSprintPlugin.PostSprintFatigueDuration?.Value ?? 10.0f;

            if (!ExhaustedSprintPlugin.IsExhaustedSprinting && cooldownTimer <= 0f && physical.Stamina.Current < 15f && Input.GetKeyDown(KeyCode.LeftShift))
            {
                ExhaustedSprintPlugin.IsExhaustedSprinting = true;
                activeDuration = 0f;
                stepTimer = 0f;
                TriggerFatigue(__instance, fatigueDuration);
                ApplyStepDrain(__instance);
            }

            if (ExhaustedSprintPlugin.IsExhaustedSprinting && Input.GetKey(KeyCode.LeftShift) && activeDuration < maxDuration)
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
            else if (ExhaustedSprintPlugin.IsExhaustedSprinting)
            {
                ExhaustedSprintPlugin.IsExhaustedSprinting = false;
                stepTimer = 0f;
                activeDuration = 0f;
                cooldownTimer = cooldown;
                ExhaustedSprintPlugin.PostSprintPenaltyTimer = fatigueDuration;
                physical.Stamina.Current = 0f;
                if (physical.HandsStamina != null) physical.HandsStamina.Current = 0f;
            }
        }

        private static void TriggerFatigue(Player player, float duration)
        {
            if (ExhaustedSprintPlugin.EnableFatigue != null && !ExhaustedSprintPlugin.EnableFatigue.Value) return;
            if (player.HealthController is ActiveHealthController healthController)
            {
                try { healthController.AddFatigue(); } catch { }

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
                var addEffectMethod = typeof(ActiveHealthController).GetMethods(flags)
                    .FirstOrDefault(m => m.Name == "AddEffect" && m.IsGenericMethod);

                if (addEffectMethod != null)
                {
                    try
                    {
                        var nestedType = typeof(ActiveHealthController).GetNestedType("TunnelVision", BindingFlags.Public | BindingFlags.NonPublic);
                        if (nestedType != null)
                        {
                            var concreteMethod = addEffectMethod.MakeGenericMethod(nestedType);
                            var parameters = concreteMethod.GetParameters();
                            var args = new object?[parameters.Length];

                            int floatIndex = 0;
                            for (int i = 0; i < parameters.Length; i++)
                            {
                                var pType = parameters[i].ParameterType;
                                if (pType == typeof(EBodyPart)) args[i] = EBodyPart.Common;
                                else if (pType == typeof(float?) || pType == typeof(float))
                                {
                                    if (floatIndex == 0) args[i] = 1.5f;
                                    else if (floatIndex == 1) args[i] = duration;
                                    else if (floatIndex == 2) args[i] = 2.0f;
                                    else if (floatIndex == 3) args[i] = 1.0f;
                                    floatIndex++;
                                }
                                else args[i] = null;
                            }
                            concreteMethod.Invoke(healthController, args);
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
                float drain = ExhaustedSprintPlugin.HydrationEnergyDrain?.Value ?? 2.5f;
                if (drain > 0f)
                {
                    healthController.ChangeHydration(-drain);
                    healthController.ChangeEnergy(-drain);
                }
            }
        }
    }
}
