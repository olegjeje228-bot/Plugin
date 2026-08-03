using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Items;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace EventHUD.FpvDrone
{
    public sealed class DroneInstance
    {
        private static readonly System.Random Rng = new System.Random();

        public int Id;
        public Player Owner;
        public Player Pilot;
        public Npc Dummy;
        public object Schematic;
        public object SoundPlayer;

        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;

        public float BatteryVoltage = 7.01f;
        public float Hp = 400f;
        public int Ammo = 5;
        public bool IsActive;
        public bool IsPiloting;
        public bool IsDestroyed;
        public bool IsDropping;

        public CoroutineHandle PhysicsHandle;
        public CoroutineHandle HudHandle;
        public CoroutineHandle SoundHandle;
        public CoroutineHandle CameraHandle;

        public Dictionary<int, bool> KeyStates = new Dictionary<int, bool>();

        public RoleTypeId OriginalRole;
        public Vector3 OriginalPosition;
        public float OriginalHealth;
        public List<ItemType> OriginalInventory = new List<ItemType>();
        public Dictionary<AmmoType, ushort> OriginalAmmo = new Dictionary<AmmoType, ushort>();
        public string OriginalName;
        public string OriginalCustomInfo;

        private int _selfDestructPresses;
        private float _selfDestructTimer;
        private bool _confirmingDestruct;

        private const float MaxHSpeed = 33.3f;
        private const float MaxVSpeed = 8f;
        private const float Accel = 15f;
        private const float Decel = 5f;
        private const float YawSpeed = 120f;
        private const float MaxTiltFwd = 45f;
        private const float MaxTiltSide = 30f;
        private const float TiltReturn = 90f;
        private const float Elasticity = 0.35f;
        private const float DmgPerMps = 15f;
        private const float G = 9.81f;
        private const float Range = 400f;
        private const float Tick = 0.05f;
        private const float HudTick = 0.2f;

        private float _tiltX;
        private float _tiltZ;
        private bool _grounded;
        private float _doorCd;

        public void StartPhysics()
        {
            IsActive = true;
            PhysicsHandle = Timing.RunCoroutine(PhysicsLoop());
            SoundHandle = Timing.RunCoroutine(SoundLoop());
        }

        public void StartPiloting(Player player)
        {
            if (IsPiloting || IsDestroyed) return;
            Pilot = player;
            IsPiloting = true;
            _grounded = false;

            SaveState(player);
            SpawnDummy(player);
            EnterCamera(player);

            HudHandle = Timing.RunCoroutine(HudLoop());
            CameraHandle = Timing.RunCoroutine(CamLoop());
            FpvDroneSystem.SendDroneKeybinds(player);
        }

        public void StopPiloting(bool restore = true)
        {
            if (!IsPiloting) return;
            IsPiloting = false;
            _confirmingDestruct = false;
            KeyStates.Clear();

            Timing.KillCoroutines(HudHandle);
            Timing.KillCoroutines(CameraHandle);

            if (Pilot != null && Pilot.IsConnected)
            {
                Pilot.ClearBroadcasts();
                if (restore) RestoreState(Pilot);
            }

            KillDummy();
            FpvDroneSystem.RestoreKeybinds(Pilot);
            Pilot = null;
        }

        public void Destroy(bool explode = true)
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            StopPiloting(true);

            Timing.KillCoroutines(PhysicsHandle);
            Timing.KillCoroutines(SoundHandle);

            if (explode)
            {
                try
                {
                    var g = (ExplosiveGrenade)Item.Create(ItemType.GrenadeHE);
                    if (g != null) { g.FuseTime = 0.05f; g.SpawnActive(Position); }
                }
                catch { }
            }

            StopSound();
            DestroySchem();
            FpvDroneSystem.RemoveDrone(this);
        }

        public void TakeDamage(float d)
        {
            Hp -= d;
            if (Hp <= 0f) Destroy(true);
        }

        public void OnKey(int id, bool pressed)
        {
            KeyStates[id] = pressed;

            if (id == FpvDroneSystem.KeyExit && pressed)
            { StopPiloting(true); return; }

            if (id == FpvDroneSystem.KeyDrop && pressed && Ammo > 0 && !IsDropping)
            { IsDropping = true; Timing.RunCoroutine(DropSeq()); return; }

            if (id == FpvDroneSystem.KeyDestruct && pressed)
                HandleDestruct();
        }

        private void HandleDestruct()
        {
            if (!_confirmingDestruct)
            {
                _confirmingDestruct = true;
                _selfDestructPresses = 1;
                _selfDestructTimer = 3f;
                if (Pilot != null)
                {
                    Pilot.ClearBroadcasts();
                    Pilot.Broadcast(4, "<color=red>SELF-DESTRUCT: Press R 2 more times to confirm</color>",
                        Broadcast.BroadcastFlags.Normal, true);
                }
                return;
            }
            _selfDestructPresses++;
            if (_selfDestructPresses >= 3) Destroy(true);
        }

        private IEnumerator<float> PhysicsLoop()
        {
            while (!IsDestroyed)
            {
                yield return Timing.WaitForSeconds(Tick);
                if (IsDestroyed) yield break;

                UpdateBat(Tick);

                if (BatteryVoltage <= 0f)
                {
                    BatteryVoltage = 0f;
                    if (IsPiloting) StopPiloting(true);
                    Velocity += Vector3.down * G * Tick;
                    Position += Velocity * Tick;
                    if (GroundCheck())
                    {
                        if (Mathf.Abs(Velocity.y) > 6f) { Destroy(true); yield break; }
                        Velocity = Vector3.zero; _grounded = true;
                    }
                    SyncSchem(); continue;
                }

                if (_confirmingDestruct)
                {
                    _selfDestructTimer -= Tick;
                    if (_selfDestructTimer <= 0f) { _confirmingDestruct = false; _selfDestructPresses = 0; }
                }

                _doorCd -= Tick;

                if (!IsPiloting)
                {
                    if (!_grounded)
                    {
                        Velocity += Vector3.down * G * Tick;
                        Position += Velocity * Tick;
                        if (GroundCheck())
                        {
                            if (Mathf.Abs(Velocity.y) > 6f) TakeDamage(Mathf.Abs(Velocity.y) * DmgPerMps);
                            Velocity = Vector3.zero; _grounded = true;
                        }
                    }
                    SyncSchem(); continue;
                }

                if (Pilot != null && Pilot.IsConnected &&
                    Vector3.Distance(Position, OriginalPosition) > Range)
                { StopPiloting(true); continue; }

                Vector3 inp = GetInput();
                float yaw = GetYaw();

                Rotation = Quaternion.Euler(0f, yaw * YawSpeed * Tick, 0f) * Rotation;
                Vector3 world = Rotation * inp;
                Velocity += world * Accel * Tick;

                if (inp.x == 0f && inp.z == 0f)
                {
                    var h = new Vector3(Velocity.x, 0f, Velocity.z);
                    h = Vector3.MoveTowards(h, Vector3.zero, Decel * Tick);
                    Velocity = new Vector3(h.x, Velocity.y, h.z);
                }

                if (inp.y == 0f)
                    Velocity = new Vector3(Velocity.x,
                        Mathf.MoveTowards(Velocity.y, 0f, Decel * 0.5f * Tick), Velocity.z);

                if (!Key(FpvDroneSystem.KeyUp) && !Key(FpvDroneSystem.KeyDown))
                    Velocity = new Vector3(Velocity.x, Velocity.y - G * 0.3f * Tick, Velocity.z);

                var hv = new Vector3(Velocity.x, 0f, Velocity.z);
                if (hv.magnitude > MaxHSpeed)
                { hv = hv.normalized * MaxHSpeed; Velocity = new Vector3(hv.x, Velocity.y, hv.z); }
                Velocity = new Vector3(Velocity.x, Mathf.Clamp(Velocity.y, -MaxVSpeed, MaxVSpeed), Velocity.z);

                float tx = 0f, tz = 0f;
                if (inp.z > 0f) tx = -MaxTiltFwd;
                else if (inp.z < 0f) tx = MaxTiltFwd * 0.5f;
                if (inp.x > 0f) tz = -MaxTiltSide;
                else if (inp.x < 0f) tz = MaxTiltSide;
                if (Key(FpvDroneSystem.KeyUp) && inp.z > 0f) tx = -MaxTiltFwd;

                _tiltX = Mathf.MoveTowards(_tiltX, tx, TiltReturn * Tick);
                _tiltZ = Mathf.MoveTowards(_tiltZ, tz, TiltReturn * Tick);
                Rotation = Quaternion.Euler(_tiltX, Rotation.eulerAngles.y, _tiltZ);

                Vector3 np = Position + Velocity * Tick;
                if (WallHit(Position, np, out Vector3 hp, out Vector3 hn))
                {
                    float spd = Velocity.magnitude;
                    Velocity = Vector3.Reflect(Velocity, hn) * Elasticity;
                    TakeDamage(spd * DmgPerMps);
                    Position = hp + hn * 0.15f;
                    HitPlayers(hp, spd);
                    DoorHit(hp);
                }
                else Position = np;

                if (GroundCheck())
                {
                    float imp = Mathf.Abs(Velocity.y);
                    if (imp > 2f)
                    {
                        Velocity = new Vector3(Velocity.x, -Velocity.y * Elasticity, Velocity.z);
                        TakeDamage(imp * DmgPerMps * 0.5f);
                    }
                    else { Velocity = new Vector3(Velocity.x, 0f, Velocity.z); _grounded = true; }
                }
                else _grounded = false;

                SyncSchem();
                TeslaCheck();
            }
        }

        private void UpdateBat(float dt)
        {
            if (!IsActive || BatteryVoltage <= 0f) return;
            float drain;
            if (!IsPiloting) drain = _grounded ? 0.02f : 0.04f;
            else if (Key(FpvDroneSystem.KeyUp) && HInput()) drain = 0.08f;
            else if (Key(FpvDroneSystem.KeyUp)) drain = 0.05f;
            else if (Key(FpvDroneSystem.KeyDown)) drain = 0.04f;
            else if (Velocity.sqrMagnitude > 1f) drain = 0.04f;
            else drain = 0.02f;
            drain *= (1f + Ammo * 0.012f);
            BatteryVoltage -= drain * dt;
        }

        private bool HInput() => Key(FpvDroneSystem.KeyForward) || Key(FpvDroneSystem.KeyBack)
            || Key(FpvDroneSystem.KeyLeft) || Key(FpvDroneSystem.KeyRight);

        private Vector3 GetInput()
        {
            Vector3 f = Vector3.zero;
            if (Key(FpvDroneSystem.KeyForward)) f.z += 1f;
            if (Key(FpvDroneSystem.KeyBack)) f.z -= 1f;
            if (Key(FpvDroneSystem.KeyRight)) f.x += 1f;
            if (Key(FpvDroneSystem.KeyLeft)) f.x -= 1f;
            if (Key(FpvDroneSystem.KeyUp)) f.y += 1f;
            if (Key(FpvDroneSystem.KeyDown)) f.y -= 1f;
            return f.sqrMagnitude > 0f ? f.normalized : Vector3.zero;
        }

        private float GetYaw()
        {
            float y = 0f;
            if (Key(FpvDroneSystem.KeyYawRight)) y += 1f;
            if (Key(FpvDroneSystem.KeyYawLeft)) y -= 1f;
            return y;
        }

        private bool Key(int id) => KeyStates.TryGetValue(id, out bool v) && v;

        private bool WallHit(Vector3 from, Vector3 to, out Vector3 hp, out Vector3 hn)
        {
            hp = to; hn = Vector3.up;
            Vector3 d = to - from; float dist = d.magnitude;
            if (dist < 0.001f) return false; d.Normalize();
            if (Physics.BoxCast(from, new Vector3(0.4f, 0.07f, 0.017f), d, out RaycastHit h, Rotation, dist))
            {
                if (h.collider == null || h.collider.isTrigger) return false;
                if (h.collider.GetComponentInParent<CharacterController>() != null) return false;
                hp = h.point; hn = h.normal; return true;
            }
            return false;
        }

        private bool GroundCheck()
        {
            if (Physics.Raycast(Position, Vector3.down, out RaycastHit h, 0.15f))
            {
                if (h.collider != null && !h.collider.isTrigger)
                { Position = new Vector3(Position.x, h.point.y + 0.1f, Position.z); return true; }
            }
            return false;
        }

        private void TeslaCheck()
        {
            try
            {
                foreach (var go in GameObject.FindGameObjectsWithTag("TeslaGate"))
                {
                    if (go == null) continue;
                    if (Vector3.Distance(Position, go.transform.position) < 4f)
                    { if (IsPiloting) StopPiloting(true); Velocity = Vector3.zero; BatteryVoltage = 0f; return; }
                }
            }
            catch { }
        }

        private void DoorHit(Vector3 pt)
        {
            if (_doorCd > 0f) return;
            try
            {
                foreach (var go in GameObject.FindGameObjectsWithTag("Door"))
                {
                    if (go == null) continue;
                    var dp = go.transform.position;
                    if (Vector3.Distance(dp, pt) > 2f) continue;
                    if (!go.activeInHierarchy) continue;
                    try
                    {
                        var doorComponent = go.GetComponent(Type.GetType("Interactables.Interobjects.Door, Assembly-CSharp"));
                        if (doorComponent == null) continue;
                        var isLockedProp = doorComponent.GetType().GetProperty("IsLocked");
                        if (isLockedProp != null && (bool)isLockedProp.GetValue(doorComponent)) continue;
                        var isOpenProp = doorComponent.GetType().GetProperty("IsOpen");
                        if (isOpenProp != null && !(bool)isOpenProp.GetValue(doorComponent))
                        { isOpenProp.SetValue(doorComponent, true); _doorCd = 1f; } break;
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void HitPlayers(Vector3 pt, float spd)
        {
            float dmg = spd * 3.6f / 5f;
            if (dmg < 1f) return;
            foreach (Player p in Player.List)
            {
                if (p == null || !p.IsAlive || p == Pilot) continue;
                if (Vector3.Distance(p.Position, pt) > 2f) continue;
                try { p.Hurt(dmg, DamageType.Crushed); FpvDroneSystem.ShowHitmarker(Pilot); } catch { }
            }
        }

        private IEnumerator<float> DropSeq()
        {
            PlayAnim("Dropping");
            yield return Timing.WaitForSeconds(0.7f);
            if (IsDestroyed) yield break;

            Vector3 loc = new Vector3(0.3167038f, 0.3081999f, 1.249203f);
            Vector3 wp = Position + Rotation * loc;
            int n = Ammo; Ammo = 0;

            for (int i = 0; i < n; i++)
            {
                if (IsDestroyed) break;
                var e = Rotation.eulerAngles;
                if (e.x > 90f && e.x < 270f) break;
                try
                {
                    var g = (ExplosiveGrenade)Item.Create(ItemType.GrenadeHE);
                    if (g == null) continue;
                    g.FuseTime = 3f;
                    g.SpawnActive(wp + Vector3.down * (i * 0.1f));
                    try { foreach (var rb in g.Base.GetComponentsInChildren<Rigidbody>(true))
                    { rb.velocity = Velocity + Vector3.down * 2f; rb.useGravity = true; break; } } catch { }
                }
                catch { }
                yield return Timing.WaitForSeconds(0.08f);
            }
            yield return Timing.WaitForSeconds(1.3f);
            IsDropping = false;
        }

        private IEnumerator<float> HudLoop()
        {
            while (IsPiloting && !IsDestroyed)
            {
                yield return Timing.WaitForSeconds(HudTick);
                if (Pilot == null || !Pilot.IsConnected || !IsPiloting) yield break;
                if (_confirmingDestruct) continue;
                float spd = Velocity.magnitude * 3.6f;
                Pilot.ClearBroadcasts();
                Pilot.Broadcast(1,
                    $"<size=55><space=55><indent=-20%>SPEED: {spd:0.0} km/h<indent=62%>BATTERY<b> {BatteryVoltage:0.00}V</b> AMMO: {Ammo}",
                    Broadcast.BroadcastFlags.Normal, true);
            }
        }

        private IEnumerator<float> CamLoop()
        {
            while (IsPiloting && !IsDestroyed)
            {
                yield return Timing.WaitForSeconds(Tick);
                if (Pilot == null || !Pilot.IsConnected || !IsPiloting) yield break;
                Vector3 cp = Position + Rotation * new Vector3(0.3140654f, 0.34165f, 1.1101f);
                Pilot.Position = cp;
                Pilot.Rotation = Rotation * Quaternion.Euler(0f, -180f, 0f);
            }
        }

        private IEnumerator<float> SoundLoop()
        {
            yield return Timing.WaitForSeconds(0.2f);
            SoundPlayer = MakeSound();
            if (SoundPlayer == null) yield break;
            var sp = SoundPlayer.GetType().GetMethod("SetSpeakerPosition", new[] { typeof(string), typeof(Vector3) });
            while (!IsDestroyed)
            {
                yield return Timing.WaitForSeconds(0.15f);
                if (SoundPlayer == null || IsDestroyed) yield break;
                try { sp?.Invoke(SoundPlayer, new object[] { "Main", Position }); } catch { yield break; }
            }
        }

        private object MakeSound()
        {
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!a.FullName.StartsWith("AudioPlayer")) continue;
                    var pt = a.GetType("AudioPlayer"); if (pt == null) break;
                    var cm = pt.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                        .FirstOrDefault(m => m.Name == "CreateOrGet"); if (cm == null) break;
                    var ps = cm.GetParameters();
                    object[] ar = new object[ps.Length]; ar[0] = $"FPV-{Id}";
                    for (int i = 1; i < ps.Length; i++)
                    { var tp = ps[i].ParameterType;
                      if (tp == typeof(bool)) ar[i] = ps[i].Name == "sendSoundGlobally";
                      else if (tp == typeof(byte)) ar[i] = (byte)255; else ar[i] = null; }
                    object pl = cm.Invoke(null, ar); if (pl == null) break;
                    var t = pl.GetType();
                    t.GetProperty("SendSoundGlobally")?.SetValue(pl, true);
                    t.GetProperty("DestroyWhenAllClipsPlayed")?.SetValue(pl, false);
                    t.GetMethod("AddSpeaker", new[] { typeof(string), typeof(Vector3), typeof(float),
                        typeof(bool), typeof(float), typeof(float) })
                        ?.Invoke(pl, new object[] { "Main", Position, 2f, true, 3f, 25f });
                    t.GetMethod("AddClip", new[] { typeof(string), typeof(float), typeof(bool), typeof(bool) })
                        ?.Invoke(pl, new object[] { "dron", 2f, true, true });
                    return pl;
                }
            } catch (Exception e) { Log.Warn($"[FPV] Sound: {e.Message}"); }
            return null;
        }

        private void StopSound()
        { if (SoundPlayer != null) { Audio.SoundService.StopHandle(SoundPlayer); SoundPlayer = null; } }

        private void SaveState(Player p)
        {
            OriginalRole = p.Role.Type; OriginalPosition = p.Position;
            OriginalHealth = p.Health; OriginalName = p.DisplayNickname;
            OriginalCustomInfo = p.CustomInfo;
            OriginalInventory.Clear();
            foreach (var i in p.Items) OriginalInventory.Add(i.Type);
            OriginalAmmo.Clear();
            foreach (AmmoType a in Enum.GetValues(typeof(AmmoType)))
            { if (a == AmmoType.None) continue; OriginalAmmo[a] = p.GetAmmo(a); }
        }

        private void SpawnDummy(Player p)
        {
            try
            {
                string nm = p.DisplayNickname ?? p.Nickname;
                Dummy = Npc.Spawn(nm, p.Role.Type);
                if (Dummy == null) return;
                Dummy.Position = p.Position;
                Timing.CallDelayed(0.5f, () =>
                {
                    if (Dummy == null || !Dummy.IsConnected) return;
                    Dummy.Health = OriginalHealth; Dummy.CustomInfo = OriginalCustomInfo;
                    Dummy.DisplayNickname = nm.Length < 30 ? nm + " [FPV DRONE]" : "[FPV DRONE]";
                    foreach (var it in OriginalInventory) { try { Dummy.AddItem(it); } catch { } }
                    try
                    {
                        var r = Dummy.Items.FirstOrDefault(i => i.Type == ItemType.Radio);
                        if (r != null) Dummy.CurrentItem = r;
                        else { var nr = Dummy.AddItem(ItemType.Radio); if (nr != null) Dummy.CurrentItem = nr; }
                    } catch { }
                    Dummy.IsGodModeEnabled = true;
                });
            } catch (Exception e) { Log.Warn($"[FPV] Dummy: {e.Message}"); }
        }

        private void EnterCamera(Player p)
        {
            p.Role.Set(RoleTypeId.Tutorial, SpawnReason.ForceClass, RoleSpawnFlags.None);
            Timing.CallDelayed(0.3f, () =>
            {
                if (p == null || !p.IsConnected) return;
                p.ClearInventory();
                p.Scale = new Vector3(0.0193f, 0.0193f, 0.0193f);
                p.IsGodModeEnabled = true;
                p.ChangeEffectIntensity(EffectType.Invisible, 255);
                try { p.ChangeEffectIntensity(EffectType.NightVision, 5); } catch { }
                try { p.ChangeEffectIntensity(EffectType.FogControl, 11); } catch { }
                Vector3 cp = Position + Rotation * new Vector3(0.3140654f, 0.34165f, 1.1101f);
                p.Position = cp;
                p.Rotation = Rotation * Quaternion.Euler(0f, -180f, 0f);
            });
        }

        private void RestoreState(Player p)
        {
            p.IsGodModeEnabled = false; p.Scale = Vector3.one;
            p.ChangeEffectIntensity(EffectType.Invisible, 0);
            try { p.ChangeEffectIntensity(EffectType.NightVision, 0); } catch { }
            try { p.ChangeEffectIntensity(EffectType.FogControl, 0); } catch { }
            p.Role.Set(OriginalRole, SpawnReason.ForceClass, RoleSpawnFlags.None);
            Timing.CallDelayed(0.3f, () =>
            {
                if (p == null || !p.IsConnected) return;
                p.Position = OriginalPosition; p.Health = OriginalHealth;
                p.ClearInventory();
                foreach (var t in OriginalInventory) { try { p.AddItem(t); } catch { } }
                foreach (var kv in OriginalAmmo) { try { p.SetAmmo(kv.Key, kv.Value); } catch { } }
                p.DisplayNickname = OriginalName; p.CustomInfo = OriginalCustomInfo;
            });
        }

        private void KillDummy()
        { if (Dummy != null) { try { Dummy.Destroy(); } catch { } Dummy = null; } }

        private void SyncSchem()
        {
            if (Schematic == null) return;
            try { var t = Schematic.GetType();
                t.GetProperty("Position")?.SetValue(Schematic, Position);
                t.GetProperty("Rotation")?.SetValue(Schematic, Rotation); } catch { }
        }

        private void DestroySchem()
        {
            if (Schematic == null) return;
            try { Schematic.GetType().GetMethod("Destroy", Type.EmptyTypes)?.Invoke(Schematic, null); } catch { }
            Schematic = null;
        }

        private void PlayAnim(string n)
        {
            if (Schematic == null) return;
            try
            {
                var t = Schematic.GetType();
                var m = t.GetMethod("PlayAnimation", new[] { typeof(string) });
                if (m != null) { m.Invoke(Schematic, new object[] { n }); return; }
                var p = t.GetProperty("AnimationController");
                if (p != null) { var c = p.GetValue(Schematic);
                    c?.GetType().GetMethod("Play", new[] { typeof(string) })?.Invoke(c, new object[] { n }); }
            } catch { }
        }

        public void PlayFlyingAnimation() => PlayAnim("Flying");

        public void OnShot(Player shooter, float dmg)
        { TakeDamage(dmg); FpvDroneSystem.ShowHitmarker(shooter); }
    }
}