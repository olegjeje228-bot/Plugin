using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Exiled.API.Features;
using MEC;
using Newtonsoft.Json;
using UnityEngine;

namespace EventHUD.Bomba
{
    public static class BombaRecorder
    {
        // точка записи (позиция, поворот, время, флажок бомб, флажок взрыва)
        public class RecordingPoint
        {
            public float x, y, z;
            public float rx, ry, rz;
            public float t;
            public bool bomb;
            public bool explode;
        }

        // запись целиком
        public class Recording
        {
            public string name;
            public bool selfDestruct;
            public List<RecordingPoint> points = new List<RecordingPoint>();
        }

        private static string RecordingsDir => Path.Combine(Paths.Configs, "EventHUD", "BombaRecordings");

        // текущая запись в памяти (последняя остановленная)
        private static Recording _current;
        private static readonly Dictionary<string, Recording> _saved = new Dictionary<string, Recording>();

        // активная запись
        private static Recording _activeRecording;
        private static Player _recordingPlayer;
        private static CoroutineHandle _recordHandle;
        private static CoroutineHandle _recordHudHandle;
        private static float _recordRealtimeStart;

        // воспроизведение
        private static CoroutineHandle _playHandle;
        private static int _playPlaneId;
        private static Recording _playData;
        private static Player _playOwner;

        public static bool IsRecording => _recordingPlayer != null;
        public static bool IsPlaying => _playData != null;

        // константы
        private const float RecordInterval = 0.1f;
        private const float HudInterval = 0.2f;
        private const float MaxRecordSeconds = 300f;

        // загрузка всех сохранённых записей при старте плагина
        public static void LoadAll()
        {
            _saved.Clear();
            try
            {
                if (!Directory.Exists(RecordingsDir))
                {
                    Directory.CreateDirectory(RecordingsDir);
                    return;
                }

                foreach (string file in Directory.GetFiles(RecordingsDir, "*.json"))
                {
                    try
                    {
                        Recording rec = JsonConvert.DeserializeObject<Recording>(File.ReadAllText(file));
                        if (rec == null || string.IsNullOrEmpty(rec.name) || rec.points == null || rec.points.Count == 0)
                            continue;
                        rec.name = Path.GetFileNameWithoutExtension(file);
                        _saved[rec.name] = rec;
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"[Bomba] Не удалось загрузить запись {Path.GetFileName(file)}: {e.Message}");
                    }
                }
                Log.Info($"[Bomba] Загружено записей: {_saved.Count}");
            }
            catch (Exception e)
            {
                Log.Warn($"[Bomba] Ошибка загрузки записей: {e.Message}");
            }
        }

        public static void Reset()
        {
            StopRecording();
            StopPlayback();
            _current = null;
        }

        // начало записи. Если уже идёт запись - остановить её.
        public static bool StartRecording(Player player, float delay)
        {
            if (player == null) return false;

            if (IsRecording)
            {
                StopRecording();
                return true;
            }

            if (IsPlaying)
                StopPlayback();

            _recordingPlayer = player;
            _activeRecording = new Recording { name = "rec", points = new List<RecordingPoint>() };
            _recordHandle = Timing.RunCoroutine(RecordWaitLoop(player, delay));
            return true;
        }

        // остановка записи (сохраняет как текущую)
        public static void StopRecording()
        {
            if (_recordingPlayer == null) return;

            Timing.KillCoroutines(_recordHandle);
            Timing.KillCoroutines(_recordHudHandle);

            // восстанавливаем ноклип и godmode
            RestoreNoclip(_recordingPlayer);
            RestoreGodMode(_recordingPlayer);

            if (_activeRecording != null && _activeRecording.points.Count > 0)
            {
                _current = _activeRecording;
                if (Plugin.Instance?.Config?.Debug ?? false)
                    Log.Debug($"[Bomba] Запись остановлена: {_current.points.Count} точек");
            }

            _activeRecording = null;
            _recordingPlayer = null;
        }

