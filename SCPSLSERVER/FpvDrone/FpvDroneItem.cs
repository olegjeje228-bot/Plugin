using System.Collections.Generic;
using Exiled.API.Features;
using Exiled.API.Features.Attributes;
using Exiled.API.Features.Items;
using Exiled.API.Features.Spawn;
using Exiled.CustomItems.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;
using UnityEngine;

namespace EventHUD.FpvDrone
{
    [CustomItem(ItemType.Radio)]
    public sealed class FpvDroneItem : CustomItem
    {
        public override uint Id { get; set; } = 6;
        public override string Name { get; set; } = "FPV-DRONE";
        public override string Description { get; set; } = "FPV Drone Controller (Radio)";
        public override ItemType Type { get; set; } = ItemType.Radio;
        public override float Weight { get; set; } = 0.5f;
        public override SpawnProperties SpawnProperties { get; set; } = new SpawnProperties
        {
            Limit = 0,
            DynamicSpawnPoints = new List<DynamicSpawnPoint>(),
        };

        protected override void SubscribeEvents()
        {
            Exiled.Events.Handlers.Player.UsingItem += OnUsingItem;
            base.SubscribeEvents();
        }

        protected override void UnsubscribeEvents()
        {
            Exiled.Events.Handlers.Player.UsingItem -= OnUsingItem;
            base.UnsubscribeEvents();
        }

        protected override void OnAcquired(Player player, Item item, bool displayMessage)
        {
            base.OnAcquired(player, item, displayMessage);

            if (player == null || item == null || !Check(item))
                return;

            Timing.CallDelayed(0.5f, () =>
            {
                if (player == null || !player.IsConnected || !player.IsAlive)
                    return;

                if (FpvDroneSystem.GetByOwner(player) != null)
                    return;

                if (FpvDroneSystem.ActiveCount >= 40)
                    return;

                Vector3 origin = player.CameraTransform.position;
                Vector3 direction = player.CameraTransform.forward.normalized;
                Vector3 spawnPosition = origin + direction * 3f;

                if (Physics.Raycast(origin, direction, out RaycastHit wallHit, 3.5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    spawnPosition = wallHit.point - direction * 0.5f;
                }

                if (Physics.Raycast(spawnPosition + Vector3.up * 2f, Vector3.down,
                    out RaycastHit floorHit, 10f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
                {
                    spawnPosition.y = floorHit.point.y + 0.2f;
                }

                Quaternion rotation = Quaternion.Euler(
                    0f,
                    player.CameraTransform.eulerAngles.y,
                    0f);

                FpvDroneSystem.SpawnDrone(player, spawnPosition, rotation);
            });
        }

        private void OnUsingItem(UsingItemEventArgs ev)
        {
            if (ev == null || ev.Player == null || ev.Item == null)
                return;

            if (!Check(ev.Item))
                return;

            ev.IsAllowed = false;

            Player player = ev.Player;
            DroneInstance drone = FpvDroneSystem.GetByOwner(player);

            if (drone == null || drone.IsDestroyed)
                return;

            if (drone.IsPiloting)
            {
                drone.StopPiloting(true);
                return;
            }

            if (Vector3.Distance(player.Position, drone.Position) > 400f)
                return;

            drone.StartPiloting(player);
        }
    }
}