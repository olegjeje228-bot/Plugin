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
        { Limit = 0, DynamicSpawnPoints = new List<DynamicSpawnPoint>() };

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
            if (player == null) return;
            Timing.CallDelayed(0.5f, () =>
            {
                if (player == null || !player.IsConnected) return;
                if (FpvDroneSystem.GetByOwner(player) != null) return;
                if (FpvDroneSystem.ActiveCount >= 40) return;

                Vector3 sp = player.Position + player.CameraTransform.forward * 3f;
                if (Physics.Raycast(player.CameraTransform.position, player.CameraTransform.forward, out RaycastHit wh, 3.5f))
                    sp = wh.point - player.CameraTransform.forward * 0.5f;
                if (Physics.Raycast(sp + Vector3.up * 2f, Vector3.down, out RaycastHit gh, 10f))
                    sp.y = gh.point.y + 0.2f;

                Quaternion rot = Quaternion.Euler(0f, player.CameraTransform.rotation.eulerAngles.y, 0f);
                FpvDroneSystem.SpawnDrone(player, sp, rot);
            });
        }

        private void OnUsingItem(UsingItemEventArgs ev)
        {
            if (ev == null || ev.Player == null) return;
            if (!Check(ev.Player.CurrentItem)) return;
            ev.IsAllowed = false;
            Player p = ev.Player;

            var d = FpvDroneSystem.GetByOwner(p);
            if (d == null || d.IsDestroyed || d.IsPiloting) return;
            if (Vector3.Distance(p.Position, d.Position) > 400f) return;
            d.StartPiloting(p);
        }
    }
}