using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Exiled.API.Features;
using MEC;
using UnityEngine;
using UserSettings.ServerSpecific;

namespace EventHUD.FpvDrone
{
    public static class FpvDroneSystem
    {
        private static readonly List<DroneInstance> Drones = new List<DroneInstance>();
        private static int _nextId = 1;
        private static bool _reg;

        public const int KeyForward = 6001;
        public const int KeyLeft = 6002;
        public const int KeyBack = 6003;
        public const int KeyRight = 6004;
        public const int KeyDown = 6005;
        public const int KeyUp = 6006;
        public const int KeyYawLeft = 6007;
        public const int KeyYawRight = 6008;
        public const int KeyDestruct = 6009;
        public const int KeyExit = 6010;
        public const int KeyDrop = 6011;

        private static readonly List<ServerSpecificSettingBase> KB = new List<ServerSpecificSettingBase>();

        public static void Register()
        {
            if (_reg) return; _reg = true;
            KB.Add(new SSKeybindSetting(KeyForward, "Drone: Forward", KeyCode.W));
            KB.Add(new SSKeybindSetting(KeyLeft, "Drone: Left", KeyCode.A));
            KB.Add(new SSKeybindSetting(KeyBack, "Drone: Back", KeyCode.S));
            KB.Add(new SSKeybindSetting(KeyRight, "Drone: Right", KeyCode.D));
            KB.Add(new SSKeybindSetting(KeyDown, "Drone: Descend", KeyCode.Q));
            KB.Add(new SSKeybindSetting(KeyUp, "Drone: Ascend", KeyCode.E));
            KB.Add(new SSKeybindSetting(KeyYawLeft, "Drone: Yaw Left", KeyCode.LeftArrow));
            KB.Add(new SSKeybindSetting(KeyYawRight, "Drone: Yaw Right", KeyCode.RightArrow));
            KB.Add(new SSKeybindSetting(KeyDestruct, "Drone: Self-Destruct", KeyCode.R));
            KB.Add(new SSKeybindSetting(KeyExit, "Drone: Exit", KeyCode.Mouse0));
            KB.Add(new SSKeybindSetting(KeyDrop, "Drone: Drop Payload", KeyCode.Mouse1));

            ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnSetting;
            Exiled.Events.Handlers.Player.Shooting += OnShoot;
            Exiled.Events.Handlers.Player.Died += OnDied;
            Exiled.Events.Handlers.Player.Left += OnLeft;
            Exiled.Events.Handlers.Server.RoundStarted += OnRound;
        }

        public static void Unregister()
        {
            if (!_reg) return; _reg = false;
            ServerSpecificSettingsSync.ServerOnSettingValueReceived -= OnSetting;
            Exiled.Events.Handlers.Player.Shooting -= OnShoot;
            Exiled.Events.Handlers.Player.Died -= OnDied;
            Exiled.Events.Handlers.Player.Left -= OnLeft;
            Exiled.Events.Handlers.Server.RoundStarted -= OnRound;
            DestroyAll(); KB.Clear();
        }

        public static void Reset() { DestroyAll(); _nextId = 1; }

        public static DroneInstance SpawnDrone(Player owner, Vector3 pos, Quaternion rot)
        {
            object sch = SpawnSchematic("fpv_\u0434\u0440\u043e\u043d", pos, rot);
            if (sch == null) { Log.Warn("[FPV] Schematic spawn failed"); return null; }
            var d = new DroneInstance { Id = _nextId++, Owner = owner, Schematic = sch, Position = pos, Rotation = rot };
            Drones.Add(d); d.StartPhysics(); d.PlayFlyingAnimation(); return d;
        }

        public static void RemoveDrone(DroneInstance d) => Drones.Remove(d);
        public static void DestroyAll() { foreach (var d in Drones.ToList()) d.Destroy(false); Drones.Clear(); }
        public static DroneInstance GetByOwner(Player p) => Drones.FirstOrDefault(d => d.Owner == p && !d.IsDestroyed);
        public static DroneInstance GetByPilot(Player p) => Drones.FirstOrDefault(d => d.Pilot == p && d.IsPiloting && !d.IsDestroyed);
        public static int ActiveCount => Drones.Count(d => !d.IsDestroyed);

        public static string ListDrones()
        {
            if (Drones.Count == 0) return "No active drones.";
            return string.Join("\n", Drones.Where(d => !d.IsDestroyed).Select(d =>
                $"#{d.Id} Owner:{d.Owner?.Nickname ?? "?"} HP:{d.Hp:0} Bat:{d.BatteryVoltage:0.00}V Pilot:{(d.IsPiloting ? d.Pilot?.Nickname : "-")}"));
        }

