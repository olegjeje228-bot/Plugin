using System;
using CommandSystem;
using Exiled.API.Features;

namespace EventHUD.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class SinkholeCommand : ICommand
    {
        public string Command => "sinkhl";
        public string[] Aliases => Array.Empty<string>();
        public string Description => "Управление чёрной лужей (sinkhole) в ЛКЗ: sinkhl on/off";

        public static bool IsEnabled { get; private set; }

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count < 1)
            {
                response = "Использование: sinkhl on/off";
                return false;
            }

            string arg = arguments.At(0).ToLowerInvariant();
            bool enable = arg == "on";

            if (arg != "on" && arg != "off")
            {
                response = "Использование: sinkhl on/off";
                return false;
            }

            IsEnabled = enable;

            try
            {
                Type sinkType = Type.GetType("SinkholeBehaviour, Assembly-CSharp");

                if (sinkType == null)
                    sinkType = Type.GetType("LightContainmentZoneDecontamination.SinkholeBehaviour, Assembly-CSharp");

                if (sinkType == null)
                {
                    response = "Класс SinkholeBehaviour не найден.";
                    return false;
                }

                foreach (var sinkhole in UnityEngine.Object.FindObjectsOfType(sinkType))
                {
                    var behaviour = sinkhole as UnityEngine.Behaviour;
                    if (behaviour != null)
                        behaviour.enabled = enable;

                    var go = sinkhole as UnityEngine.GameObject;
                    if (go == null && behaviour != null)
                        go = behaviour.gameObject;

                    if (go != null)
                        go.SetActive(enable);
                }

                response = enable
                    ? "Sinkhole (чёрная лужа) включен."
                    : "Sinkhole (чёрная лужа) выключен.";
            }
            catch (Exception e)
            {
                response = "Ошибка: " + e.Message;
                return false;
            }

            return true;
        }
    }
}