using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DagrAndNott
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class CustomDayCyclePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.bigai.dagrandnott";
        public const string PluginName = "DagrAndNott";
        public const string PluginVersion = "1.0.0";

        private static ConfigEntry<float> configDawnMultiplier;
        private static ConfigEntry<float> configDayMultiplier;
        private static ConfigEntry<float> configDuskMultiplier;
        private static ConfigEntry<float> configNightMultiplier;

        private readonly Harmony harmony = new Harmony(PluginGUID);

        private void Awake()
        {
            // Default values tuned for: 5.0m Dawn, 30m Day, 5.0m Dusk, 20m Night (Total 60m cycle)
            configDawnMultiplier = Config.Bind("DayCycle", "DawnMultiplier", 0.90f, "Time speed multiplier during Dawn (0.00 - 0.15). Default 0.90 = ~5.0 mins");
            configDayMultiplier = Config.Bind("DayCycle", "DayMultiplier", 0.50f, "Time speed multiplier during Day (0.15 - 0.65). Default 0.50 = ~30.0 mins");
            configDuskMultiplier = Config.Bind("DayCycle", "DuskMultiplier", 0.90f, "Time speed multiplier during Dusk (0.65 - 0.80). Default 0.90 = ~5.0 mins");
            configNightMultiplier = Config.Bind("DayCycle", "NightMultiplier", 0.30f, "Time speed multiplier during Night (0.80 - 1.00). Default 0.30 = ~20.0 mins");

            harmony.PatchAll();
            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded successfully.");
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.UpdateTime))]
        public static class Patch_EnvMan_UpdateTime
        {
            static void Prefix(EnvMan __instance, [HarmonyArgument(0)] ref float dt)
            {
                // Ensure authority check so clients don't double-apply delta if run locally.
                // In Valheim, the server (or host in singleplayer) is authoritative for global time progression.
                if (ZNet.instance != null && !ZNet.instance.IsServer())
                    return;

                float fraction = __instance.m_smoothDayFraction;
                float multiplier = 1.0f;

                // Adjust multiplier based on current day fraction phase thresholds
                if (fraction < 0.15f) // Dawn: 0.00 - 0.15
                {
                    multiplier = configDawnMultiplier.Value;
                }
                else if (fraction < 0.65f) // Day: 0.15 - 0.65
                {
                    multiplier = configDayMultiplier.Value;
                }
                else if (fraction < 0.80f) // Dusk: 0.65 - 0.80
                {
                    multiplier = configDuskMultiplier.Value;
                }
                else // Night: 0.80 - 1.00
                {
                    multiplier = configNightMultiplier.Value;
                }

                // Apply custom multiplier to delta time (dt)
                dt *= multiplier;
            }
        }
    }
}
