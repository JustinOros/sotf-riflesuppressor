using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RedLoader;
using RedLoader.Utils;
using Sons.FieldOfView;
using Sons.Items.Core;
using Sons.Weapon;
using SonsSdk;
using SonsSdk.Attributes;
using TheForest.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RifleSuppressor;

public class RifleSuppressor : SonsMod
{
    private const string SuppressorModName = "CompactPistolSuppressorMod";
    private const string SuppressedFireEvent = "event:/SotF Events/player sounds/Weapons/PistolTactical/PistolTacticalFire";
    private const string MuzzleName = "RifleSuppressorMuzzle";
    private const string RifleFireEvent = "event:/SotF Events/player sounds/Weapons/Rifle/rifle_fire";
    private const string UpgradeParameter = "upgrade";
    private const float SuppressedUpgradeValue = 0.2f;

    private static bool _enabled = true;
    private static string _configPath;
    private static string _originalFireEvent;
    private static HarmonyLib.Harmony _audioHarmony;
    private static bool _loggedParamError;
    private static bool _loggedPistolParams;
    private static bool _reissuing;

    private static RifleAnimatorController _rifle;
    private static RifleAnimatorController _pendingRifle;
    private static ScriptableObject _suppressorMod;

    public RifleSuppressor()
    {
        OnUpdateCallback = OnUpdate;
        HarmonyPatchAll = true;
    }

    protected override void OnSdkInitialized()
    {
        _configPath = Path.Combine(LoaderEnvironment.UserDataDirectory, "RifleSuppressor.txt");
        Load();
        RLog.Msg($"RifleSuppressor loaded. Suppressor is {(_enabled ? "on" : "off")}. Change it with: suppressor on or suppressor off");
    }

    private void OnUpdate()
    {
        try
        {
            if (_rifle && _rifle.gameObject.activeInHierarchy)
                return;

            _rifle = null;
            var pending = _pendingRifle;
            _pendingRifle = null;
            if (!pending || !pending.gameObject.activeInHierarchy)
                return;
            AdoptRifle(pending);
        }
        catch (Exception e)
        {
            RLog.Error($"RifleSuppressor error: {e.Message}");
            _rifle = null;
        }
    }

    internal static void OnWeaponTick(RangedWeaponController controller)
    {
        if (!controller || (_rifle && _rifle.Pointer == controller.Pointer))
            return;
        var rifle = controller.TryCast<RifleAnimatorController>();
        if (rifle && rifle.IsLocalPlayer())
            _pendingRifle = rifle;
    }

    private static void AdoptRifle(RifleAnimatorController rifle)
    {
        _rifle = rifle;
        _originalFireEvent ??= rifle._gunShotAudioEvent;
        if (_enabled)
            SetSuppressor(rifle, true);
    }

