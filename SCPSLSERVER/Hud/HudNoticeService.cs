using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Features;

namespace EventHUD.Hud
{
    public enum NoticePosition
    {
        TopLeft,  // под карточкой игрока
        Center,   // центр экрана (кастомное оружие)
        Top       // верх экрана (общие уведомления)
    }

    public static class HudNoticeService
    {
        private class Notice
        {
            public string         Text;
            public DateTime       ExpiresAt;
            public NoticePosition Position;
        }

        private static readonly Dictionary<string, List<Notice>> _notices = new();

        public static void Show(Player player, string text, float duration,
            NoticePosition position = NoticePosition.Top)
        {
            if (player == null || string.IsNullOrEmpty(text))
                return;

            lock (_notices)
            {
                if (!_notices.TryGetValue(player.UserId, out var list))
                {
                    list = new List<Notice>();
                    _notices[player.UserId] = list;
                }

                list.Add(new Notice
                {
                    Text      = text,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(duration),
                    Position  = position
                });

                if (list.Count > 8)
                    list.RemoveAt(0);
            }
        }

        public static string GetActive(Player player)
        {
            if (player == null)
                return null;

            lock (_notices)
            {
                if (!_notices.TryGetValue(player.UserId, out var list))
                    return null;

                DateTime now = DateTime.UtcNow;
                list.RemoveAll(n => now >= n.ExpiresAt);

                if (list.Count == 0)
                {
                    _notices.Remove(player.UserId);
                    return null;
                }

                if (list.Count == 1)
                    return list[0].Text;

                // Нельзя использовать \n или <br> — ломает HUD.
                // Каждое следующее уведомление смещаем на строку вниз через <voffset>.
                return string.Join("<voffset=-1em>", list.Select(n => n.Text));
            }
        }

        /// <summary>
        /// Возвращает активные уведомления для указанной позиции.
        /// </summary>
        public static string GetActive(Player player, NoticePosition position)
        {
            if (player == null)
                return null;

            lock (_notices)
            {
                if (!_notices.TryGetValue(player.UserId, out var list))
                    return null;

                DateTime now = DateTime.UtcNow;
                list.RemoveAll(n => now >= n.ExpiresAt);

                if (list.Count == 0)
                {
                    _notices.Remove(player.UserId);
                    return null;
                }

                var filtered = list.Where(n => n.Position == position).ToList();
                if (filtered.Count == 0)
                    return null;

                if (filtered.Count == 1)
                    return filtered[0].Text;

                // Нельзя использовать \n или <br> — ломает HUD.
                // Каждое следующее уведомление смещаем на строку вниз через <voffset>.
                return string.Join("<voffset=-1em>", filtered.Select(n => n.Text));
            }
        }

        public static void Clear(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return;

            lock (_notices)
                _notices.Remove(userId);
        }

        public static void Reset() => _notices.Clear();
    }
}