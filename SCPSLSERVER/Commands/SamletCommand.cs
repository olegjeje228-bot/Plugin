using System;
using System.Linq;
using CommandSystem;
using Exiled.API.Features;
using UnityEngine;

namespace EventHUD.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(ClientCommandHandler))]
    public class SamletCommand : ICommand
    {
        public string Command => "samlet";
        public string[] Aliases => new[] { "saml" };
        public string Description => "samlet enter/leave - управление самолётом";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            response = "Ручное управление самолётом удалено. Используйте: bomba record/bomba play.";
            return false;
        }
    }
}