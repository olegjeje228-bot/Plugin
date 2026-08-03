using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Exiled.API.Features;
using MEC;

namespace EventHUD.Hud
{
    public class HudCompositor
    {
        private readonly Config          _config;
        private readonly EffectScheduler _effects;
        private          CoroutineHandle _tickHandle;

        public HudCompositor(Config config, EffectScheduler effects)
        {
            _config  = config;
            _effects = effects;
        }

        public void Start() => _tickHandle = Timing.RunCoroutine(TickLoop());
        public void Stop()  => Timing.KillCoroutines(_tickHandle);

        private IEnumerator<float> TickLoop()
        {
            while (true)
            {
                float interval = GetCurrentInterval();

                foreach (var player in Player.List)
                {
                    try
                    {
                        if (!HudToggleService.IsEnabled(player))
                        {
                            string offNotice = HudNoticeService.GetActive(player);
                            if (!string.IsNullOrEmpty(offNotice))
                            {
                                player.ShowHint(
                                    $"<indent={_config.RadioSwitchHintIndent}%><voffset=10em><size={_config.BaseFontSize}>{offNotice}",
                                    interval + 2f);
                            }
                            continue;
                        }

                        // Не показываем HUD для Dummy-игроков и ботов
                        if (player.IsNPC || (player.Nickname != null && player.Nickname.Contains("Dummy")))
                            continue;

                        string text = Build(player);
                        if (!string.IsNullOrEmpty(text))
                            player.ShowHint(text, interval + 2f);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"[HUD] TickLoop exception for {player?.Nickname}: {ex.Message}");
                    }
                }

                yield return Timing.WaitForSeconds(interval);
            }
        }

        private static bool IsInNoclip(Player player)
        {
            try
            {
                if (player.ReferenceHub == null)
                    return false;

                var ccm = player.ReferenceHub.characterClassManager;
                if (ccm == null)
                    return false;

                // в разных версиях поле называется по-разному
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                foreach (var name in new[] { "Noclip", "_noclip", "noclip" })
                {
                    var prop = ccm.GetType().GetProperty(name, flags);
                    if (prop != null && prop.CanRead)
                        return (bool)prop.GetValue(ccm);

                    var field = ccm.GetType().GetField(name, flags);
                    if (field != null)
                        return (bool)field.GetValue(ccm);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private float GetCurrentInterval()
        {
            bool anyEffect = _effects.State.IsMinutePulseActive ||
                             _effects.State.IsFlashActive;

            return anyEffect ? _config.EffectTickInterval : _config.HudUpdateInterval;
        }

        private string Build(Player player)
        {
            var sw = Stopwatch.StartNew();

            // Сдвиг HUD вниз при noclip, чтобы не пересекался с индикатором
            float noclipShift = IsInNoclip(player) ? 7f : 0f;

            // FullRP: гейт скрывает только карточку и статус ивента, но не геймплей (049, уведомления)
            bool rpGate = Rpm.FullRpState.IsEnabled && !Rpm.FullRpState.IsConfirmed(player.UserId);

            string card      = rpGate ? string.Empty : PlayerCardBuilder.Build(player, _config);
            string eventPart = rpGate ? string.Empty : EventStatusBuilder.Build(_config);
            string full      = card + eventPart;

            sw.Stop();
            if (sw.ElapsedMilliseconds > 5 && _config.Debug)
                Log.Debug($"[HUD] Build took {sw.ElapsedMilliseconds}ms for {player.Nickname}");

            // Строка обезвреживания растяжки (на 1em ниже состояния)
            if (!rpGate)
            {
                string disarm = Tripwire.TripwireSystem.GetDisarmHudLine(player);
                if (!string.IsNullOrEmpty(disarm))
                {
                    full +=
                        $"<indent={_config.RadioSwitchHintIndent}%>" +
                        $"<voffset={_config.MedicineHudVoffset - 1 + noclipShift}em>" +
                        $"<size={_config.BaseFontSize}><color=#FFC107>" + disarm + "</color></size>";
                }
            }

            // Хинт смены волны (1 сек, поверх всего)
            string radioNotice = Radio.RadioSwitchNoticeService.GetActive(player);
            if (!string.IsNullOrEmpty(radioNotice))
            {
                full +=
                    $"<indent={_config.RadioSwitchHintIndent}%>" +
                    $"<voffset={_config.RadioSwitchHintVoffset + noclipShift}em>" +
                    $"<size={_config.BaseFontSize}>" +
                    radioNotice +
                    "</size>";
            }

            // Общие уведомления (SCP, медицина, команды) в этом же хинте
            // ВАЖНО: без <align>, align в TMP сдвигает весь HUD
            string notice = HudNoticeService.GetActive(player, NoticePosition.Top);
            if (!string.IsNullOrEmpty(notice))
            {
                full +=
                    $"<indent={_config.RadioSwitchHintIndent}%>" +
                    $"<voffset={10 + noclipShift}em>" +
                    $"<size={_config.BaseFontSize}>" + notice + "</size>";
            }

            // Прокси-чат SCP под закрывающей скобкой CInfo в левом верхнем углу
            string topLeftNotice = HudNoticeService.GetActive(player, NoticePosition.TopLeft);
            if (!string.IsNullOrEmpty(topLeftNotice))
            {
                full +=
                    $"<indent={_config.RoleLabelIndent}%>" +
                    $"<voffset={_config.RoleValueVoffset - 1 + noclipShift}em>" +
                    $"<size={_config.BaseFontSize}>" + topLeftNotice + "</size>";
            }

            // Кастомное оружие ближе к центру экрана
            string centerNotice = HudNoticeService.GetActive(player, NoticePosition.Center);
            if (!string.IsNullOrEmpty(centerNotice))
            {
                full +=
                    "<indent=30%>" +
                    $"<voffset={noclipShift}em>" +
                    $"<size={_config.BaseFontSize}>" + centerNotice + "</size>";
            }

            // Эффекты цвета
            if (_effects.State.IsFlashActive)
            {
                full = EffectApplier.ApplyColorOverlay(
                    full,
                    _effects.State.FlashProgress,
                    _config.ColorFlashColor);
            }
            else if (_effects.State.IsMinutePulseActive)
            {
                full = EffectApplier.ApplyColorOverlay(
                    full,
                    _effects.State.MinutePulseProgress,
                    _config.MinutePulseColor);
            }

            return full;
        }
    }
}