using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LoggerSystem
{
    [BepInPlugin("com.logger.system", "Logger System", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        public static ManualLogSource Log;
        public static string LogFilePath;

        // Per-unit toggles: key = sanitized unit name -> config toggle
        // Uses definition-level scanning (like Equalizer) so each unique unit type gets one toggle
        public static Dictionary<string, ConfigEntry<bool>> UnitToggles = new Dictionary<string, ConfigEntry<bool>>();

        // Active tracked units: instanceID -> tracker component
        public static Dictionary<int, UnitTracker> TrackedUnits = new Dictionary<int, UnitTracker>();

        public static float MissionStartTime = -1f;
        private bool _definitionScanDone = false;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            LogFilePath = Path.Combine(Path.GetDirectoryName(Info.Location), "logoutput.log");
            File.WriteAllText(LogFilePath, $"=== Logger System Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");

            var harmony = new Harmony("com.logger.system");
            try
            {
                harmony.PatchAll();
                Logger.LogInfo("Logger System loaded. Patches applied.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Patch failed: {ex}");
            }

            StartCoroutine(ScanRoutine());
        }

        /// <summary>
        /// Periodic scanner: first scans definitions (prefabs) to build config toggles,
        /// then scans live scene units to attach/detach trackers based on toggle state.
        /// </summary>
        private IEnumerator ScanRoutine()
        {
            // Wait for game to load definitions
            yield return new WaitForSeconds(5f);
            ScanDefinitions();

            float lastDefScan = Time.time;
            while (true)
            {
                yield return new WaitForSeconds(3f);
                // Re-scan definitions every 30s to pick up newly loaded mod units
                if (Time.time - lastDefScan > 30f)
                {
                    ScanDefinitions();
                    lastDefScan = Time.time;
                }
                ScanLiveUnits();
            }
        }

        /// <summary>
        /// Scans all UnitDefinition assets (like Equalizer's Resources.FindObjectsOfTypeAll pattern)
        /// and creates per-unit-type config toggles grouped by category.
        /// </summary>
        public void ScanDefinitions()
        {
            // Scan all definition types
            ScanDefinitionType<AircraftDefinition>("Aircraft", 600);
            ScanDefinitionType<ShipDefinition>("Ship", 500);
            ScanDefinitionType<MissileDefinition>("Missile", 300);
            ScanDefinitionType<BuildingDefinition>("Building", 200);

            // For GroundVehicle, Scenery, Container — use UnitDefinition and filter
            ScanAllUnitDefinitions();
    
            _definitionScanDone = true;
            Logger.LogInfo($"Definition scan complete. {UnitToggles.Count} unit toggles created.");
        }

        private void ScanDefinitionType<T>(string category, int baseOrder) where T : UnitDefinition
        {
            var defs = Resources.FindObjectsOfTypeAll<T>();
            int order = baseOrder;
            foreach (var def in defs)
            {
                if (def == null || string.IsNullOrEmpty(def.unitName)) continue;
                RegisterUnitToggle(def.unitName, def.name, category, order--);
            }
        }

        private void ScanAllUnitDefinitions()
        {
            // Catch GroundVehicle, Scenery, Container definitions that aren't covered above
            var allDefs = Resources.FindObjectsOfTypeAll<UnitDefinition>();
            foreach (var def in allDefs)
            {
                if (def == null) continue;
                string name = def.unitName;
                if (string.IsNullOrEmpty(name)) name = def.name;
                if (string.IsNullOrEmpty(name)) continue;

                string key = MakeKey(name, def.name);
                if (UnitToggles.ContainsKey(key)) continue; // Already registered by specific scan

                // Determine category from definition type
                string category = GetCategoryFromDefinition(def);
                RegisterUnitToggle(name, def.name, category, 100);
            }
        }

        private string GetCategoryFromDefinition(UnitDefinition def)
        {
            string typeName = def.GetType().Name;
            if (typeName.Contains("Aircraft")) return "Aircraft";
            if (typeName.Contains("Ship")) return "Ship";
            if (typeName.Contains("Missile")) return "Missile";
            if (typeName.Contains("Building")) return "Building";
            if (typeName.Contains("Vehicle")) return "GroundVehicle";
            if (typeName.Contains("Scenery")) return "Scenery/Other";
            if (typeName.Contains("Container")) return "Scenery/Other";
            return "Scenery/Other";
        }

        private void RegisterUnitToggle(string displayName, string internalName, string category, int order)
        {
            string key = MakeKey(displayName, internalName);
            if (UnitToggles.ContainsKey(key)) return;

            // BepInEx forbids these chars in section/key names: = \n \t \ " ' [ ]
            string label = displayName == internalName ? displayName : $"{displayName} ({internalName})";
            string safeLabel = SanitizeConfigKey(label);
            string safeCategory = SanitizeConfigKey(category);

            try
            {
                UnitToggles[key] = Config.Bind(safeCategory, safeLabel, false,
                    new ConfigDescription($"Enable logging for {label}",
                    null, new ConfigurationManagerAttributes { Order = order }));
                Logger.LogInfo($"Registered toggle: [{safeCategory}] {safeLabel}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not register toggle for '{label}': {ex.Message}");
            }
        }

        /// <summary>Strip characters BepInEx prohibits in config section/key names.</summary>
        public static string SanitizeConfigKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unknown";
            // Remove or replace: = \n \t \ " ' [ ]
            s = s.Replace("[", "(").Replace("]", ")")
                 .Replace("=", "-").Replace("\\", "/")
                 .Replace("'", "").Replace("\"", "")
                 .Replace("\n", " ").Replace("\t", " ");
            return s.Trim();
        }

        public static string MakeKey(string displayName, string internalName)
        {
            return $"{Sanitize(displayName)}|{internalName}".ToLowerInvariant();
        }

        /// <summary>
        /// Scans all live Unit instances in the scene. Attaches trackers to enabled ones,
        /// removes trackers from disabled ones.
        /// </summary>
        public void ScanLiveUnits()
        {
            if (!_definitionScanDone)
            {
                ScanDefinitions();
                return;
            }

            var allUnits = FindObjectsOfType<Unit>();
            if (allUnits == null || allUnits.Length == 0) return;

            if (MissionStartTime < 0f)
            {
                MissionStartTime = Time.time;
                WriteLog("[MISSION] Mission timer started.");
            }

            foreach (var unit in allUnits)
            {
                if (unit == null) continue;
                int id = unit.GetInstanceID();

                string defName = GetDefinitionName(unit);
                string displayName = GetDisplayName(unit);
                string key = MakeKey(displayName, defName);

                // If no toggle exists for this live unit, create one dynamically
                if (!UnitToggles.ContainsKey(key))
                {
                    string category = GetCategoryFromUnit(unit);
                    RegisterUnitToggle(displayName, defName, category, 0);
                }

                bool enabled = UnitToggles.ContainsKey(key) && UnitToggles[key].Value;

                if (enabled && !TrackedUnits.ContainsKey(id))
                {
                    // Attach tracker
                    var tracker = unit.gameObject.AddComponent<UnitTracker>();
                    tracker.Init(unit, GetCategoryFromUnit(unit), key);
                    TrackedUnits[id] = tracker;
                    WriteLog($"[TRACKER] Attached to {displayName} (ID:{id}, Cat:{GetCategoryFromUnit(unit)})");
                }
                else if (!enabled && TrackedUnits.ContainsKey(id))
                {
                    // Detach tracker
                    RemoveTracker(id);
                }
            }

            // Clean up dead trackers
            var deadKeys = TrackedUnits.Where(kvp => kvp.Value == null || kvp.Value.TrackedUnit == null)
                .Select(kvp => kvp.Key).ToList();
            foreach (var dk in deadKeys)
            {
                TrackedUnits.Remove(dk);
            }
        }

        public static void RemoveTracker(int id)
        {
            if (TrackedUnits.TryGetValue(id, out var tracker))
            {
                if (tracker != null)
                {
                    WriteLog($"[TRACKER] Detached from {tracker.UnitName} (ID:{id})");
                    Destroy(tracker);
                }
                TrackedUnits.Remove(id);
            }
        }

        public static string GetCategoryFromUnit(Unit unit)
        {
            if (unit is Aircraft) return "Aircraft";
            if (unit is GroundVehicle) return "GroundVehicle";
            if (unit is Ship) return "Ship";
            if (unit is Building) return "Building";
            if (unit is Missile) return "Missile";
            // Scenery, Container, and anything else
            string typeName = unit.GetType().Name;
            return typeName;
        }

        public static string GetDisplayName(Unit unit)
        {
            if (unit == null) return "NULL";
            try
            {
                if (!string.IsNullOrEmpty(unit.unitName)) return Sanitize(unit.unitName);
                if (unit.definition != null && !string.IsNullOrEmpty(unit.definition.unitName))
                    return Sanitize(unit.definition.unitName);
                return Sanitize(unit.gameObject.name);
            }
            catch { return "Unknown"; }
        }

        public static string GetDefinitionName(Unit unit)
        {
            if (unit == null) return "unknown";
            try
            {
                if (unit.definition != null) return unit.definition.name ?? "unknown";
                return unit.gameObject.name ?? "unknown";
            }
            catch { return "unknown"; }
        }

        public static string Sanitize(string n)
        {
            if (string.IsNullOrEmpty(n)) return "Unknown";
            if (n.Contains("(Clone)")) n = n.Substring(0, n.IndexOf("(Clone)"));
            return n.Trim();
        }

        public static float MissionTime()
        {
            if (MissionStartTime < 0f) return 0f;
            return Time.time - MissionStartTime;
        }

        public static string MissionTimeStr()
        {
            float t = MissionTime();
            int m = (int)(t / 60f);
            float s = t % 60f;
            return $"T+{m:D2}:{s:05.2f}";
        }

        public static void WriteLog(string msg)
        {
            string line = $"[{MissionTimeStr()}] {msg}";
            try { File.AppendAllText(LogFilePath, line + "\n"); } catch { }
            Log?.LogInfo(line);
        }

        /// <summary>
        /// Check if a given live unit is currently being logged.
        /// Used by patches to quickly filter events.
        /// </summary>
        public static bool IsTracked(Unit unit)
        {
            if (unit == null) return false;
            return TrackedUnits.ContainsKey(unit.GetInstanceID());
        }

        public static UnitTracker GetTracker(Unit unit)
        {
            if (unit == null) return null;
            TrackedUnits.TryGetValue(unit.GetInstanceID(), out var t);
            return t;
        }
    }
}
