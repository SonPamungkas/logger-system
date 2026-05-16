using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LoggerSystem
{
    // ==================== DAMAGE PATCHES ====================

    /// <summary>Intercept UnitPart.ApplyDamage to log every damage event on tracked units.</summary>
    [HarmonyPatch(typeof(UnitPart), "ApplyDamage")]
    public static class UnitPart_ApplyDamage_Patch
    {
        public static void Postfix(UnitPart __instance, float netPierceDamage, float netBlastDamage, float netFireDamage, float netImpactDamage)
        {
            try
            {
                var unit = __instance.parentUnit;
                if (unit == null) return;
                int id = unit.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(unit)}#{id}] " +
                    $"DAMAGE RECEIVED on part [{__instance.id}]{__instance.gameObject.name}: " +
                    $"Pierce={netPierceDamage:F0} Blast={netBlastDamage:F0} Fire={netFireDamage:F0} Impact={netImpactDamage:F0} " +
                    $"| PartHP={__instance.hitPoints:F0}");
            }
            catch { }
        }
    }

    /// <summary>Intercept UnitPart.Detach to log part destruction/detachment.</summary>
    [HarmonyPatch(typeof(UnitPart), "Detach")]
    public static class UnitPart_Detach_Patch
    {
        public static void Prefix(UnitPart __instance, Vector3 velocity)
        {
            try
            {
                var unit = __instance.parentUnit;
                if (unit == null) return;
                int id = unit.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(unit)}#{id}] " +
                    $"PART DETACHED: [{__instance.id}]{__instance.gameObject.name} vel={velocity.magnitude:F1}m/s");
            }
            catch { }
        }
    }

    // ==================== UNIT LIFECYCLE PATCHES ====================

    /// <summary>Log when any Unit is disabled (killed).</summary>
    [HarmonyPatch(typeof(Unit), "DisableUnit")]
    public static class Unit_DisableUnit_Patch
    {
        public static void Prefix(Unit __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"*** UNIT DISABLED/KILLED *** at {UnitTracker.FormatPos(__instance.transform.position)}");
            }
            catch { }
        }
    }

    /// <summary>Log when Unit.ReportKilled is called.</summary>
    [HarmonyPatch(typeof(Unit), "ReportKilled")]
    public static class Unit_ReportKilled_Patch
    {
        public static void Prefix(Unit __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"*** KILL REPORTED ***");
            }
            catch { }
        }
    }

    /// <summary>Log Unit.OnDestroy for tracked units.</summary>
    [HarmonyPatch(typeof(Unit), "OnDestroy")]
    public static class Unit_OnDestroy_Patch
    {
        public static void Prefix(Unit __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"*** UNIT GAMEOBJECT DESTROYED ***");
            }
            catch { }
        }
    }

    // ==================== DAMAGE RECORDING / HIT REGISTRATION ====================

    /// <summary>Log when a tracked unit records damage credit (who damaged it).</summary>
    [HarmonyPatch(typeof(Unit), "RecordDamage")]
    public static class Unit_RecordDamage_Patch
    {
        public static void Postfix(Unit __instance, PersistentID lastDamagedBy, float damageAmount)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"DAMAGE CREDIT: {damageAmount:F0} from PersistentID={lastDamagedBy}");
            }
            catch { }
        }
    }

    /// <summary>Log when a tracked unit registers a hit on another unit (outgoing damage).</summary>
    [HarmonyPatch(typeof(Unit), "RegisterHit")]
    public static class Unit_RegisterHit_Patch
    {
        public static void Postfix(Unit __instance, Unit hitUnit, Vector3 relativePos, Vector3 bulletVelocity, WeaponInfo weaponInfo)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (Plugin.TrackedUnits.ContainsKey(id))
                {
                    string targetName = hitUnit != null ? Plugin.GetDisplayName(hitUnit) : "null";
                    string wInfo = weaponInfo != null ? weaponInfo.ToString() : "unknown";
                    Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                        $"HIT DEALT -> {targetName} weapon={wInfo} bulletSpeed={bulletVelocity.magnitude:F0}m/s");
                }

                // Also log from victim's perspective if tracked
                if (hitUnit != null)
                {
                    int hid = hitUnit.GetInstanceID();
                    if (Plugin.TrackedUnits.ContainsKey(hid))
                    {
                        Plugin.WriteLog($"[{Plugin.TrackedUnits[hid].Category}][{Plugin.GetDisplayName(hitUnit)}#{hid}] " +
                            $"HIT RECEIVED <- {Plugin.GetDisplayName(__instance)} weapon={weaponInfo}");
                    }
                }
            }
            catch { }
        }
    }

    // ==================== MISSILE EVENTS ====================

    /// <summary>Log missile target changes.</summary>
    [HarmonyPatch(typeof(Missile), "SetTarget")]
    public static class Missile_SetTarget_Patch
    {
        public static void Postfix(Missile __instance, Unit target)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                string tName = target != null ? Plugin.GetDisplayName(target) : "NONE";
                Plugin.WriteLog($"[Missile][{Plugin.GetDisplayName(__instance)}#{id}] TARGET SET -> {tName}");

                // Also log on the target if tracked
                if (target != null)
                {
                    int tid = target.GetInstanceID();
                    if (Plugin.TrackedUnits.ContainsKey(tid))
                    {
                        Plugin.WriteLog($"[{Plugin.TrackedUnits[tid].Category}][{Plugin.GetDisplayName(target)}#{tid}] " +
                            $"MISSILE INCOMING: {Plugin.GetDisplayName(__instance)} targeting this unit");
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>Log missile detonation.</summary>
    [HarmonyPatch(typeof(Missile), "Detonate")]
    public static class Missile_Detonate_Patch
    {
        public static void Prefix(Missile __instance, bool hitArmor, bool hitTerrain)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[Missile][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"*** DETONATED *** hitArmor={hitArmor} hitTerrain={hitTerrain} " +
                    $"at {UnitTracker.FormatPos(__instance.transform.position)} " +
                    $"yield={__instance.GetYield():F0} pierce={__instance.GetPierce():F0}");
            }
            catch { }
        }
    }

    /// <summary>Log missile aimpoint changes.</summary>
    [HarmonyPatch(typeof(Missile), "SetAimpoint")]
    public static class Missile_SetAimpoint_Patch
    {
        public static void Postfix(Missile __instance, GlobalPosition aimPoint, Vector3 targetVel)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[Missile][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"AIMPOINT: {aimPoint} targetVel={targetVel}");
            }
            catch { }
        }
    }

    // ==================== AIRCRAFT EVENTS ====================

    /// <summary>Log missile launches from tracked aircraft (local player calls this).</summary>
    [HarmonyPatch(typeof(Aircraft), "CmdLaunchMissile")]
    public static class Aircraft_CmdLaunchMissile_Patch
    {
        public static void Postfix(Aircraft __instance, byte stationIndex, Unit target, GlobalPosition aimpoint)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                string tName = target != null ? Plugin.GetDisplayName(target) : "NONE";
                Plugin.WriteLog($"[Aircraft][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"MISSILE LAUNCHED (CMD): station={stationIndex} target={tName} aimpoint={aimpoint}");
            }
            catch { }
        }
    }

    /// <summary>Log missile launches from tracked aircraft (synced via RPC).</summary>
    [HarmonyPatch(typeof(Aircraft), "RpcLaunchMissile")]
    public static class Aircraft_LaunchMissile_Patch
    {
        public static void Postfix(Aircraft __instance, byte stationIndex, Unit target, GlobalPosition aimpoint)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                string tName = target != null ? Plugin.GetDisplayName(target) : "NONE";
                Plugin.WriteLog($"[Aircraft][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"MISSILE LAUNCHED (RPC): station={stationIndex} target={tName} aimpoint={aimpoint}");
            }
            catch { }
        }
    }

    /// <summary>Log countermeasure deployment (local player).</summary>
    [HarmonyPatch(typeof(Aircraft), "CmdCountermeasures")]
    public static class Aircraft_CmdCountermeasures_Patch
    {
        public static void Postfix(Aircraft __instance, bool active, byte index)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[Aircraft][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"COUNTERMEASURE (CMD): index={index} active={active}");
            }
            catch { }
        }
    }

    /// <summary>Log countermeasure deployment (synced via RPC).</summary>
    [HarmonyPatch(typeof(Aircraft), "RpcCountermeasures")]
    public static class Aircraft_Countermeasures_Patch
    {
        public static void Postfix(Aircraft __instance, byte index)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[Aircraft][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"COUNTERMEASURE (RPC): index={index}");
            }
            catch { }
        }
    }

    /// <summary>Log ejection sequence.</summary>
    [HarmonyPatch(typeof(Aircraft), "StartEjectionSequence")]
    public static class Aircraft_Eject_Patch
    {
        public static void Prefix(Aircraft __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[Aircraft][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"*** EJECTION SEQUENCE *** at {UnitTracker.FormatPos(__instance.transform.position)}");
            }
            catch { }
        }
    }

    /// <summary>Log gear state changes.</summary>
    [HarmonyPatch(typeof(Aircraft), "SetGear", new Type[] { typeof(bool) })]
    public static class Aircraft_SetGear_Patch
    {
        public static void Postfix(Aircraft __instance, bool deployed)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[Aircraft][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"GEAR: {(deployed ? "DEPLOYED" : "RETRACTED")}");
            }
            catch { }
        }
    }

    /// <summary>Log rearm events.</summary>
    [HarmonyPatch(typeof(Aircraft), "Rearm")]
    public static class Aircraft_Rearm_Patch
    {
        public static void Postfix(Aircraft __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[Aircraft][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"REARMED at {UnitTracker.FormatPos(__instance.transform.position)}");
            }
            catch { }
        }
    }

    // ==================== UNIT FACTION / STATE CHANGES ====================

    /// <summary>Log when a unit's faction HQ changes.</summary>
    [HarmonyPatch(typeof(Unit), "HQChanged")]
    public static class Unit_HQChanged_Patch
    {
        public static void Postfix(Unit __instance, FactionHQ oldHQ, FactionHQ newHQ)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"FACTION CHANGED: {oldHQ} -> {newHQ}");
            }
            catch { }
        }
    }

    /// <summary>Log when a unit changes state.</summary>
    [HarmonyPatch(typeof(Unit), "ChangeUnitState")]
    public static class Unit_ChangeState_Patch
    {
        public static void Postfix(Unit __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"UNIT STATE -> {__instance.unitState}");
            }
            catch { }
        }
    }

    /// <summary>Log missile registration on tracked units (incoming missile lock).</summary>
    [HarmonyPatch(typeof(Unit), "RegisterMissile")]
    public static class Unit_RegisterMissile_Patch
    {
        public static void Postfix(Unit __instance, Missile missile)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"MISSILE LOCKED ON: {Plugin.GetDisplayName(missile)} (seeker={missile.seekerMode})");
            }
            catch { }
        }
    }

    /// <summary>Log missile deregistration (missile lost lock or destroyed).</summary>
    [HarmonyPatch(typeof(Unit), "DeregisterMissile")]
    public static class Unit_DeregisterMissile_Patch
    {
        public static void Postfix(Unit __instance, Missile missile)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"MISSILE DEREGISTERED: {Plugin.GetDisplayName(missile)}");
            }
            catch { }
        }
    }

    // ==================== RCS / VISIBILITY MODIFICATIONS ====================

    /// <summary>Log RCS modifications on tracked units.</summary>
    [HarmonyPatch(typeof(Unit), "ModifyRCS")]
    public static class Unit_ModifyRCS_Patch
    {
        public static void Postfix(Unit __instance, float changeInRCS)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"RCS MODIFIED: Δ={changeInRCS:F4} newRCS={__instance.RCS:F4}");
            }
            catch { }
        }
    }

    /// <summary>Log visibility changes.</summary>
    [HarmonyPatch(typeof(Unit), "ModifyVisibility")]
    public static class Unit_ModifyVisibility_Patch
    {
        public static void Postfix(Unit __instance, float visibility)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"VISIBILITY MODIFIED: {visibility:F3} (total={__instance.GetVisibility():F3})");
            }
            catch { }
        }
    }

    // ==================== UNIT INITIALIZATION ====================

    /// <summary>Log unit initialization.</summary>
    [HarmonyPatch(typeof(Unit), "InitializeUnit")]
    public static class Unit_Initialize_Patch
    {
        public static void Postfix(Unit __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"UNIT INITIALIZED at {UnitTracker.FormatPos(__instance.transform.position)}");
            }
            catch { }
        }
    }

    // ==================== BUILDING-SPECIFIC ====================

    /// <summary>Log building collapse. Parameter names must match: oldState, newState.</summary>
    [HarmonyPatch(typeof(Building), "UnitDisabled")]
    public static class Building_Disabled_Patch
    {
        public static void Postfix(Building __instance, bool newState)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                if (newState)
                    Plugin.WriteLog($"[Building][{Plugin.GetDisplayName(__instance)}#{id}] *** BUILDING COLLAPSED/DISABLED ***");
            }
            catch { }
        }
    }

    // ==================== JAMMING ====================

    /// <summary>Log jamming events on tracked units.</summary>
    [HarmonyPatch(typeof(Unit), "Jam")]
    public static class Unit_Jam_Patch
    {
        public static void Postfix(Unit __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"JAMMED");
            }
            catch { }
        }
    }

    // ==================== WEAPON FIRING ====================

    /// <summary>Log weapon firing state changes.</summary>
    [HarmonyPatch(typeof(Unit), "SetFiringState")]
    public static class Unit_SetFiringState_Patch
    {
        public static void Postfix(Unit __instance, int index, bool firing)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(__instance)}#{id}] " +
                    $"WEAPON FIRE STATE: station={index} firing={firing}");
            }
            catch { }
        }
    }

    /// <summary>Log direct weapon station fire events (more reliable for players).</summary>
    [HarmonyPatch(typeof(WeaponStation), "Fire")]
    public static class WeaponStation_Fire_Patch
    {
        public static void Postfix(WeaponStation __instance, Unit owner, Unit target)
        {
            try
            {
                if (owner == null) return;
                int id = owner.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                string tName = target != null ? Plugin.GetDisplayName(target) : "NONE";
                Plugin.WriteLog($"[{Plugin.TrackedUnits[id].Category}][{Plugin.GetDisplayName(owner)}#{id}] " +
                    $"WEAPON FIRE: station={__instance.Number} target={tName} ammo={__instance.Ammo}/{__instance.FullAmmo}");
            }
            catch { }
        }
    }

    /// <summary>Log fuel consumption.</summary>
    [HarmonyPatch(typeof(Aircraft), "UseFuel")]
    public static class Aircraft_UseFuel_Patch
    {
        public static void Postfix(Aircraft __instance, float fuelDrawn, bool __result)
        {
            try
            {
                if (!__result) return; // Didn't actually consume?
                int id = __instance.GetInstanceID();
                if (!Plugin.TrackedUnits.ContainsKey(id)) return;

                if (fuelDrawn > 0.1f) // Only log significant draws
                {
                    Plugin.WriteLog($"[Aircraft][{Plugin.GetDisplayName(__instance)}#{id}] " +
                        $"FUEL USED: {fuelDrawn:F2} | Current: {__instance.fuelLevel:P1}");
                }
            }
            catch { }
        }
    }
}
