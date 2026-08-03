namespace EventHUD.SpecItems
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using EventHUD.Hud;
    using Exiled.API.Enums;
    using Exiled.API.Features;
    using Exiled.API.Features.Attributes;
    using Exiled.API.Features.Doors;
    using Exiled.API.Features.Items;
    using Exiled.API.Features.Spawn;
    using Exiled.CustomItems.API.Features;
    using Exiled.Events.EventArgs.Player;
    using UnityEngine;

    [CustomItem(ItemType.GunCOM15)]
    public sealed class BreakerGun : CustomWeapon
    {
        private readonly Dictionary<ushort, bool> aiming = new Dictionary<ushort, bool>();

        public override uint Id { get; set; } = 1;

        public override string Name { get; set; } = "Ломатор";

        public override string Description { get; set; } = "Без прицела ломает двери. С прицелом восстанавливает - стрелять в терминал у двери.";

        public override ItemType Type { get; set; } = ItemType.GunCOM15;

        public override float Weight { get; set; } = 0.6f;

        public override float Damage { get; set; } = 0f;

        public override byte ClipSize { get; set; } = 24;

        public override SpawnProperties SpawnProperties { get; set; } = new SpawnProperties
        {
            Limit = 0,
            DynamicSpawnPoints = new List<DynamicSpawnPoint>(),
        };

        public float RayDistance { get; set; } = 70f;

        public float DoorHitRadius { get; set; } = 2.5f;

        public float TerminalSearchRadius { get; set; } = 6f;

        protected override void OnReloading(ReloadingWeaponEventArgs ev)
        {
            ev.IsAllowed = false;
        }

        public void OnAimingDownSight(AimingDownSightEventArgs ev)
        {
            if (ev.Firearm is null)
                return;

            aiming[ev.Firearm.Serial] = ev.AdsIn;
        }

        protected override void OnShooting(ShootingEventArgs ev)
        {
            base.OnShooting(ev);

            Player player = ev.Player;

            if (player is null)
                return;

            if (ev.Firearm != null)
                ev.Firearm.MagazineAmmo = ClipSize;

            bool ads = false;

            if (!(ev.Firearm is null) && aiming.TryGetValue(ev.Firearm.Serial, out bool stored))
                ads = stored;

            Vector3 origin = player.CameraTransform.position;
            Vector3 direction = player.CameraTransform.forward;

            RaycastHit hit;

            if (!Physics.Raycast(origin, direction, out hit, RayDistance))
            {
                HudNoticeService.Show(player, "Мимо", 1f, NoticePosition.Center);
                return;
            }

            float radius = ads ? TerminalSearchRadius : DoorHitRadius;
            Door door = FindNearestDoor(hit.point, radius);

            if (door is null)
            {
                HudNoticeService.Show(player, ads ? "Рядом нет двери для ремонта" : "Это не дверь", 1.5f, NoticePosition.Center);
                return;
            }

            BreakableDoor breakable = door as BreakableDoor;

            if (breakable is null)
            {
                HudNoticeService.Show(player, "Эту дверь трогать нельзя", 1.5f, NoticePosition.Center);
                SpecDebug.Log("ЛОМАТОР: " + door.Type + " не ломаемая, пропуск");
                return;
            }

            if (ads)
            {
                bool ok = TryRepair(breakable);
                HudNoticeService.Show(player, ok ? "<color=green>Дверь восстановлена</color>" : "<color=red>Не удалось восстановить</color>", 2f, NoticePosition.Center);
                SpecDebug.Log("ЛОМАТОР ремонт " + door.Type + ": " + (ok ? "успех" : "ошибка"));
            }
            else
            {
                try
                {
                    breakable.Break();
                    HudNoticeService.Show(player, "<color=orange>Дверь сломана</color>", 2f, NoticePosition.Center);
                    SpecDebug.Log("ЛОМАТОР сломал " + door.Type);
                }
                catch (Exception e)
                {
                    SpecDebug.Log("ЛОМАТОР ошибка Break: " + e.Message);
                }
            }
        }

        private static Door FindNearestDoor(Vector3 point, float maxDistance)
        {
            Door best = null;
            float bestDistance = float.MaxValue;

            foreach (Door door in Door.List)
            {
                if (door is null)
                    continue;

                float distance = Vector3.Distance(door.Position, point);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = door;
                }
            }

            if (bestDistance > maxDistance)
                return null;

            return best;
        }

        private static bool TryRepair(BreakableDoor door)
        {
            try
            {
                PropertyInfo property = door.GetType().GetProperty("IsDestroyed");

                if (!(property is null) && property.CanWrite)
                {
                    property.SetValue(door, false, null);
                    SpecDebug.Log("ЛОМАТОР: ремонт через Door.IsDestroyed");
                    return true;
                }

                MethodInfo repair = door.GetType().GetMethod("Repair", System.Type.EmptyTypes);

                if (!(repair is null))
                {
                    repair.Invoke(door, null);
                    SpecDebug.Log("ЛОМАТОР: ремонт через Door.Repair()");
                    return true;
                }

                object baseDoor = door.Base;

                if (baseDoor is null)
                    return false;

                System.Type baseType = baseDoor.GetType();
                PropertyInfo netProperty = baseType.GetProperty("NetworkIsDestroyed");

                if (!(netProperty is null) && netProperty.CanWrite)
                {
                    netProperty.SetValue(baseDoor, false, null);
                    SpecDebug.Log("ЛОМАТОР: ремонт через NetworkIsDestroyed");
                    return true;
                }

                FieldInfo field = baseType.GetField(
                    "_destroyed",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (!(field is null))
                {
                    field.SetValue(baseDoor, false);
                    SpecDebug.Log("ЛОМАТОР: ремонт через поле _destroyed");
                    return true;
                }

                SpecDebug.Log("ЛОМАТОР: путь ремонта не найден. Тип двери = " + baseType.FullName);
                return false;
            }
            catch (Exception e)
            {
                SpecDebug.Log("ЛОМАТОР ошибка ремонта: " + e);
                return false;
            }
        }
    }
}