        // задержка перед началом записи (чтобы админ успел включить ноклип)
        private static IEnumerator<float> RecordWaitLoop(Player player, float delay)
        {
            if (delay > 0f)
            {
                player.Broadcast(5, $"<size=25>Запись начнётся через {delay:0} сек. Включите ноклип и невидимость.</size>");
                yield return Timing.WaitForSeconds(delay);
            }

            if (_recordingPlayer == null || !player.IsConnected)
                yield break;

            // включает ноклип и godmode, чтобы админ мог летать
            ToggleNoclip(player, true);
            ToggleGodMode(player, true);

            // выдаём карточку уборщика и гранату (для управления бомбами и самоуничтожением)
            GiveKeycard(player);
            GiveGrenade(player);

            // экипируем карточку, чтобы граната не была в руках
            EquipKeycard(player);

            _recordRealtimeStart = Time.realtimeSinceStartup;
            _recordHandle = Timing.RunCoroutine(RecordLoop(player));
            _recordHudHandle = Timing.RunCoroutine(RecordHudLoop(player));
        }

        // основной цикл записи точек каждые 0.1с
        private static IEnumerator<float> RecordLoop(Player player)
        {
            float startY = player.Position.y;
            float maxY = startY;
            float airborneTime = 0f;

            while (true)
            {
                yield return Timing.WaitForSeconds(RecordInterval);

                if (_recordingPlayer == null || player == null || !player.IsConnected)
                {
                    StopRecording();
                    yield break;
                }

                float elapsed = Time.realtimeSinceStartup - _recordRealtimeStart;
                float currentY = player.Position.y;

                // отслеживаем максимальную высоту полёта
                if (currentY > maxY)
                    maxY = currentY;

                // если админ поднялся выше стартовой точки минимум на 3 метра - он в воздухе
                bool isAirborne = currentY > startY + 3f;

                if (isAirborne)
                    airborneTime += RecordInterval;
                else
                    airborneTime = 0f;

                // приземлился после полёта:
                // был в воздухе минимум 3 сек И опустился на 5+ метров от максимальной высоты
                if (elapsed >= MaxRecordSeconds ||
                    (airborneTime >= 3f && maxY - currentY >= 5f))
                {
                    StopRecording();
                    yield break;
                }

                Vector3 pos = player.Position;
                Quaternion rot = player.CameraTransform.rotation;
                Vector3 euler = rot.eulerAngles;

                // карточка уборщика в руках = бомбы летят
                bool bomb = IsKeycardInHands(player);

                // если админ взял гранату в руки - самолёт взорвётся в этой точке
                bool explode = IsGrenadeInHands(player);

                _activeRecording.points.Add(new RecordingPoint
                {
                    x = pos.x, y = pos.y, z = pos.z,
                    rx = euler.x, ry = euler.y, rz = euler.z,
                    t = (float)Math.Round(elapsed, 2),
                    bomb = bomb,
                    explode = explode
                });
            }
        }

        // HUD записи в broadcast каждые 0.2с
        private static IEnumerator<float> RecordHudLoop(Player player)
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(HudInterval);

                if (_recordingPlayer == null || player == null || !player.IsConnected)
                    yield break;

                if (_activeRecording == null) yield break;

