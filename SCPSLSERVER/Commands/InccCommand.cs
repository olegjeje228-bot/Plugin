using System;
using System.Collections.Generic;
using CommandSystem;
using Exiled.API.Features;
using MEC;

namespace EventHUD.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class InccCommand : ICommand
    {
        public string Command => "incc";
        public string[] Aliases => Array.Empty<string>();
        public string Description => "Блокировка/разблокировка интеркома: incc on/off";

        public static bool IsBlocked { get; private set; }
        private static CoroutineHandle _coroutine;
        private static readonly HashSet<string> Muted = new();

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count < 1)
            {
                response = "Использование: incc on/off";
                return false;
            }

            string arg = arguments.At(0).ToLowerInvariant();
            if (arg != "on" && arg != "off")
            {
                response = "Использование: incc on/off";
                return false;
            }

            bool enable = arg == "on";

            IsBlocked = enable;
            Timing.KillCoroutines(_coroutine);

            if (enable)
            {
                MuteAll();
                _coroutine = Timing.RunCoroutine(BlockSequence());
                response = "Интерком заблокирован.";
            }
            else
            {
                UnmuteAll();
                _coroutine = Timing.RunCoroutine(UnblockSequence());
                response = "Интерком разблокируется...";
            }

            return true;
        }

        private static void MuteAll()
        {
            Muted.Clear();
            string hostId = EventManager.Instance.Session?.HostUserId;

            foreach (var p in Player.List)
            {
                // Проводящий ивента не мутится
                if (!string.IsNullOrEmpty(hostId) && p.UserId == hostId)
                    continue;

                Muted.Add(p.UserId);
                p.IsMuted = true;
            }
        }

        private static void UnmuteAll()
        {
            foreach (var id in Muted)
            {
                var p = Player.Get(id);
                if (p != null)
                    p.IsMuted = false;
            }
            Muted.Clear();
        }

        private static void SetIntercomText(string text)
        {
            try
            {
                foreach (var p in Player.List)
                {
                    if (p.ReferenceHub == null)
                        continue;

                    // Ищем компонент Intercom на игроке через рефлексию
                    var intercom = p.ReferenceHub.GetComponent("Intercom");
                    if (intercom == null)
                        continue;

                    var prop = intercom.GetType().GetProperty("Network_displayUnit");
                    if (prop != null && prop.CanWrite)
                        prop.SetValue(intercom, text);
                }
            }
            catch { }
        }

        private static IEnumerator<float> BlockSequence()
        {
            string[] lines =
            {
                "[SHUTDOWN] Запрос на завершение работы...",
                "[SHUTDOWN]   Оператор: ID 7842",
                "",
                "[SHUTDOWN] Деактивация аудиоинтерфейса...",
                "[SHUTDOWN]   Микрофон: ВЫКЛ",
                "[SHUTDOWN]   Динамики: ВЫКЛ",
                "",
                "[SHUTDOWN] Освобождение каналов...",
                "[SHUTDOWN]   Канал A: СВОБОДЕН",
                "[SHUTDOWN]   Канал B: СВОБОДЕН",
                "[SHUTDOWN]   Канал C: СВОБОДЕН",
                "",
                "[HOLD] Завершение...",
                "[HOLD]   3с",
                "[HOLD]   2с",
                "[HOLD]   1с",
                "",
                "[SHUTDOWN] ОТКЛЮЧЕНИЕ...",
                "[TIMER]   5.0s",
                "[TIMER]   4.0s",
                "[TIMER]   3.0s",
                "[TIMER]   2.0s",
                "[TIMER]   1.0s",
                "[TIMER]   0.0s",
                "",
                "[SHUTDOWN] C.A.S.S.I.E: ОТКЛЮЧЕН",
                "[SHUTDOWN] Intercom: НЕАКТИВЕН",
            };

            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            for (int i = 0; i < lines.Length; i++)
            {
                if (!IsBlocked) yield break;

                string line = lines[i];
                string full = $"<color=red>{timestamp} {line}</color>\n<color=red>Заблокировано</color>";

                SetIntercomText(full);

                // Реалистичные паузы: длинные строки - дольше, пустые - быстрее
                float delay = string.IsNullOrEmpty(line) ? 0.3f : Math.Max(0.6f, line.Length / 40f);
                if (line.StartsWith("[TIMER]")) delay = 1.0f;
                if (line.StartsWith("[HOLD]")) delay = 1.0f;
                yield return Timing.WaitForSeconds(delay);
            }

            // Остаётся только "Заблокировано"
            while (IsBlocked)
            {
                SetIntercomText("<color=red>Заблокировано</color>");
                yield return Timing.WaitForSeconds(1.5f);
            }
        }

        private static IEnumerator<float> UnblockSequence()
        {
            string[] lines =
            {
                "[INFO] Инициализация аудиоинтерфейса...",
                "[INFO] Драйвер: ALSA (v1.2.8)",
                "[INFO] Проверка целостности каналов: PASSED",
                "",
                "[AUDIO] Калибровка уровня сигнала...",
                "[AUDIO]   Канал A: -3.2 dB  OK",
                "[AUDIO]   Канал B: -2.8 dB  OK",
                "[AUDIO]   Канал C: -4.1 dB  OK",
                "",
                "[SECURE] Аутентификация оператора...",
                "[SECURE]   ID: 7842 | Уровень доступа: 4",
                "[SECURE]   Подтверждено: [REDACTED]",
                "",
                "[INTERCOM] Активация режима ПЕРЕДАЧА...",
                "[INTERCOM] Блокировка входящих каналов: ВЫПОЛНЕНО",
                "[INTERCOM] MUTE для всех абонентов: АКТИВЕН",
                "",
                "[HOLD] Удержание соединения...",
                "[HOLD]   3 секунды",
                "[HOLD]   2 секунды",
                "[HOLD]   1 секунда",
                "",
                "[RELEASE] РАЗБЛОКИРОВКА КАНАЛА...",
                "[TIMER]   5.0s | Активно",
                "[TIMER]   4.0s | Активно",
                "[TIMER]   3.0s | Активно",
                "[TIMER]   2.0s | Активно",
                "[TIMER]   1.0s | Активно",
                "[TIMER]   0.0s | Завершено",
                "",
                "[INTERCOM] Канал освобожден",
                "[INTERCOM] C.A.S.S.I.E Активен",
                "[SYSTEM] Возврат в режим ожидания",
                "",
                "[STATUS] ГОТОВНОСТЬ: ОЖИДАНИЕ | СИСТЕМА: СТАБИЛЬНА",
            };

            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            for (int i = 0; i < lines.Length; i++)
            {
                if (IsBlocked) yield break;

                string line = lines[i];
                string full = $"<color=green>{timestamp} {line}</color>";

                SetIntercomText(full);

                float delay = string.IsNullOrEmpty(line) ? 0.3f : Math.Max(0.6f, line.Length / 40f);
                if (line.StartsWith("[TIMER]")) delay = 1.0f;
                if (line.StartsWith("[HOLD]")) delay = 1.0f;
                yield return Timing.WaitForSeconds(delay);
            }
        }
    }
}