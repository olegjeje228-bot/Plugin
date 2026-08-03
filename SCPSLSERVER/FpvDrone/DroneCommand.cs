using System;
using CommandSystem;
using Exiled.API.Features;

namespace EventHUD.FpvDrone
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class DroneCommand : ICommand
    {
        public string Command => "drone";
        public string[] Aliases => Array.Empty<string>();
        public string Description => "drone give/delete/remove/list";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count == 0) { response = "drone give <id> | drone delete all | drone remove <id> | drone list"; return false; }
            switch (arguments.At(0).ToLowerInvariant())
            {
                case "give":
                    if (arguments.Count < 2) { response = "drone give <player_id>"; return false; }
                    var t = Player.Get(arguments.At(1));
                    if (t == null) { response = "Player not found."; return false; }
                    if (FpvDroneSystem.ActiveCount >= 40) { response = "Max 40 drones."; return false; }
                    new FpvDroneItem().Give(t);
                    response = $"FPV-DRONE -> {t.Nickname}"; return true;
                case "delete":
                    if (arguments.Count < 2 || arguments.At(1).ToLowerInvariant() != "all")
                    { response = "drone delete all"; return false; }
                    int c = FpvDroneSystem.ActiveCount; FpvDroneSystem.DestroyAll();
                    response = $"{c} drones destroyed."; return true;
                case "remove":
                    if (arguments.Count < 2) { response = "drone remove <id>"; return false; }
                    var tp = Player.Get(arguments.At(1));
                    if (tp == null) { response = "Player not found."; return false; }
                    var dr = FpvDroneSystem.GetByOwner(tp);
                    if (dr == null) { response = "No drone."; return false; }
                    dr.Destroy(false); response = $"Removed from {tp.Nickname}"; return true;
                case "list":
                    response = FpvDroneSystem.ListDrones(); return true;
                default:
                    response = "drone give/delete/remove/list"; return false;
            }
        }
    }
}