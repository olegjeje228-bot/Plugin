namespace EventHUD.SpecItems
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using EventHUD.Hud;
    using Exiled.API.Enums;
    using Exiled.API.Features;
    using Exiled.API.Features.Attributes;
    using Exiled.API.Features.Items;
    using Exiled.API.Features.Pickups.Projectiles;
    using Exiled.API.Features.Spawn;
    using Exiled.CustomItems.API.Features;
    using Exiled.Events.EventArgs.Player;
    using InventorySystem.Items.MicroHID.Modules;
    using MEC;
    using UnityEngine;

    [CustomItem(ItemType.MicroHID)]
    public sealed class GrenadeLauncher : CustomItem
    {
        private const float GrenadeSpeed = 10f;
        private const float FastInterval = 0.2f;
        private const float SlowInterval = 0.5f;
        private const float FastPhaseDuration = 5f;

        private static readonly Dictionary<string, float> SessionStart = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> NextShot = new Dictionary<string, float>();

        private readonly HashSet<string> firingNow = new HashSet<string>();

        private CoroutineHandle loop;

        public override uint Id { get; set; } = 3;

        public override ItemType Type { get; set; } = ItemType.MicroHID;

        public override string Name { get; set; } = "Гранатомёт";

        public override string Description { get; set; } = "МикроХИД, стреляющий гранатами";

        public override float Weight { get; set; } = 25f;

        public override SpawnProperties SpawnProperties { get; set; } = new SpawnProperties();

        protected override void SubscribeEvents()
        {
            Exiled.Events.Handlers.Player.ChangingMicroHIDState += OnChangingPhase;
            Exiled.Events.Handlers.Player.Hurting += OnHurting;
            loop = Timing.RunCoroutine(FireLoop());
            base.SubscribeEvents();
            SpecDebug.Log("ГРАНАТОМЁТ: SubscribeEvents");
        }

        protected override void UnsubscribeEvents()
        {
            Exiled.Events.Handlers.Player.ChangingMicroHIDState -= OnChangingPhase;
            Exiled.Events.Handlers.Player.Hurting -= OnHurting;
            Timing.KillCoroutines(loop);
            firingNow.Clear();
            base.UnsubscribeEvents();
            SpecDebug.Log("ГРАНАТОМЁТ: UnsubscribeEvents");
        }

        protected override void OnAcquired(Player player, Item item, bool displayMessage)
        {
            base.OnAcquired(player, item, displayMessage);

            if (player == null || item == null)
                return;

            ushort serial = item.Serial;

            Timing.CallDelayed(0.5f, () =>
            {
                try
                {
                    if (player == null || !player.IsConnected)
                        return;

                    SpecDebug.Log("МИКРОХИД: у " + player.Nickname + " гранатомётов в инвентаре");
                }
                catch (System.Exception e)
                {
                    SpecDebug.Log("МИКРОХИД лимит err: " + e.Message);
                }
            });
        }

        private void OnChangingPhase(ChangingMicroHIDStateEventArgs ev)
        {
            if (ev.Player == null || ev.MicroHID == null || !Check(ev.MicroHID))
                return;

            string id = ev.Player.UserId ?? ev.Player.Nickname;
            SpecDebug.Log("МИКРОХИД " + ev.Player.Nickname + " фаза -> " + ev.NewPhase);

            if (ev.NewPhase == MicroHidPhase.Firing)
            {
                float now = Time.time;
                firingNow.Add(id);
                SessionStart[id] = now;
                NextShot[id] = now;
            }
            else
            {
                firingNow.Remove(id);
            }
        }

        private void OnHurting(HurtingEventArgs ev)
        {
            if (ev.Attacker == null || ev.DamageHandler == null)
                return;

            if (ev.DamageHandler.Type != DamageType.MicroHid)
                return;

            if (!Check(ev.Attacker.CurrentItem))
                return;

            ev.IsAllowed = false;
        }

        private IEnumerator<float> FireLoop()
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(0.05f);

                try
                {
                    Tick();
                }
                catch (Exception e)
                {
                    SpecDebug.Log("МИКРОХИД loop err: " + e.Message);
                }
            }
        }

        private void Tick()
        {
            float now = Time.time;

            if (firingNow.Count == 0)
                return;

            List<string> ids = firingNow.ToList();

            foreach (string id in ids)
            {
                Player player = Player.List.FirstOrDefault(p => (p.UserId ?? p.Nickname) == id);

                if (player == null || !player.IsAlive || !Check(player.CurrentItem))
                {
                    firingNow.Remove(id);
                    continue;
                }

                MicroHid micro = player.CurrentItem as MicroHid;

                if (micro != null)
                {
                    try { micro.Energy = 1f; } catch { }
                }

                float next;
                NextShot.TryGetValue(id, out next);

                if (now < next)
                    continue;

                float started;
                SessionStart.TryGetValue(id, out started);

                float interval = now - started <= FastPhaseDuration ? FastInterval : SlowInterval;
                NextShot[id] = now + interval;
                FireGrenade(player);
            }
        }

        private void FireGrenade(Player player)
        {
            try
            {
                Projectile projectile = player.ThrowGrenade(ProjectileType.FragGrenade, false).Projectile;

                TimeGrenadeProjectile timed = projectile as TimeGrenadeProjectile;

                if (timed != null)
                    timed.FuseTime = 3f;

                Vector3 direction = player.CameraTransform.forward;
                projectile.Position = player.CameraTransform.position + direction * 0.7f;

                Rigidbody body = projectile.GameObject.GetComponent<Rigidbody>();

                if (body != null)
                {
                    body.velocity = direction * GrenadeSpeed;
                    body.angularVelocity = Vector3.zero;
                }

                projectile.GameObject.AddComponent<NoPhysicsProjectile>();
                projectile.GameObject.AddComponent<SpecGrenadeTag>();
            }
            catch (Exception e)
            {
                SpecDebug.Log("МИКРОХИД выстрел err: " + e.Message);
            }
        }
    }
}