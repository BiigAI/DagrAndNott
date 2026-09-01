using System;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Utils;
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
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class CustomDayCyclePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.bigai.dagrnott_customdaycycle";
        public const string PluginName = "DagrNott_CustomDayCycle";
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
            configDawnMultiplier = Config.Bind("DayCycle", "DawnMultiplier", 0.90f, new ConfigDescription("Visual time speed multiplier during Dawn.", new AcceptableValueRange<float>(0.05f, 4f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configDayMultiplier = Config.Bind("DayCycle", "DayMultiplier", 0.50f, new ConfigDescription("Visual time speed multiplier during Day.", new AcceptableValueRange<float>(0.05f, 4f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configDuskMultiplier = Config.Bind("DayCycle", "DuskMultiplier", 0.90f, new ConfigDescription("Visual time speed multiplier during Dusk.", new AcceptableValueRange<float>(0.05f, 4f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configNightMultiplier = Config.Bind("DayCycle", "NightMultiplier", 0.30f, new ConfigDescription("Visual time speed multiplier during Night.", new AcceptableValueRange<float>(0.05f, 4f), new ConfigurationManagerAttributes { IsAdminOnly = true }));

            configLogPhaseTransitions = Config.Bind("Logging", "LogPhaseTransitions", true, "Log when day/night phase transitions occur to the server console.");

            // Apply each patch individually so one failure doesn't break others
            ApplyPatch(harmony, typeof(Patch_Game_Start));
            ApplyPatch(harmony, typeof(Patch_Terminal_TryRunCommand));
            ApplyPatch(harmony, typeof(Patch_EnvMan_FixedUpdate));
            Log.LogInfo($"[{PluginName}] Harmony patching complete.");

            GetEstimatedCycleLengths(out float dawnMins, out float dayMins, out float duskMins, out float nightMins, out float totalMins);
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded successfully.");
            Log.LogInfo($"Cycle Timing: Dawn={configDawnMultiplier.Value:F2}x (~{dawnMins:F1}m), Day={configDayMultiplier.Value:F2}x (~{dayMins:F1}m), Dusk={configDuskMultiplier.Value:F2}x (~{duskMins:F1}m), Night={configNightMultiplier.Value:F2}x (~{nightMins:F1}m) | Estimated Total: ~{totalMins:F1} mins");
            Log.LogInfo("[DagrAndNott] Jotunn synchronizes cycle settings; all clients require DagrAndNott. Network time and water simulation are unchanged.");
        }

        /// <summary>
        /// Applies a single Harmony patch class with isolated error handling.
        /// If the patch fails, it logs an error but does NOT prevent other patches from being applied.
        /// </summary>
        private static void ApplyPatch(Harmony harmony, Type patchType)
        {
            try
            {
                harmony.CreateClassProcessor(patchType).Patch();
                Log.LogInfo($"[{PluginName}] Patched: {patchType.Name}");
            }
            catch (Exception ex)
            {
                Log.LogError($"[{PluginName}] Failed to apply patch {patchType.Name}: {ex.Message}");
            }
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

        private static readonly System.Reflection.FieldInfo FiAdminList = typeof(ZNet).GetField(
            "m_adminList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

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
            if (string.IsNullOrEmpty(hostName) && peer.m_rpc?.GetSocket() != null)
            {
                hostName = peer.m_rpc.GetSocket().GetHostName();
            }

            if (string.IsNullOrEmpty(hostName))
                return false;

            try
            {
                object adminListObj = FiAdminList != null
                    ? FiAdminList.GetValue(ZNet.instance)
                    : Traverse.Create(ZNet.instance).Field("m_adminList").GetValue();

                if (adminListObj == null)
                    return false;

                if (adminListObj is SyncedList syncedList)
                {
                    if (syncedList.Contains(hostName))
                        return true;
                    if (ulong.TryParse(hostName, out _) && syncedList.Contains("Steam_" + hostName))
                        return true;
                    return false;
                }

                var containsMethod = adminListObj.GetType().GetMethod("Contains", new[] { typeof(string) });
                if (containsMethod != null)
                {
                    if ((bool)(containsMethod.Invoke(adminListObj, new object[] { hostName }) ?? false))
                        return true;
                    if (ulong.TryParse(hostName, out _) && (bool)(containsMethod.Invoke(adminListObj, new object[] { "Steam_" + hostName }) ?? false))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log?.LogError($"[DagrAndNott] IsAdmin check error: {ex.Message}");
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

        public const string RpcCommand = "DagrAndNott_Cmd";
        public const string RpcResponse = "DagrAndNott_Resp";
        private static bool _rpcsRegistered;

        public static void RegisterRPCs()
        {
            if (_rpcsRegistered || ZRoutedRpc.instance == null) return;
            _rpcsRegistered = true;

            try
            {
                ZRoutedRpc.instance.Register<string>(RpcCommand, RPC_AdminCommand);
            }
            catch (ArgumentException) { }

            try
            {
                ZRoutedRpc.instance.Register<string>(RpcResponse, RPC_AdminResponse);
            }
            catch (ArgumentException) { }

            Log?.LogInfo("[DagrAndNott] Network RPC handlers registered.");
        }

        private static void RPC_AdminCommand(long sender, string text)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            string senderName = peer != null ? $"{peer.m_playerName} ({peer.m_socket?.GetHostName() ?? sender.ToString()})" : sender.ToString();

            if (!IsAdmin(sender))
            {
                Log?.LogWarning($"[DagrAndNott] Non-admin {senderName} tried to run: {text}");
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcResponse, "<color=#FF4444>[DagrAndNott] Access denied: You are not listed in the server's adminlist.txt.</color>");
                return;
            }

            Log?.LogInfo($"[DagrAndNott] Executing command '{text}' for admin {senderName}");
            string response = ExecuteCommand(text);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcResponse, response);
        }

        private static void RPC_AdminResponse(long sender, string response)
        {
            DisplayResponse(response);
        }

        public static void DisplayResponse(string response)
        {
            if (string.IsNullOrEmpty(response)) return;

            // 1. Always log to BepInEx console and LogOutput.log
            Log?.LogInfo($"\n{response}");

            // 2. Print into Chat & Console terminal output buffer
            if (Chat.instance != null)
            {
                Chat.instance.AddString(response);
            }

            // 3. Display on-screen HUD message for immediate visual confirmation
            if (Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "<color=#E0B084>[DagrAndNott]</color> Status output sent to chat & log.");
            }
            else if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "<color=#E0B084>[DagrAndNott]</color> Status output sent to chat & log.");
            }
        }

        public static void SendAdminCommand(string text)
        {
            Log?.LogInfo($"[DagrAndNott] Handling command: '{text}'");

            if (string.IsNullOrWhiteSpace(text)) return;

            if (!text.StartsWith("/", StringComparison.OrdinalIgnoreCase))
                text = "/" + text;

            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                // Singleplayer, listen server host, or local test -> execute immediately
                string response = ExecuteCommand(text);
                DisplayResponse(response);
                return;
            }

            // Dedicated server client -> route command to server
            if (ZRoutedRpc.instance != null)
            {
                Log?.LogInfo($"[DagrAndNott] Sending admin command to server: {text}");
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcCommand, text);
            }
            else
            {
                // Fallback if network RPCs not initialized
                string response = ExecuteCommand(text);
                DisplayResponse(response);
            }
        }

        public static string ExecuteCommand(string text)
        {
            string body = text;
            if (body.StartsWith("/dagrandnott", StringComparison.OrdinalIgnoreCase))
                body = body.Substring(12).Trim();
            else if (body.StartsWith("dagrandnott", StringComparison.OrdinalIgnoreCase))
                body = body.Substring(11).Trim();
            else if (body.StartsWith("/dn", StringComparison.OrdinalIgnoreCase))
                body = body.Substring(3).Trim();
            else if (body.StartsWith("dn", StringComparison.OrdinalIgnoreCase))
                body = body.Substring(2).Trim();

            string[] tokens = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string subCmd = tokens.Length > 0 ? tokens[0].ToLowerInvariant() : "status";

            if (subCmd == "status")
            {
                return BuildStatusMessage(EnvMan.instance);
            }
            else
            {
                return "<color=#E0B084><b>[DagrAndNott Commands]</b></color>\n• <b>/dn</b> or <b>/dn status</b> - Display current phase, day progress, and cycle configuration.";
            }
        }

        // Register RPCs after Game.Start so ZRoutedRpc.instance is ready
        [HarmonyPatch(typeof(Game), "Start")]
        public static class Patch_Game_Start
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                RegisterRPCs();
            }
        }

        [HarmonyPatch(typeof(Terminal), "TryRunCommand")]
        public static class Patch_Terminal_TryRunCommand
        {
            [HarmonyPrefix]
            public static bool Prefix(Terminal __instance, string text)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(text))
                        return true;

                    string trimmed = text.Trim();
                    if (trimmed.StartsWith("dn ", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Equals("dn", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("/dn ", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Equals("/dn", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("dagrandnott ", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Equals("dagrandnott", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("/dagrandnott ", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Equals("/dagrandnott", StringComparison.OrdinalIgnoreCase))
                    {
                        SendAdminCommand(trimmed);
                        return false; // Handled, suppress unknown command warning / default terminal handling
                    }
                }
                catch (Exception ex)
                {
                    Log?.LogError($"[DagrAndNott] Error in TryRunCommand prefix: {ex}");
                }

                return true;
            }
        }

        public static double GetVisualTimeSeconds()
        {
            if (ZNet.instance == null || EnvMan.instance == null)
                return 0d;

            double dayLength = EnvMan.instance.m_dayLengthSec;
            if (dayLength <= 0d)
                return ZNet.instance.GetTimeSeconds();

            double realTime = ZNet.instance.GetTimeSeconds();
            double dawnDuration = dayLength * 0.15d / Mathf.Max(0.05f, configDawnMultiplier.Value);
            double dayDuration = dayLength * 0.50d / Mathf.Max(0.05f, configDayMultiplier.Value);
            double duskDuration = dayLength * 0.15d / Mathf.Max(0.05f, configDuskMultiplier.Value);
            double nightDuration = dayLength * 0.20d / Mathf.Max(0.05f, configNightMultiplier.Value);
            double realCycleDuration = dawnDuration + dayDuration + duskDuration + nightDuration;
            double cycleOffset = realTime % realCycleDuration;
            long cycle = (long)Math.Floor(realTime / realCycleDuration);

            double visualOffset;
            if (cycleOffset < dawnDuration)
                visualOffset = cycleOffset * configDawnMultiplier.Value;
            else if ((cycleOffset -= dawnDuration) < dayDuration)
                visualOffset = dayLength * 0.15d + cycleOffset * configDayMultiplier.Value;
            else if ((cycleOffset -= dayDuration) < duskDuration)
                visualOffset = dayLength * 0.65d + cycleOffset * configDuskMultiplier.Value;
            else
                visualOffset = dayLength * 0.80d + (cycleOffset - duskDuration) * configNightMultiplier.Value;

            return cycle * dayLength + visualOffset;
        }

        // Replace only EnvMan's time read. ZNet's clock remains untouched for water and gameplay.
        [HarmonyPatch(typeof(EnvMan), "FixedUpdate")]
        public static class Patch_EnvMan_FixedUpdate
        {
            [HarmonyTranspiler]
            static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions)
            {
                var getTimeSeconds = AccessTools.Method(typeof(ZNet), nameof(ZNet.GetTimeSeconds));
                var getVisualTimeSeconds = AccessTools.Method(typeof(CustomDayCyclePlugin), nameof(GetVisualTimeSeconds));
                bool replaced = false;

                foreach (CodeInstruction instruction in instructions)
                {
                    if (!replaced && instruction.Calls(getTimeSeconds))
                    {
                        // The original call consumes ZNet.instance; discard it before the static replacement.
                        instruction.opcode = System.Reflection.Emit.OpCodes.Pop;
                        instruction.operand = null;
                        replaced = true;
                        yield return instruction;
                        yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, getVisualTimeSeconds);
                        continue;
                    }
                    yield return instruction;
                }

                if (!replaced)
                    Log.LogError("[DagrAndNott] EnvMan.FixedUpdate did not contain ZNet.GetTimeSeconds; visual time patch was not applied.");
            }
        }
    }
}
