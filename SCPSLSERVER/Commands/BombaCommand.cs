using System;
using System.Globalization;
using CommandSystem;
using Exiled.API.Features;
using UnityEngine;

namespace EventHUD.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class BombaCommand : ICommand
    {
        public string Command => "bomba";
        public string[] Aliases => Array.Empty<string>();
        public string Description => "bomba record/play/save/list/spawn";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count == 0)
            {
                response = "bomba record/play/save/list/spawn";
                return false;
            }

            string sub = arguments.At(0).ToLowerInvariant();

            switch (sub)
            {
                case "record":
                case "rec":
                {
                    var player = Player.Get(sender);
                    if (player == null)
                    {
                        response = "Команда доступна только игрокам.";
                        return false;
                    }

                    // остановка записи
                    if (arguments.Count >= 2 && arguments.At(1).ToLowerInvariant() == "stop")
                    {
                        if (Bomba.BombaRecorder.IsRecording)
                        {
                            Bomba.BombaRecorder.StopRecording();
                            response = "Запись остановлена.";
                            return true;
                        }
                        response = "Запись не идёт.";
                        return false;
                    }

                    // если запись уже идёт - останавливаем её
                    if (Bomba.BombaRecorder.IsRecording)
                    {
                        Bomba.BombaRecorder.StopRecording();
                        response = "Запись остановлена.";
                        return true;
                    }

                    float delay = 0f;
                    if (arguments.Count >= 2)
                    {
                        float.TryParse(arguments.At(1), NumberStyles.Float, CultureInfo.InvariantCulture, out delay);
                        if (delay < 0f) delay = 0f;
                        if (delay > 30f) delay = 30f;
                    }

                    bool started = Bomba.BombaRecorder.StartRecording(player, delay);
                    response = started
                        ? (delay > 0f
                            ? $"Запись начнётся через {delay:0} сек."
                            : "Запись начата. Приземление или bomba record stop остановит запись. Фонарик выкл = бомбы летят.")
                        : "Не удалось начать запись.";
                    return started;
                }

                case "play":
                {
                    var player = Player.Get(sender);
                    if (player == null)
                    {
                        response = "Команда доступна только игрокам.";
                        return false;
                    }

                    string name = arguments.Count >= 2 ? arguments.At(1) : null;

                    bool ok = Bomba.BombaRecorder.PlayRecording(player, name);
                    response = ok
                        ? (string.IsNullOrEmpty(name)
                            ? "Воспроизведение текущей записи."
                            : $"Воспроизведение записи '{name}'.")
                        : "Запись не найдена. Используйте: bomba record, затем bomba save <имя>.";
                    return ok;
                }

                case "save":
                {
                    string name = arguments.Count >= 2 ? arguments.At(1) : null;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        response = "Использование: bomba save <имя>";
                        return false;
                    }

                    bool ok = Bomba.BombaRecorder.SaveRecording(name);
                    response = ok ? $"Запись сохранена как '{name}'." : "Нет записи для сохранения.";
                    return ok;
                }

                case "list":
                {
                    response = Bomba.BombaRecorder.ListRecordings();
                    return true;
                }

                case "spawn":
                {
                    var player = Player.Get(sender);
                    if (player == null)
                    {
                        response = "Команда доступна только игрокам.";
                        return false;
                    }

                    bool bombs = true;
                    float speed = 0f;

                    if (arguments.Count >= 2)
                    {
                        string mode = arguments.At(1).ToLowerInvariant();
                        if (mode == "safe")
                            bombs = false;
                        // auto или любое другое - обычный самолёт
                    }

                    if (arguments.Count >= 3)
                    {
                        if (float.TryParse(arguments.At(2), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float ps))
                        {
                            speed = Mathf.Clamp(ps, 0f, 500f);
                        }
                    }

                    Vector3 dir = player.CameraTransform.forward;
                    int id = Bomba.BombaPlane.Spawn(player, dir, true, bombs, speed);
                    if (id == 0)
                    {
                        response = "Не удалось создать самолёт.";
                        return false;
                    }

                    string modeName = bombs ? "обычный" : "safe";
                    string speedText = speed > 0f ? $", скорость {speed:0} м/с" : "";
                    response = $"Самолёт #{id} запущен ({modeName}{speedText}).";
                    return true;
                }

                case "delete":
                {
                    if (arguments.Count < 2)
                    {
                        response = "bomba delete <id> или bomba delete all";
                        return false;
                    }

                    string target = arguments.At(1).ToLowerInvariant();
                    if (target == "all")
                    {
                        bool ok = Bomba.BombaPlane.DeleteAll();
                        response = ok ? "Все самолёты удалены." : "Активных самолётов нет.";
                        return ok;
                    }

                    if (int.TryParse(target, out int id))
                    {
                        bool ok = Bomba.BombaPlane.Delete(id);
                        response = ok ? $"Самолёт #{id} удалён." : $"Самолёт #{id} не найден.";
                        return ok;
                    }

                    response = "bomba delete <id> или bomba delete all";
                    return false;
                }

                default:
                    response = "bomba record/play/save/list/spawn";
                    return false;
            }
        }
    }
}