                float elapsed = Time.realtimeSinceStartup - _recordRealtimeStart;
                string hud = $"<size=25><color=green>Запись идёт: {elapsed:0.0} сек | Точки: {_activeRecording.points.Count}</color></size>";
                player.ClearBroadcasts();
                player.Broadcast(1, hud);
            }
        }


        // выдаёт карточку уборщика, если её нет
        private static void GiveKeycard(Player player)
        {
            try
            {
                if (player == null || !player.IsConnected)
                    return;

                bool hasKeycard = false;
                foreach (var item in player.Items)
                {
                    if (item.Type == ItemType.KeycardJanitor)
                    {
                        hasKeycard = true;
                        break;
                    }
                }

                if (!hasKeycard)
                    player.AddItem(ItemType.KeycardJanitor);
            }
            catch { }
        }

        // выдаёт гранату, если её нет (для самоуничтожения)
        private static void GiveGrenade(Player player)
        {
            try
            {
                if (player == null || !player.IsConnected)
                    return;

                bool hasGrenade = false;
                foreach (var item in player.Items)
                {
                    if (item.Type == ItemType.GrenadeHE || item.Type == ItemType.GrenadeFlash)
                    {
                        hasGrenade = true;
                        break;
                    }
                }

                if (!hasGrenade)
                    player.AddItem(ItemType.GrenadeHE);
            }
            catch { }
        }

        // экипирует карточку уборщика
        private static void EquipKeycard(Player player)
        {
            try
            {
                if (player == null || !player.IsConnected)
                    return;

                foreach (var item in player.Items)
                {
                    if (item.Type == ItemType.KeycardJanitor)
                    {
                        player.CurrentItem = item;
                        return;
                    }
                }
            }
            catch { }
        }

        // карточка уборщика в руках?
        private static bool IsKeycardInHands(Player player)
        {
            try
            {
                var current = player.CurrentItem;
                return current != null && current.Type == ItemType.KeycardJanitor;
            }
            catch { }
            return false;
        }

        // граната в руках?
        private static bool IsGrenadeInHands(Player player)
        {
            try
            {
                var current = player.CurrentItem;
                return current != null && (current.Type == ItemType.GrenadeHE || current.Type == ItemType.GrenadeFlash);
            }
            catch { }
            return false;
        }

        // ноклип через рефлексию (как в HudCompositor)
        private static void ToggleNoclip(Player player, bool value)
        {
            try
            {
                if (player.ReferenceHub == null) return;
                var ccm = player.ReferenceHub.characterClassManager;
                if (ccm == null) return;

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                foreach (var name in new[] { "Noclip", "_noclip", "noclip" })
                {
                    var prop = ccm.GetType().GetProperty(name, flags);
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(ccm, value);
                        return;
                    }
                    var field = ccm.GetType().GetField(name, flags);
                    if (field != null)
                    {
                        field.SetValue(ccm, value);
                        return;
                    }
                }
            }
            catch { }
        }

        private static void RestoreNoclip(Player player)
        {
            ToggleNoclip(player, false);
        }

        private static void ToggleGodMode(Player player, bool value)
        {
            try
            {
                if (player != null) player.IsGodModeEnabled = value;
            }
            catch { }
        }

        private static void RestoreGodMode(Player player)
        {
            ToggleGodMode(player, false);
        }

        // получение текущей записи (для bomba play без имени)
        public static Recording GetCurrent()
        {
            return _current;
        }

        // сохранение записи в файл
        public static bool SaveRecording(string name)
        {
            if (_current == null || _current.points.Count == 0)
                return false;

            if (string.IsNullOrWhiteSpace(name))
                name = $"rec_{DateTime.Now:HHmmss}";

            try
            {
                if (!Directory.Exists(RecordingsDir))
                    Directory.CreateDirectory(RecordingsDir);

                var rec = new Recording
                {
                    name = name,
                    selfDestruct = _current.selfDestruct,
                    points = _current.points.Select(p => new RecordingPoint
                    {
                        x = p.x, y = p.y, z = p.z,
                        rx = p.rx, ry = p.ry, rz = p.rz,
                        t = p.t, bomb = p.bomb, explode = p.explode
                    }).ToList()
                };

                string path = Path.Combine(RecordingsDir, name + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(rec, Formatting.Indented));
                _saved[name] = rec;
                return true;
            }
            catch (Exception e)
            {
                Log.Warn($"[Bomba] Ошибка сохранения записи: {e.Message}");
                return false;
            }
        }

        // список сохранённых записей
        public static string ListRecordings()
        {
            if (_saved.Count == 0)
                return "Сохранённых записей нет.";

            var lines = _saved.Values
                .OrderBy(r => r.name)
                .Select(r =>
                {
                    float dur = r.points.Count > 0 ? r.points[r.points.Count - 1].t : 0f;
                    return $"{r.name} ({dur:0.0} сек, {r.points.Count} точек)";
                });
            return string.Join("\n", lines);
        }

        // воспроизведение записи по имени или текущей
        public static bool PlayRecording(Player player, string name)
        {
            if (player == null) return false;

            Recording data = null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                _saved.TryGetValue(name, out data);
            }
            else
            {
                data = _current;
            }

            if (data == null || data.points.Count < 2)
                return false;

            if (IsRecording)
                StopRecording();

            if (IsPlaying)
                StopPlayback();

            _playData = data;
            _playOwner = player;
            _playHandle = Timing.RunCoroutine(PlayLoop(player, data));
            return true;
        }

        public static void StopPlayback()
        {
            if (_playData == null) return;

            Timing.KillCoroutines(_playHandle);
            if (_playPlaneId > 0)
                BombaPlane.Delete(_playPlaneId);
            _playPlaneId = 0;
            _playData = null;
            _playOwner = null;
        }

        // основной цикл воспроизведения
        private static IEnumerator<float> PlayLoop(Player owner, Recording data)
        {
            var points = data.points;
            float totalTime = points[points.Count - 1].t;

            // спавним самолёт на первой точке
            RecordingPoint first = points[0];
            Vector3 startPos = new Vector3(first.x, first.y, first.z);
            Quaternion startRot = Quaternion.Euler(first.rx, first.ry, first.rz);

            int planeId = BombaPlane.SpawnAt(startPos, startRot);
            if (planeId == 0)
            {
                _playData = null;
                yield break;
            }
            _playPlaneId = planeId;
            object plane = BombaPlane.GetPlane(planeId);

            // запускаем звук самолёта
            BombaPlane.StartSoundPublic(planeId);

            float elapsed = 0f;
            Vector3 prevPos = startPos;
            Vector3 lastHudPos = startPos;

            while (true)
            {
                yield return Timing.WaitForSeconds(0.05f); // 20 тиков в секунду для плавности

                if (_playPlaneId == 0 || _playData == null)
                    yield break;

                plane = BombaPlane.GetPlane(_playPlaneId);
                if (plane == null)
                    yield break;

                elapsed += 0.05f;

                // конец записи - просто удаляем самолёт (без взрыва)
                if (elapsed >= totalTime)
                {
                    BombaPlane.Delete(_playPlaneId);
                    _playPlaneId = 0;
                    _playData = null;
                    yield break;
                }

                // находим текущий сегмент
                int idx = FindSegmentIndex(points, elapsed);
                if (idx < 0 || idx >= points.Count - 1)
                {
                    BombaPlane.Delete(_playPlaneId);
                    _playPlaneId = 0;
                    _playData = null;
                    yield break;
                }

                var p0 = points[idx];
                var p1 = points[idx + 1];
                float segLen = Mathf.Max(0.001f, p1.t - p0.t);
                float frac = Mathf.Clamp01((elapsed - p0.t) / segLen);

                Vector3 pos = Vector3.Lerp(
                    new Vector3(p0.x, p0.y, p0.z),
                    new Vector3(p1.x, p1.y, p1.z), frac);
                Quaternion rot = Quaternion.Slerp(
                    Quaternion.Euler(p0.rx, p0.ry, p0.rz),
                    Quaternion.Euler(p1.rx, p1.ry, p1.rz), frac);

                // сброс бомб: пока карточка в руках (bomb=true) - гранаты летят каждые 0.1с
                if (points[idx].bomb)
                {
                    BombaPlane.DropGrenadePublic(plane);
                }

                // если админ взял гранату в этой точке - самолёт взрывается сразу
                if (points[idx].explode)
                {
                    BombaPlane.ExplodePlanePublic(plane, 40);
                    _playPlaneId = 0;
                    _playData = null;
                    yield break;
                }

                // движение самолёта (с доворотом на 90 как в Spawn)
                BombaPlane.SetPlanePosition(plane, pos, rot * Quaternion.Euler(0f, 90f, 0f));
                prevPos = pos;

                // HUD каждые 0.2с
                if (elapsed - (lastHudPos.x - 0) > 0.2f)
                {
                    lastHudPos.x = elapsed;
                    bool bombsOn = points[idx].bomb;
                    string bombText = bombsOn ? "<color=red>вкл</color>" : "<color=green>выкл</color>";
                    string hud = $"<size=25><color=yellow>Воспроизведение: {elapsed:0.0}/{totalTime:0.0} сек | Бомбы: {bombText}</color></size>";
                    if (owner != null && owner.IsConnected)
                    {
                        owner.ClearBroadcasts();
                        owner.Broadcast(1, hud);
                    }
                }
            }
        }

        // поиск индекса сегмента по времени
        private static int FindSegmentIndex(List<RecordingPoint> points, float time)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (time >= points[i].t && time <= points[i + 1].t)
                    return i;
            }
            return points.Count - 2;
        }
    }
}