        public static void SendDroneKeybinds(Player p)
        { if (p?.IsConnected == true) ServerSpecificSettingsSync.SendToPlayer(p.ReferenceHub, KB.ToArray()); }

        public static void RestoreKeybinds(Player p)
        { if (p?.IsConnected == true) Timing.CallDelayed(0.5f, () => Hud.SssRoleSync.SyncPlayer(p)); }

        public static void ShowHitmarker(Player p)
        {
            if (p?.IsConnected == true)
            {
                try
                {
                    var hmType = Type.GetType("Hitmarker, Mirror");
                    if (hmType == null) hmType = Type.GetType("Hitmarker, Assembly-CSharp");
                    if (hmType != null)
                    {
                        var m = hmType.GetMethod("SendHitmarkerDirectly",
                            BindingFlags.Static | BindingFlags.Public,
                            null, new[] { typeof(ReferenceHub), typeof(float) }, null);
                        m?.Invoke(null, new object[] { p.ReferenceHub, 1f });
                    }
                }
                catch { }
            }
        }

        private static void OnSetting(ReferenceHub hub, ServerSpecificSettingBase s)
        {
            if (!(s is SSKeybindSetting kb)) return;
            if (kb.SettingId < KeyForward || kb.SettingId > KeyDrop) return;
            Player p = Player.Get(hub); if (p == null) return;
            var d = GetByPilot(p); if (d == null) return;
            d.OnKey(kb.SettingId, kb.SyncIsPressed);
        }

        private static void OnShoot(Exiled.Events.EventArgs.Player.ShootingEventArgs ev)
        {
            if (ev?.Player == null || Drones.Count == 0) return;
            var t = FindTarget(ev.Player); if (t == null) return;
            t.OnShot(ev.Player, WeaponDmg(ev.Firearm));
        }

        private static DroneInstance FindTarget(Player s)
        {
            Vector3 o = s.CameraTransform.position, dir = s.CameraTransform.forward;
            DroneInstance best = null; float bd = float.MaxValue;
            foreach (var d in Drones)
            {
                if (d.IsDestroyed) continue;
                Vector3 td = d.Position - o; float dist = td.magnitude;
                if (dist > 200f) continue;
                if (Vector3.Angle(dir, td.normalized) > 5f) continue;
                if (dist < bd) { bd = dist; best = d; }
            }
            return best;
        }

        private static float WeaponDmg(Exiled.API.Features.Items.Item f)
        {
            if (f == null) return 10f;
            switch (f.Type)
            {
                case ItemType.GunCOM15: return 15f; case ItemType.GunCOM18: return 18f;
                case ItemType.GunFSP9: return 20f; case ItemType.GunCrossvec: return 20f;
                case ItemType.GunE11SR: return 25f; case ItemType.GunAK: return 30f;
                case ItemType.GunShotgun: return 50f; case ItemType.GunLogicer: return 22f;
                case ItemType.GunRevolver: return 40f; case ItemType.GunA7: return 22f;
                case ItemType.GunFRMG0: return 20f; case ItemType.ParticleDisruptor: return 100f;
                case ItemType.MicroHID: return 150f; default: return 15f;
            }
        }

        private static void OnDied(Exiled.Events.EventArgs.Player.DiedEventArgs ev)
        { if (ev?.Player != null) GetByPilot(ev.Player)?.StopPiloting(false); }

        private static void OnLeft(Exiled.Events.EventArgs.Player.LeftEventArgs ev)
        {
            if (ev?.Player == null) return;
            GetByPilot(ev.Player)?.StopPiloting(false);
            var o = GetByOwner(ev.Player); if (o != null) o.Owner = null;
        }

        private static void OnRound() => Reset();

        private static object SpawnSchematic(string name, Vector3 pos, Quaternion rot)
        {
            try
            {
                Type scType = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!a.FullName.StartsWith("ProjectMER")) continue;
                    scType = a.GetType("ProjectMER.Features.Objects.SchematicObject");
                    break;
                }
                if (scType == null) return null;

                Type st = Type.GetType("ProjectMER.Features.ObjectSpawner, ProjectMER");
                if (st == null) return null;

                var m = st.GetMethod("TrySpawnSchematic",
                    BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(string), typeof(Vector3), typeof(Quaternion), scType.MakeByRefType() }, null);
                if (m == null) return null;

                object[] a2 = { name, pos, rot, null };
                return (bool)m.Invoke(null, a2) ? a2[3] : null;
            }
            catch (Exception e) { Log.Warn($"[FPV] SpawnSchematic: {e.Message}"); return null; }
        }
    }
}