    private static WeaponMod FindSuppressorMod()
    {
        if (_suppressorMod)
            return _suppressorMod.TryCast<WeaponMod>();
        foreach (var mod in Resources.FindObjectsOfTypeAll<WeaponMod>())
        {
            if (mod && mod.name == SuppressorModName)
            {
                _suppressorMod = mod;
                return mod;
            }
        }
        return null;
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }
        return null;
    }

    private static bool EnsureMuzzleSlot(RangedWeaponController rifle, WeaponMods mods)
    {
        var links = mods._modLocations;
        for (var i = 0; i < links.Count; i++)
        {
            if (links[i].Slot == WeaponMod.Slot.Muzzle)
                return true;
        }

        var anchor = FindChild(rifle.transform, "ProjectileVisualTransform");
        if (!anchor)
            return false;

        var muzzle = FindChild(anchor.parent, MuzzleName);
        if (!muzzle)
        {
            var go = new GameObject(MuzzleName);
            go.layer = anchor.gameObject.layer;
            muzzle = go.transform;
            muzzle.SetParent(anchor.parent, false);
            muzzle.localPosition = anchor.localPosition;
            muzzle.localRotation = Quaternion.identity;
            muzzle.localScale = Vector3.one;
        }

        var link = new WeaponMods.ModLink();
        link.Slot = WeaponMod.Slot.Muzzle;
        link.Location = muzzle;
        links.Add(link);
        return true;
    }

    private static Type FindGameType(string name)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            try
            {
                var t = asm.GetType(name, false) ?? asm.GetType("Il2Cpp." + name, false);
                if (t != null)
                    return t;
            }
            catch
            {
            }
        }

        foreach (var asm in assemblies)
        {
            var asmName = asm.GetName().Name ?? string.Empty;
            if (asm.IsDynamic || asmName.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                asmName.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ||
                asmName.StartsWith("Il2Cppmscorlib", StringComparison.OrdinalIgnoreCase) ||
                asmName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                asmName.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase))
                continue;

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var t in types)
            {
                if (t != null && t.Name == name)
                    return t;
            }
        }
        return null;
    }

    private static void InstallAudioPatches()
    {
        if (_audioHarmony != null)
            return;
        _audioHarmony = new HarmonyLib.Harmony("RifleSuppressor.Audio");

        var handler = FindGameType("FMOD_AnimationEventHandler");
        var animPrefix = new HarmonyMethod(typeof(RifleSuppressor).GetMethod(nameof(AnimEventPrefix), BindingFlags.NonPublic | BindingFlags.Static));
        var count = 0;
        if (handler != null)
        {
            foreach (var m in handler.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var ps = m.GetParameters();
                if (m.Name != "playFMODEvent" || ps.Length == 0 || ps[0].ParameterType != typeof(string))
                    continue;
                _audioHarmony.Patch(m, prefix: animPrefix);
                count++;
            }
        }

        var common = FindGameType("FMODCommon");
        var oneshotPrefix = new HarmonyMethod(typeof(RifleSuppressor).GetMethod(nameof(OneshotPrefix), BindingFlags.NonPublic | BindingFlags.Static));
        if (common != null)
        {
            foreach (var m in common.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var ps = m.GetParameters();
                if (m.Name != "PlayOneshotInternal" || ps.Length < 4 || ps[0].ParameterType != typeof(string))
                    continue;
                _audioHarmony.Patch(m, prefix: oneshotPrefix);
                count++;
            }
        }

        RLog.Msg($"Suppressor audio: patched {count} methods (handler={(handler != null)}, common={(common != null)})");
    }

    private static bool RifleSuppressedNow() =>
        _enabled && _rifle && _rifle.gameObject.activeInHierarchy;

    private static bool AnimEventPrefix(Component __instance, object[] __args)
    {
        try
        {
            if (!RifleSuppressedNow() || __args == null || __args.Length == 0 || __args[0] as string != RifleFireEvent)
                return true;
            if (!__instance || __instance.transform.root != _rifle.transform.root)
                return true;
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool OneshotPrefix(MethodBase __originalMethod, object[] __args)
    {
        if (_reissuing)
            return true;

        try
        {
            if (__args == null || __args.Length < 4 || __args[0] as string != SuppressedFireEvent)
                return true;

            var existing = __args[3] as Il2CppReferenceArray<Il2CppSystem.Object>;

            if (!RifleSuppressedNow())
            {
                if (!_loggedPistolParams && existing != null && existing.Length > 0)
                {
                    _loggedPistolParams = true;
                    RLog.Msg($"Suppressor audio: pistol params [{string.Join(", ", existing.Select(Describe))}]");
                }
                return true;
            }

            var oldCount = existing?.Length ?? 0;
            for (var i = 0; i < oldCount; i++)
            {
                if (existing[i] != null && existing[i].ToString() == UpgradeParameter)
                    return true;
            }

            var array = new Il2CppReferenceArray<Il2CppSystem.Object>(oldCount + 2);
            for (var i = 0; i < oldCount; i++)
                array[i] = existing[i];
            array[oldCount] = new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(UpgradeParameter));
            array[oldCount + 1] = BoxFloat(SuppressedUpgradeValue);

            var args = (object[])__args.Clone();
            args[3] = array;
            _reissuing = true;
            try
            {
                __originalMethod.Invoke(null, args);
            }
            finally
            {
                _reissuing = false;
            }
            return false;
        }
        catch (Exception e)
        {
            if (!_loggedParamError)
            {
                _loggedParamError = true;
                RLog.Error($"Suppressor audio: could not play suppressed shot: {(e.InnerException ?? e).Message}");
            }
            return true;
        }
    }

    private static Il2CppSystem.Object BoxFloat(float value)
    {
        object single = new Il2CppSystem.Single();
        typeof(Il2CppSystem.Single).GetField("m_value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(single, value);
        return ((Il2CppSystem.Single)single).BoxIl2CppObject();
    }

    private static string Describe(Il2CppSystem.Object o)
    {
        if (o == null)
            return "null";
        try
        {
            return $"{o.GetIl2CppType().Name}:{o.ToString()}";
        }
        catch
        {
            return "?";
        }
    }

    private static void SetSuppressor(RangedWeaponController rifle, bool on)
    {
        var weapon = rifle._rangedWeapon;
        var mods = weapon ? weapon._weaponMods : null;
        if (!mods)
        {
            Say("Suppressor: rifle has no WeaponMods");
            return;
        }

        var mod = FindSuppressorMod();
        if (!mod)
        {
            Say("Suppressor: pistol suppressor mod is not loaded");
            return;
        }

        if (on)
        {
            InstallAudioPatches();
            if (!EnsureMuzzleSlot(rifle, mods))
            {
                Say("Suppressor: could not find the rifle muzzle");
                return;
            }

            var applied = mods.HasMod(mod.ModItemId) || mods.ApplyMod(mod);
            rifle._gunShotAudioEvent = SuppressedFireEvent;
            RLog.Msg($"Suppressor on: applied={applied} slot={mod.AttachesToSlot} required={mod.RequiredSlot} item={mod.ModItemId}");
            return;
        }

        if (mods.HasMod(mod.ModItemId))
            mods.RemoveMod(mod);
        if (_originalFireEvent != null)
            rifle._gunShotAudioEvent = _originalFireEvent;
    }

    [DebugCommand("suppressor")]
    private static void SuppressorCommand(string args)
    {
        args = (args ?? string.Empty).Trim().ToLowerInvariant();

        if (args != "on" && args != "off")
        {
            Say($"Rifle suppressor is {(_enabled ? "on" : "off")}. Usage: suppressor on or suppressor off");
            return;
        }

        _enabled = args == "on";
        Save();
        if (_rifle)
            SetSuppressor(_rifle, _enabled);
        Say($"Rifle suppressor {(_enabled ? "on" : "off")}");
    }

    private static void Say(string text)
    {
        RLog.Msg(text);
        try
        {
            SonsTools.ShowMessage(text, 5f);
        }
        catch
        {
        }
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(_configPath))
                return;
            _enabled = File.ReadAllText(_configPath).Trim() != "0";
        }
        catch (Exception e)
        {
            RLog.Error($"RifleSuppressor could not read config: {e.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(_configPath, _enabled ? "1" : "0");
        }
        catch (Exception e)
        {
            RLog.Error($"RifleSuppressor could not write config: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(RangedWeaponController), nameof(RangedWeaponController.CheckFireInput))]
internal static class CheckFireInputPatch
{
    private static void Postfix(RangedWeaponController __instance)
    {
        try
        {
            global::RifleSuppressor.RifleSuppressor.OnWeaponTick(__instance);
        }
        catch
        {
        }
    }
}
