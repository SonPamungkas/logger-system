using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LoggerSystem
{
    /// <summary>
    /// Attached to each tracked unit. Polls and logs comprehensive state every tick interval.
    /// Also hooks into events for damage, disable, faction change, missile registration, etc.
    /// </summary>
    public class UnitTracker : MonoBehaviour
    {
        public Unit TrackedUnit;
        public string UnitName;
        public string Category;
        public string ConfigKey;
        public int UnitID;

        // Snapshot for delta detection
        private Vector3 _lastPos;
        private float _lastHP = -1f;
        private bool _wasDisabled;
        private float _lastLogTime;
        private float _logInterval = 2f;
        private bool _initialized;
        private bool _spawnLogged;

        // Cached reflection
        private static FieldInfo _fuelLevelField;
        private static FieldInfo _countermeasureManagerField;
        private static PropertyInfo _definitionProp;

        public void Init(Unit unit, string category, string configKey)
        {
            TrackedUnit = unit;
            Category = category;
            ConfigKey = configKey;
            UnitID = unit.GetInstanceID();
            UnitName = Plugin.GetDisplayName(unit);
            _initialized = true;

            CacheReflection();
            LogSpawnReport();
            
            // Log initial inventory
            LogInventory();
        }

        private void CacheReflection()
        {
            if (_fuelLevelField == null)
                _fuelLevelField = typeof(Aircraft).GetField("fuelLevel", BindingFlags.Public | BindingFlags.Instance);
            if (_countermeasureManagerField == null)
                _countermeasureManagerField = typeof(Aircraft).GetField("countermeasureManager", BindingFlags.Public | BindingFlags.Instance);
            if (_definitionProp == null)
                _definitionProp = typeof(Aircraft).GetProperty("definition", BindingFlags.Public | BindingFlags.Instance);
        }

        private void LogSpawnReport()
        {
            if (_spawnLogged) return;
            _spawnLogged = true;

            L("========== SPAWN REPORT ==========");
            L($"  Name: {UnitName}");
            L($"  Category: {Category}");
            L($"  InstanceID: {UnitID}");
            L($"  GameObject: {TrackedUnit.gameObject.name}");
            L($"  Position: {FormatPos(TrackedUnit.transform.position)}");
            L($"  Rotation: {TrackedUnit.transform.eulerAngles}");

            // Unit base fields
            try { L($"  PersistentID: {TrackedUnit.persistentID}"); } catch { }
            try { L($"  UniqueName: {TrackedUnit.UniqueName}"); } catch { }
            try { L($"  UnitState: {TrackedUnit.unitState}"); } catch { }
            try { L($"  Disabled: {TrackedUnit.disabled}"); } catch { }
            try { L($"  RCS: {TrackedUnit.RCS:F4}"); } catch { }
            try { L($"  Speed: {TrackedUnit.speed:F1} m/s"); } catch { }
            try { L($"  RadarAlt: {TrackedUnit.radarAlt:F1}m"); } catch { }
            try { L($"  MaxRadius: {TrackedUnit.maxRadius:F1}"); } catch { }
            try { L($"  AirDensity: {TrackedUnit.airDensity:F3}"); } catch { }
            try { L($"  Networked: {TrackedUnit.networked}"); } catch { }

            // UnitDefinition fields
            LogDefinition();

            // Mass
            try { L($"  Mass: {TrackedUnit.GetMass():F1} kg"); } catch { }
            try { L($"  PrefabMass: {TrackedUnit.GetPrefabMass():F1} kg"); } catch { }

            // Faction
            try
            {
                var hq = Traverse.Create(TrackedUnit).Field("HQ").GetValue();
                if (hq != null) L($"  FactionHQ: {hq}");
            }
            catch { }

            // Parts
            LogParts();

            // Weapon stations
            LogWeaponStations();

            // Category-specific
            LogCategorySpecific();

            L("========== END SPAWN REPORT ==========");
            _lastPos = TrackedUnit.transform.position;
        }

        private void LogDefinition()
        {
            try
            {
                var def = TrackedUnit.definition;
                if (def == null) return;
                L($"  --- Definition ---");
                L($"    DefName: {def.unitName}");
                L($"    BogeyName: {def.bogeyName}");
                L($"    Description: {def.description}");
                L($"    Code: {def.code}");
                L($"    Value/Price: {def.value:F0}");
                L($"    Mass: {def.mass:F1}");
                L($"    RadarSize: {def.radarSize:F3}");
                L($"    ArmorTier: {def.armorTier:F1}");
                L($"    DamageTolerance: {def.damageTolerance:F1}");
                L($"    VisibleRange: {def.visibleRange:F0}");
                L($"    Length: {def.length:F1}");
                L($"    Width: {def.width:F1}");
                L($"    Height: {def.height:F1}");
                L($"    CaptureStrength: {def.captureStrength:F1}");
                L($"    CaptureDefense: {def.captureDefense:F1}");
                L($"    TypeIdentity: {def.typeIdentity}");
                L($"    RoleIdentity: {def.roleIdentity}");
                try { L($"    CanSlingLoad: {def.CanSlingLoad}"); } catch { }
            }
            catch { }
        }

        private void LogParts()
        {
            try
            {
                var parts = TrackedUnit.GetAllParts();
                if (parts == null || parts.Count == 0) return;
                L($"  --- UnitParts ({parts.Count}) ---");
                foreach (var part in parts)
                {
                    if (part == null) continue;
                    try
                    {
                        string critical = "";
                        try
                        {
                            var cf = typeof(UnitPart).GetField("criticalPart", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (cf != null && (bool)cf.GetValue(part)) critical = " [CRITICAL]";
                        }
                        catch { }
                        L($"    Part[{part.id}]: {part.gameObject.name} HP={part.hitPoints:F0} Mass={part.mass:F1}{critical}");

                        try
                        {
                            var armor = part.GetArmorProperties();
                            if (armor != null)
                                L($"      Armor: {armor}");
                        }
                        catch { }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void LogWeaponStations()
        {
            try
            {
                if (TrackedUnit.weaponStations == null) return;
                L($"  --- WeaponStations ({TrackedUnit.weaponStations.Count}) ---");
                foreach (var ws in TrackedUnit.weaponStations)
                {
                    if (ws == null) continue;
                    try
                    {
                        L($"    Station: {ws}");
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void LogCategorySpecific()
        {
            try
            {
                if (TrackedUnit is Aircraft ac)
                    LogAircraftDetails(ac);
                else if (TrackedUnit is Missile ms)
                    LogMissileDetails(ms);
                else if (TrackedUnit is GroundVehicle gv)
                    LogGroundVehicleDetails(gv);
                else if (TrackedUnit is Ship sh)
                    LogShipDetails(sh);
                else if (TrackedUnit is Building bd)
                    LogBuildingDetails(bd);
                
                // General "Other" info
                Aircraft ac2 = TrackedUnit as Aircraft;
                if (ac2 != null && ac2.playerRef.PlayerId != 0)
                    L($"  PLAYER CONTROLLED: {ac2.playerRef.PlayerId}");
            }
            catch { }
        }

        private void LogAircraftDetails(Aircraft ac)
        {
            L("  --- Aircraft Details ---");
            try { L($"    FuelLevel: {ac.fuelLevel:P1}"); } catch { }
            try { L($"    GForce: {ac.gForce:F2}"); } catch { }
            try { L($"    Skill: {ac.skill:F2}"); } catch { }
            try { L($"    Bravery: {ac.bravery:F2}"); } catch { }
            try { L($"    Ignition: {ac.Ignition}"); } catch { }
            try { L($"    GearDeployed: {ac.gearDeployed}"); } catch { }
            try { L($"    FlightAssist: {ac.flightAssist}"); } catch { }
            try { L($"    SortieScore: {ac.sortieScore:F0}"); } catch { }
            try { L($"    SimplePhysics: {ac.simplePhysics}"); } catch { }

            // AircraftDefinition specifics
            try
            {
                var def = ac.definition;
                if (def != null)
                {
                    var ap = def.aircraftParameters;
                    if (ap != null)
                    {
                        L($"    AircraftName: {ap.aircraftName}");
                        L($"    MaxSpeed: {ap.maxSpeed:F0}");
                        L($"    TakeoffSpeed: {ap.takeoffSpeed:F0}");
                        L($"    CornerSpeed: {ap.cornerSpeed:F0}");
                        L($"    GLimit: {ap.aircraftGLimit:F1}");
                        L($"    TurningRadius: {ap.turningRadius:F0}");
                        L($"    RankRequired: {ap.rankRequired}");
                    }
                    var info = def.aircraftInfo;
                    if (info != null)
                    {
                        L($"    EmptyWeight: {info.emptyWeight:F0}");
                        L($"    MaxWeight: {info.maxWeight:F0}");
                        L($"    StallSpeed: {info.stallSpeed:F0}");
                        L($"    Maneuverability: {info.maneuverability:F1}");
                    }
                }
            }
            catch { }

            // Loadout
            try
            {
                if (ac.loadout != null)
                    L($"    Loadout: {ac.loadout}");
            }
            catch { }

            // WeaponManager
            try
            {
                if (ac.weaponManager != null)
                {
                    L($"    CurrentLoadoutMass: {ac.weaponManager.GetCurrentMass():F1}");
                    L($"    CurrentLoadoutValue: {ac.weaponManager.GetCurrentValue(true):F0}");
                    L($"    CurrentWarheads: {ac.weaponManager.GetCurrentWarheads()}");
                }
            }
            catch { }

            // CountermeasureManager
            try
            {
                if (ac.countermeasureManager != null)
                    L($"    CountermeasureManager: present");
            }
            catch { }

            // Radar
            try
            {
                if (TrackedUnit.radar != null)
                    L($"    Radar: {TrackedUnit.radar.gameObject.name}");
            }
            catch { }
        }

        private void LogMissileDetails(Missile ms)
        {
            L("  --- Missile Details ---");
            try { L($"    OwnerID: {ms.ownerID}"); } catch { }
            try { L($"    Owner: {ms.owner?.unitName ?? "null"}"); } catch { }
            try { L($"    SeekerMode: {ms.seekerMode}"); } catch { }
            try { L($"    StartVelocity: {ms.startingVelocity}"); } catch { }
            try
            {
                var info = ms.GetWeaponInfo();
                if (info != null) L($"    WeaponInfo: {info}");
            }
            catch { }
            try { L($"    BlastYield: {ms.GetYield():F0}"); } catch { }
            try { L($"    PierceDamage: {ms.GetPierce():F0}"); } catch { }
            try { L($"    Mass: {ms.GetMass():F1}"); } catch { }
            try { L($"    FinArea: {ms.GetFinArea():F3}"); } catch { }
            try { L($"    Torque: {ms.GetTorque():F1}"); } catch { }
            try { L($"    MaxTurnRate: {ms.GetMaxTurnRate():F1}"); } catch { }
            try { L($"    SeekerType: {ms.GetSeekerType()}"); } catch { }
            try { L($"    TotalBurnTime: {ms.GetTotalBurnTime():F1}s"); } catch { }
            try { L($"    TimeSinceSpawn: {ms.timeSinceSpawn:F2}s"); } catch { }
        }

        private void LogGroundVehicleDetails(GroundVehicle gv)
        {
            L("  --- GroundVehicle Details ---");
            try { L($"    TopSpeed: {gv.GetTopSpeed():F1}"); } catch { }
            try { L($"    Skill: {gv.skill:F2}"); } catch { }
            try { L($"    Destination: {gv.GetDestination()}"); } catch { }
            try { L($"    HoldPosition: {gv.GetHoldPosition()}"); } catch { }
        }

        private void LogShipDetails(Ship sh)
        {
            L("  --- Ship Details ---");
            // Ship inherits from Unit, most fields are private
            try { L($"    Name: {sh.unitName}"); } catch { }
        }

        private void LogBuildingDetails(Building bd)
        {
            L("  --- Building Details ---");
            try { L($"    Capturable: {bd.capturable}"); } catch { }
            try { L($"    NeedsRepair: {bd.needsRepair}"); } catch { }
            try { L($"    CanRearm: {bd.canRearm}"); } catch { }
        }

        private void Update()
        {
            if (!_initialized || TrackedUnit == null)
            {
                if (_initialized)
                {
                    L("*** UNIT DESTROYED / NULL ***");
                    Plugin.TrackedUnits.Remove(UnitID);
                    Destroy(this);
                }
                return;
            }

            if (Time.time - _lastLogTime < _logInterval) return;
            _lastLogTime = Time.time;

            LogPeriodicState();
        }

        private void LogPeriodicState()
        {
            try
            {
                Vector3 pos = TrackedUnit.transform.position;
                float dist = Vector3.Distance(pos, _lastPos);

                // Only log position if moved significantly
                if (dist > 1f)
                    L($"POS: {FormatPos(pos)} | Δ={dist:F1}m | Speed={TrackedUnit.speed:F1}m/s | Alt={TrackedUnit.radarAlt:F0}m");

                _lastPos = pos;

                // Disabled state change
                if (TrackedUnit.disabled != _wasDisabled)
                {
                    L($"STATE: disabled changed {_wasDisabled} -> {TrackedUnit.disabled}");
                    _wasDisabled = TrackedUnit.disabled;
                }

                // HP tracking for parts
                LogPartHealthChanges();

                // Aircraft-specific periodic
                if (TrackedUnit is Aircraft ac)
                {
                    try 
                    {
                        string flareInfo = GetFlareInfo(ac);
                        string capInfo = GetCapacitorInfo(ac);
                        L($"FUEL: {ac.fuelLevel:P1} | G={ac.gForce:F1} | {flareInfo} | {capInfo}");
                    } 
                    catch { }
                }

                // Missile periodic
                if (TrackedUnit is Missile ms)
                {
                    try
                    {
                        L($"MISSILE: t={ms.timeSinceSpawn:F1}s engine={ms.EngineOn()} speed={TrackedUnit.speed:F0}");
                        try
                        {
                            var targetField = typeof(Missile).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                            var target = targetField?.GetValue(ms) as Unit;
                            if (target != null)
                                L($"  TARGET: {Plugin.GetDisplayName(target)} at {FormatPos(target.transform.position)}");
                        }
                        catch { }
                    }
                    catch { }
                }

                // Periodic inventory check for everyone
                LogInventory();
            }
            catch { }
        }

        private void LogInventory()
        {
            try
            {
                if (TrackedUnit.weaponStations == null) return;
                var list = new List<string>();
                foreach (var ws in TrackedUnit.weaponStations)
                {
                    if (ws == null) continue;
                    string name = ws.WeaponInfo != null ? ws.WeaponInfo.weaponName : "Gun";
                    list.Add($"{name}:{ws.Ammo}/{ws.FullAmmo}");
                }
                if (list.Count > 0)
                    L($"INVENTORY: {string.Join(", ", list)}");
            }
            catch { }
        }

        private string GetFlareInfo(Aircraft ac)
        {
            try
            {
                if (ac.countermeasureManager == null) return "CM: None";
                var stationsField = typeof(CountermeasureManager).GetField("countermeasureStations", BindingFlags.NonPublic | BindingFlags.Instance);
                var stations = stationsField?.GetValue(ac.countermeasureManager) as System.Collections.IList;
                if (stations == null) return "CM: 0";

                int totalFlares = 0;
                foreach (var s in stations)
                {
                    var ammoField = s.GetType().GetField("ammo", BindingFlags.Public | BindingFlags.Instance);
                    if (ammoField != null) totalFlares += (int)ammoField.GetValue(s);
                }
                return $"Flares: {totalFlares}";
            }
            catch { return "CM: Err"; }
        }

        private string GetCapacitorInfo(Aircraft ac)
        {
            try
            {
                if (ac.countermeasureManager == null) return "";
                var stationsField = typeof(CountermeasureManager).GetField("countermeasureStations", BindingFlags.NonPublic | BindingFlags.Instance);
                var stations = stationsField?.GetValue(ac.countermeasureManager) as System.Collections.IList;
                if (stations == null) return "";

                foreach (var s in stations)
                {
                    var cmField = s.GetType().GetField("countermeasures", BindingFlags.NonPublic | BindingFlags.Instance);
                    var cms = cmField?.GetValue(s) as System.Collections.IList;
                    if (cms == null) continue;

                    foreach (var cm in cms)
                    {
                        if (cm is RadarJammer jammer)
                        {
                            var capField = typeof(RadarJammer).GetField("capacitance", BindingFlags.NonPublic | BindingFlags.Instance);
                            float cap = (float)(capField?.GetValue(jammer) ?? 0f);
                            return $"Capacitor: {cap:F1}";
                        }
                    }
                }
                return "";
            }
            catch { return ""; }
        }

        private void LogPartHealthChanges()
        {
            try
            {
                var parts = TrackedUnit.GetAllParts();
                if (parts == null) return;
                float totalHP = 0;
                foreach (var p in parts)
                {
                    if (p != null) totalHP += p.hitPoints;
                }
                if (_lastHP >= 0 && Math.Abs(totalHP - _lastHP) > 0.5f)
                    L($"HP: totalHP changed {_lastHP:F0} -> {totalHP:F0} (Δ={totalHP - _lastHP:F0})");
                _lastHP = totalHP;
            }
            catch { }
        }

        private void OnDestroy()
        {
            L("*** TRACKER DESTROYED ***");
            Plugin.TrackedUnits.Remove(UnitID);
        }

        private void L(string msg)
        {
            Plugin.WriteLog($"[{Category}][{UnitName}#{UnitID}] {msg}");
        }

        public static string FormatPos(Vector3 v)
        {
            return $"({v.x:F1}, {v.y:F1}, {v.z:F1})";
        }
    }
}
