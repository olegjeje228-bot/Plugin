using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;

namespace EventHUD.Logging
{
    public static class PlayerDisconnectTracker
    {
        private const int ThresholdSeconds = 240; // 4 минуты

        private class Session
        {
            public DateTime JoinedAt;
            public readonly List<string> Actions = new List<string>();
        }

        private static readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();

        public static void Register()
        {
            Exiled.Events.Handlers.Player.Verified += OnVerified;
            Exiled.Events.Handlers.Player.Left += OnLeft;
        }

        public static void Unregister()
        {
            Exiled.Events.Handlers.Player.Verified -= OnVerified;
            Exiled.Events.Handlers.Player.Left -= OnLeft;
            _sessions.Clear();
        }

        public static void TrackAction(string userId, string command)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(command))
                return;

            lock (_sessions)
            {
                if (!_sessions.TryGetValue(userId, out var s))
                    return;

                // Ограничиваем список действий
                if (s.Actions.Count < 20)
                    s.Actions.Add(command);
            }
        }

        private static void OnVerified(VerifiedEventArgs ev)
        {
            if (ev.Player == null || string.IsNullOrEmpty(ev.Player.UserId))
                return;

            lock (_sessions)
            {
                _sessions[ev.Player.UserId] = new Session { JoinedAt = DateTime.UtcNow };
            }
        }

        private static void OnLeft(LeftEventArgs ev)
        {
            if (ev.Player == null || string.IsNullOrEmpty(ev.Player.UserId))
                return;

            Session s;
            lock (_sessions)
            {
                if (!_sessions.TryGetValue(ev.Player.UserId, out s))
                    return;
                _sessions.Remove(ev.Player.UserId);
            }

            double seconds = (DateTime.UtcNow - s.JoinedAt).TotalSeconds;
            if (seconds > ThresholdSeconds)
                return;

            int ping = GetPing(ev.Player);
            string actions = s.Actions.Count == 0
                ? "нет"
                : string.Join(", ", s.Actions.Take(10));

            string timeStr = FormatTime(seconds);
            string pingStr = ping >= 0 ? ping.ToString() : "?";

            GameLogService.Game.Add(
                $"[Быстрый выход] {Tag(ev.Player)} вышел через {timeStr}. ПИНГ при выходе: {pingStr}, Действий: {s.Actions.Count} | {actions}");
        }

        private static int GetPing(Player p)
        {
            try
            {
                if (p.ReferenceHub == null)
                    return -1;

                var conn = p.ReferenceHub.networkIdentity.connectionToClient;
                if (conn == null)
                    return -1;

                // в разных версиях игры свойство называется по-разному
                var prop = conn.GetType().GetProperty("ping");
                if (prop != null && prop.CanRead)
                    return Convert.ToInt32(prop.GetValue(conn));

                var field = conn.GetType().GetField("ping");
                if (field != null)
                    return Convert.ToInt32(field.GetValue(conn));

                return -1;
            }
            catch
            {
                return -1;
            }
        }

        private static string Tag(Player p) =>
            p == null ? "[?][?]" : $"[{p.UserId}][{p.Nickname}]";

        private static string FormatTime(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalMinutes >= 1)
                return $"{(int)t.TotalMinutes} мин {t.Seconds} сек";
            return $"{t.Seconds} сек";
        }
    }
}