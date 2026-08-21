using System;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DagrAndNott
{
    public enum DayPhase
    {
        Dawn,
        Day,
        Dusk,
        Night
    }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class CustomDayCyclePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.bigai.dagrandnott";
        public const string PluginName = "DagrAndNott";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        private static ConfigEntry<float> configDawnMultiplier;
        private static ConfigEntry<float> configDayMultiplier;
        private static ConfigEntry<float> configDuskMultiplier;
        private static ConfigEntry<float> configNightMultiplier;
        private static ConfigEntry<bool> configLogPhaseTransitions;

        private static DayPhase? lastLoggedPhase = null;

        private readonly Harmony harmony = new Harmony(PluginGUID);

        private void Awake()
        {
            Log = Logger;

            // Default values tuned for: ~5.0m Dawn, ~30m Day, ~5.0m Dusk, ~20m Night (~60m total cycle)
            configDawnMultiplier = Config.Bind("DayCycle", "DawnMultiplier", 0.90f, "Time speed multiplier during Dawn (0.00 - 0.15). Default 0.90 = ~5.0 mins");
            configDayMultiplier = Config.Bind("DayCycle", "DayMultiplier", 0.50f, "Time speed multiplier during Day (0.15 - 0.65). Default 0.50 = ~30.0 mins");
            configDuskMultiplier = Config.Bind("DayCycle", "DuskMultiplier", 0.90f, "Time speed multiplier during Dusk (0.65 - 0.80). Default 0.90 = ~5.0 mins");
            configNightMultiplier = Config.Bind("DayCycle", "NightMultiplier", 0.30f, "Time speed multiplier during Night (0.80 - 1.00). Default 0.30 = ~20.0 mins");

            configLogPhaseTransitions = Config.Bind("Logging", "LogPhaseTransitions", true, "Log when day/night phase transitions occur to the server console.");

            harmony.PatchAll();

            GetEstimatedCycleLengths(out float dawnMins, out float dayMins, out float duskMins, out float nightMins, out float totalMins);
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded successfully.");
            Log.LogInfo($"Cycle Timing: Dawn={configDawnMultiplier.Value:F2}x (~{dawnMins:F1}m), Day={configDayMultiplier.Value:F2}x (~{dayMins:F1}m), Dusk={configDuskMultiplier.Value:F2}x (~{duskMins:F1}m), Night={configNightMultiplier.Value:F2}x (~{nightMins:F1}m) | Estimated Total: ~{totalMins:F1} mins");
        }

        public static DayPhase GetDayPhase(float fraction, out float multiplier, out string phaseName)
        {
            if (fraction < 0.15f) // Dawn: 0.00 - 0.15
            {
                multiplier = configDawnMultiplier.Value;
                phaseName = "Dawn";
                return DayPhase.Dawn;
            }
            else if (fraction < 0.65f) // Day: 0.15 - 0.65
            {
                multiplier = configDayMultiplier.Value;
                phaseName = "Day";
                return DayPhase.Day;
            }
            else if (fraction < 0.80f) // Dusk: 0.65 - 0.80
            {
                multiplier = configDuskMultiplier.Value;
                phaseName = "Dusk";
                return DayPhase.Dusk;
            }
            else // Night: 0.80 - 1.00
            {
                multiplier = configNightMultiplier.Value;
                phaseName = "Night";
                return DayPhase.Night;
            }
        }

        public static void GetEstimatedCycleLengths(out float dawnMins, out float dayMins, out float duskMins, out float nightMins, out float totalMins)
        {
            // Vanilla durations (30 min / 1800 sec total):
            // Dawn: 15% (270s = 4.5m)
            // Day: 50% (900s = 15.0m)
            // Dusk: 15% (270s = 4.5m)
            // Night: 20% (360s = 6.0m)
            float dawnMult = Mathf.Max(0.001f, configDawnMultiplier.Value);
            float dayMult = Mathf.Max(0.001f, configDayMultiplier.Value);
            float duskMult = Mathf.Max(0.001f, configDuskMultiplier.Value);
            float nightMult = Mathf.Max(0.001f, configNightMultiplier.Value);

            dawnMins = 4.5f / dawnMult;
            dayMins = 15.0f / dayMult;
            duskMins = 4.5f / duskMult;
            nightMins = 6.0f / nightMult;
            totalMins = dawnMins + dayMins + duskMins + nightMins;
        }

        public static bool IsAdmin(long senderId)
        {
            // Singleplayer / local host: always admin
            if (ZNet.instance == null || !ZNet.instance.IsServer() || !ZNet.instance.IsDedicated())
                return true;

            // Sender ID 0 is server console / host
            if (senderId == 0)
                return true;

            ZNetPeer peer = ZNet.instance.GetPeer(senderId);
            if (peer == null)
                return false;

            string hostName = peer.m_socket?.GetHostName();
            if (!string.IsNullOrEmpty(hostName) && ZNet.instance.m_adminList != null && ZNet.instance.m_adminList.Contains(hostName))
                return true;

            if (peer.m_rpc?.GetSocket() != null)
            {
                string rpcHost = peer.m_rpc.GetSocket().GetHostName();
                if (!string.IsNullOrEmpty(rpcHost) && ZNet.instance.m_adminList != null && ZNet.instance.m_adminList.Contains(rpcHost))
                    return true;
            }

            return false;
        }

        public static string BuildStatusMessage(EnvMan envMan)
        {
            double totalSeconds = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;
            double dayLength = envMan != null ? (double)envMan.m_dayLengthSec : 1800.0;
            if (dayLength <= 0) dayLength = 1800.0;

            float fraction = (float)((totalSeconds % dayLength) / dayLength);
            if (fraction < 0f) fraction += 1f;

            long dayNumber = envMan != null ? envMan.GetDay(totalSeconds) : (long)(totalSeconds / dayLength);

            GetDayPhase(fraction, out float activeMultiplier, out string phaseName);
            GetEstimatedCycleLengths(out float dawnMins, out float dayMins, out float duskMins, out float nightMins, out float totalMins);

            float progressPercent = fraction * 100f;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<color=#E0B084><b>[DagrAndNott Status]</b></color>");
            sb.AppendLine($"• <b>Current Phase:</b> <color=#98E274>{phaseName}</color> ({activeMultiplier:F2}x speed)");
            sb.AppendLine($"• <b>Day Progress:</b> {progressPercent:F1}% (Day {dayNumber})");
            sb.AppendLine($"• <b>Estimated Cycle:</b> {totalMins:F1} mins (Vanilla: 30.0 mins)");
            sb.AppendLine("• <b>Configured Multipliers:</b>");
            sb.AppendLine($"  Dawn: {configDawnMultiplier.Value:F2}x (~{dawnMins:F1}m) | Day: {configDayMultiplier.Value:F2}x (~{dayMins:F1}m)");
            sb.Append($"  Dusk: {configDuskMultiplier.Value:F2}x (~{duskMins:F1}m) | Night: {configNightMultiplier.Value:F2}x (~{nightMins:F1}m)");

            return sb.ToString();
        }

        public static void SendChatMessage(long targetPeerId, string message)
        {
            // If running on local client or singleplayer/listen-server
            if (Chat.instance != null)
            {
                Chat.instance.AddString(message);
            }

            // If dedicated server sending to a connected client
            if (targetPeerId != 0 && ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(targetPeerId, "ChatMessage", new object[] { Vector3.zero, 1, "<color=#E0B084>DagrAndNott</color>", message });
            }
        }

        public static bool HandleChatCommand(long senderId, string rawText)
        {
            string[] parts = rawText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            string cmd = parts[0].ToLowerInvariant();
            if (cmd != "/dn" && cmd != "/dagrandnott")
                return false;

            // Admin check
            if (!IsAdmin(senderId))
            {
                // Silently ignore non-admins without feedback
                return true; // Suppress chat broadcast
            }

            string subCmd = parts.Length > 1 ? parts[1].ToLowerInvariant() : "status";

            if (subCmd == "status")
            {
                string status = BuildStatusMessage(EnvMan.instance);
                SendChatMessage(senderId, status);

                string senderName = "LocalHost/Console";
                if (ZNet.instance != null && senderId != 0)
                {
                    ZNetPeer peer = ZNet.instance.GetPeer(senderId);
                    if (peer != null)
                        senderName = $"{peer.m_playerName} ({peer.m_socket?.GetHostName() ?? senderId.ToString()})";
                }
                Log.LogInfo($"Admin '{senderName}' requested status via chat.");
            }
            else
            {
                // Help menu for unrecognized subcommands
                string help = "<color=#E0B084><b>[DagrAndNott Commands]</b></color>\n• <b>/dn</b> or <b>/dn status</b> - Display current phase, day progress, and cycle configuration.";
                SendChatMessage(senderId, help);
            }

            return true; // Suppress chat broadcast
        }

        [HarmonyPatch(typeof(EnvMan), "UpdateTime")]
        public static class Patch_EnvMan_UpdateTime
        {
            static void Prefix(EnvMan __instance, [HarmonyArgument(0)] ref float dt)
            {
                // Ensure authority check so clients don't double-apply delta if run locally.
                // In Valheim, the server (or host in singleplayer) is authoritative for global time progression.
                if (ZNet.instance != null && !ZNet.instance.IsServer())
                    return;

                double totalSeconds = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;
                double dayLength = (double)__instance.m_dayLengthSec;
                if (dayLength <= 0) dayLength = 1800.0;

                float fraction = (float)((totalSeconds % dayLength) / dayLength);
                if (fraction < 0f) fraction += 1f;

                DayPhase currentPhase = GetDayPhase(fraction, out float multiplier, out string phaseName);

                // Detect and log phase transitions on the server
                if (lastLoggedPhase == null || lastLoggedPhase.Value != currentPhase)
                {
                    if (lastLoggedPhase != null && configLogPhaseTransitions.Value)
                    {
                        long dayNumber = __instance.GetDay(totalSeconds);
                        Log.LogInfo($"[Phase Transition] {lastLoggedPhase.Value} -> {currentPhase} ({multiplier:F2}x speed) | Day {dayNumber} ({fraction * 100f:F1}%)");
                    }
                    lastLoggedPhase = currentPhase;
                }

                // Apply custom multiplier to delta time (dt)
                dt *= multiplier;
            }
        }

        [HarmonyPatch(typeof(Chat), "RPC_ChatMessage")]
        public static class Patch_Chat_RPC_ChatMessage
        {
            static bool Prefix(long sender, object[] __args)
            {
                if (__args == null || __args.Length == 0)
                    return true;

                // Dynamically scan arguments for the chat string
                for (int i = 0; i < __args.Length; i++)
                {
                    if (__args[i] is string text && !string.IsNullOrEmpty(text))
                    {
                        string trimmed = text.Trim();
                        if (trimmed.StartsWith("/dn", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("/dagrandnott", StringComparison.OrdinalIgnoreCase))
                        {
                            // Intercept and handle command
                            return !HandleChatCommand(sender, trimmed);
                        }
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Chat), "SendText")]
        public static class Patch_Chat_SendText
        {
            static bool Prefix(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return true;

                string trimmed = text.Trim();
                if (trimmed.StartsWith("/dn", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("/dagrandnott", StringComparison.OrdinalIgnoreCase))
                {
                    // Intercept local chat entry (singleplayer / host)
                    return !HandleChatCommand(0, trimmed);
                }

                return true;
            }
        }
    }
}
