using System;
using System.Collections.Generic;
using System.Globalization;
using CommandSystem;
using Exiled.API.Features;
using UnityEngine;

namespace EventHUD.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class SizeCommand : ICommand
    {
        public string Command => "size";
        public string[] Aliases => Array.Empty<string>();
        public string Description => "Изменяет размер игрока: size all/<id.id.id> <x> [y] [z]";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count < 2)
            {
                response = "Использование: size all/<id.id.id> <x> [y] [z] | Пример: size all 0.5 | size 1.2 0.1 0.1 0.2";
                return false;
            }

            string target = arguments.At(0).ToLowerInvariant();

            if (!float.TryParse(arguments.At(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
            {
                response = "Неверное значение scale: '" + arguments.At(1) + "'";
                return false;
            }

            float y = x;
            float z = x;

            if (arguments.Count >= 3)
            {
                if (float.TryParse(arguments.At(2), NumberStyles.Float, CultureInfo.InvariantCulture, out float py))
                    y = py;
                else
                {
                    response = "Неверное значение y: '" + arguments.At(2) + "'";
                    return false;
                }
            }

            if (arguments.Count >= 4)
            {
                if (float.TryParse(arguments.At(3), NumberStyles.Float, CultureInfo.InvariantCulture, out float pz))
                    z = pz;
                else
                {
                    response = "Неверное значение z: '" + arguments.At(3) + "'";
                    return false;
                }
            }

            if (x < 0.05f || y < 0.05f || z < 0.05f || x > 10f || y > 10f || z > 10f)
            {
                response = "Scale должен быть в диапазоне 0.05 - 10";
                return false;
            }

            var scale = new Vector3(x, y, z);

            if (target == "all")
            {
                int count = 0;

                foreach (Player p in Player.List)
                {
                    if (p == null || !p.IsAlive)
                        continue;

                    p.Scale = scale;
                    count++;
                }

                response = "Размер изменён у " + count + " игроков: " + Format(scale);
                return true;
            }

            var names = new List<string>();

            foreach (string token in target.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Player p = int.TryParse(token, out int id) ? Player.Get(id) : null;
                if (p == null)
                    continue;

                p.Scale = scale;
                names.Add(p.Nickname);
            }

            if (names.Count == 0)
            {
                response = "Игроки не найдены.";
                return false;
            }

            response = "Размер изменён: " + string.Join(", ", names) + " | " + Format(scale);
            return true;
        }

        private static string Format(Vector3 v)
        {
            return v.x.ToString("0.##", CultureInfo.InvariantCulture) + " " +
                   v.y.ToString("0.##", CultureInfo.InvariantCulture) + " " +
                   v.z.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}