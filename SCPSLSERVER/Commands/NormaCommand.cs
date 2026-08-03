using System;
using System.Linq;
using CommandSystem;
using Exiled.API.Features;

namespace EventHUD.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class NormaCommand : ICommand
    {
        public string Command => "norma";
        public string[] Aliases => Array.Empty<string>();
        public string Description => "norma send/show [дни]";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count == 0)
            {
                response = "norma send/show [дни]";
                return false;
            }

            string sub = arguments.At(0).ToLowerInvariant();
            double days = 3.0;

            if (arguments.Count > 1)
                double.TryParse(arguments.At(1).Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out days);

            if (days <= 0)
                days = 3.0;

            var norma = Plugin.Instance?.Norma;
            if (norma == null)
            {
                response = "Система нормы не запущена.";
                return false;
            }

            switch (sub)
            {
                case "send":
                    norma.SendReport(days, false);
                    response = $"Отчёт за {days} дн. отправлен в канал нормы.";
                    return true;

                case "show":
                    string report = norma.BuildReportText(days);
                    if (string.IsNullOrEmpty(report))
                    {
                        response = "Отчёт пуст.";
                        return false;
                    }
                    foreach (Player p in Player.List)
                        Hud.HudNoticeService.Show(p, report, 15f);
                    response = "Отчёт показан в игре.";
                    return true;

                default:
                    response = "norma send/show [дни]";
                    return false;
            }
        }
    }
}