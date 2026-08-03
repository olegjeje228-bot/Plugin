using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CommandSystem;
using Exiled.API.Features;
using Newtonsoft.Json;

namespace EventHUD.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class UpdateCommand : ICommand
    {
        public string Command => "update";
        public string[] Aliases => Array.Empty<string>();
        public string Description => string.Empty;

        private static readonly (string Key, string Title, int Color)[] Types =
        {
            ("bug",     "🟥 Обнаружен баг",             0xE74C3C),
            ("fixed",   "🟩 Баг пофикшен",              0x2ECC71),
            ("update",  "🟦 Обновление",                0x3498DB),
            ("events",  "⚙️ Обновление Events",         0x9B59B6),
            ("discord", "🔷 Обновление дискорд канала", 0x5865F2),
            ("tech",    "🛠 Технические работы",        0xF1C40F),
        };

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count == 0)
            {
                response = "update <тип> <текст>";
                return false;
            }

            string type = arguments.At(0).ToLowerInvariant();
            string text = string.Join(" ", arguments.Skip(1));

            if (string.IsNullOrWhiteSpace(text))
            {
                response = "Текст не может быть пустым.";
                return false;
            }

            var found = Types.FirstOrDefault(t => t.Key == type);
            if (found.Key == null)
            {
                response = "Неизвестный тип. Доступные: bug, fixed, update, events, discord, tech";
                return false;
            }

            string url = Plugin.Instance?.Config?.DiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                response = "Webhook не задан.";
                return false;
            }

            var payload = new
            {
                embeds = new object[]
                {
                    new
                    {
                        title = found.Title,
                        description = text.Replace("\\n", "\n"),
                        color = found.Color,
                        footer = new { text = "Aceline Events" },
                        timestamp = DateTime.UtcNow.ToString("o"),
                    }
                }
            };

            _ = PostAsync(url, payload);

            response = "Обновление отправлено.";
            return true;
        }

        private static async Task PostAsync(string url, object payload)
        {
            try
            {
                string json = JsonConvert.SerializeObject(payload);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    var resp = await client.PostAsync(url, content).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        Log.Warn($"[Update] Webhook HTTP {(int)resp.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[Update] Webhook error: {ex.Message}");
            }
        }
    }
}