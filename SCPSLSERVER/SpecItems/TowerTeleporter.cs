namespace EventHUD.SpecItems
{
    using System.Collections.Generic;
    using EventHUD.Hud;
    using MEC;
    using Exiled.API.Enums;
    using Exiled.API.Features;
    using Exiled.API.Features.Attributes;
    using Exiled.API.Features.Items;
    using Exiled.API.Features.Spawn;
    using Exiled.CustomItems.API.Features;
    using Exiled.Events.EventArgs.Player;
    using PlayerStatsSystem;
    using UnityEngine;

    [CustomItem(ItemType.GunCOM18)]
    public sealed class TowerTeleporter : CustomWeapon
    {
        private readonly HashSet<ushort> adsSerials = new HashSet<ushort>();

        public override uint Id { get; set; } = 2;

        public override string Name { get; set; } = "Телепортер";

        public override string Description { get; set; } = "Телепортирует игрока, в которого попали, в башню. 0 урона.";

        public override ItemType Type { get; set; } = ItemType.GunCOM18;

        public override float Weight { get; set; } = 0.6f;

        public override float Damage { get; set; } = 0f;

        public override byte ClipSize { get; set; } = 24;

        public override SpawnProperties SpawnProperties { get; set; } = new SpawnProperties
        {
            Limit = 0,
            DynamicSpawnPoints = new List<DynamicSpawnPoint>(),
        };

        public float RayDistance { get; set; } = 200f;

        public static void ResetState()
        {
        }

        public void OnAimingDownSight(AimingDownSightEventArgs ev)
        {
            if (ev.Firearm is null)
                return;

            if (ev.AdsIn)
                adsSerials.Add(ev.Firearm.Serial);
            else
                adsSerials.Remove(ev.Firearm.Serial);
        }

        protected override void OnShooting(ShootingEventArgs ev)
        {
            base.OnShooting(ev);

            Player shooter = ev.Player;

            if (shooter is null)
                return;

            Vector3 origin = shooter.CameraTransform.position;
            Vector3 direction = shooter.CameraTransform.forward;

            RaycastHit hit;

            if (!Physics.Raycast(origin, direction, out hit, RayDistance))
            {
                HudNoticeService.Show(shooter, "Мимо", 1f, NoticePosition.Center);
                return;
            }

            Player target = null;

            if (hit.collider != null)
            {
                // SCP:SL использует HitboxIdentity на хитбоксах игроков
                HitboxIdentity hitbox = hit.collider.GetComponentInParent<HitboxIdentity>();
                if (hitbox != null && hitbox.TargetHub != null)
                {
                    target = Player.Get(hitbox.TargetHub);
                }
                else
                {
                    ReferenceHub hub = hit.collider.GetComponentInParent<ReferenceHub>();
                    if (hub != null)
                        target = Player.Get(hub);
                }
            }

            if (target is null)
            {
                HudNoticeService.Show(shooter, "Попади в игрока", 1.5f, NoticePosition.Center);
                return;
            }

            if (target == shooter)
            {
                HudNoticeService.Show(shooter, "Нельзя телепортировать себя", 1.5f, NoticePosition.Center);
                return;
            }

            Vector3 towerPos = GetTowerPosition();

            if (towerPos == Vector3.zero)
            {
                HudNoticeService.Show(shooter, "<color=red>Не найдена точка башни</color>", 2f, NoticePosition.Center);
                SpecDebug.Log("ТЕЛЕПОРТЕР: не найдена точка башни");
                return;
            }

            // godmode на пару секунд, чтобы не убило при падении
            bool wasGod = target.IsGodModeEnabled;
            target.IsGodModeEnabled = true;
            target.Teleport(towerPos);
            Timing.CallDelayed(1.5f, () =>
            {
                if (target != null && target.IsConnected)
                    target.IsGodModeEnabled = wasGod;
            });

            HudNoticeService.Show(shooter, $"<color=lime>Игрок {target.Nickname} телепортирован в башню</color>", 2f, NoticePosition.Center);
            HudNoticeService.Show(target, "<color=orange>Вы телепортированы в башню администратором</color>", 3f, NoticePosition.Center);

            SpecDebug.Log($"ТЕЛЕПОРТЕР: {shooter.Nickname} -> {target.Nickname} в башню");
        }

        private static Vector3 GetTowerPosition()
        {
            foreach (Room room in Room.List)
            {
                if (room.Type == RoomType.Surface)
                    return room.Position + Vector3.up * 5f;
            }

            foreach (Room room in Room.List)
            {
                if (room.Name != null &&
                    (room.Name.Contains("Tower", System.StringComparison.OrdinalIgnoreCase) ||
                     room.Name.Contains("башн", System.StringComparison.OrdinalIgnoreCase)))
                {
                    return room.Position + Vector3.up * 3f;
                }
            }

            return new Vector3(0f, 100f, 0f);
        }
    }
}