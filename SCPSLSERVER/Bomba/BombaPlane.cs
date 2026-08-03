using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Items;
using Exiled.API.Features.Pickups;
using MEC;
using UnityEngine;

namespace EventHUD.Bomba
{
    public static class BombaPlane
    {
        private class Plane
        {
            public int Id;
            public object Schematic;
            public object SoundPlayer; // зацикленный звук
            public Vector3 Position;
            public Vector3 Direction;
            public float Speed;         // максимальная скорость м/с (для manual)
            public float Lifetime;
            public int BulletHits;
            public int MicroHidHits;
            public CoroutineHandle MoveHandle;
            public CoroutineHandle BombHandle;
            public CoroutineHandle SoundHandle;
        }

        private static readonly List<Plane> Planes = new List<Plane>();
        private static int _nextId = 1;
        private static bool _merChecked;
        private static Action<object, Vector3> _schematicSetPos;
        private static Action<object, Quaternion> _schematicSetRot;
        private static Func<object, bool> _schematicDestroy;

        private static readonly System.Random Rng = new System.Random();

        // максимум активных самолётов одновременно (защита от ресурсного исчерпания)
        private const int MaxPlanes = 10;

        public static int ActiveCount => Planes.Count;

        // ── Публичные методы для контроллера ──
        public static object GetPlane(int id) => Planes.FirstOrDefault(p => p.Id == id);
        public static int GetActiveCount(out int[] ids)
        {
            ids = Planes.Select(p => p.Id).ToArray();
            return ids.Length;
        }
        public static Vector3 GetPlanePosition(object planeObj)
        {
            if (planeObj is Plane plane)
                return plane.Position;
            return Vector3.zero;
        }
        public static float GetPlaneSpeed(object planeObj)
        {
            if (planeObj is Plane plane)
                return plane.Speed;
            return 25f;
        }
        public static void SetPlanePosition(object planeObj, Vector3 pos, Quaternion rot)
        {
            if (planeObj is Plane plane)
            {
                plane.Position = pos;
                SetSchematicPos(plane.Schematic, pos);
                SetSchematicRot(plane.Schematic, rot);
            }
        }
        public static void DropGrenadePublic(object planeObj)
        {
            if (planeObj is Plane plane)
                DropGrenade(plane);
        }
        public static bool CheckCollisionPublic(Vector3 from, Vector3 to, out Vector3 hitPoint)
            => CheckCollision(from, to, out hitPoint);
        public static void ExplodePlanePublic(object planeObj, int count)
        {
            if (planeObj is Plane plane)
                ExplodePlane(plane, count);
        }

        public static void Reset()
        {
            foreach (Plane p in Planes.ToList())
                RemovePlane(p);
            _nextId = 1;
        }

        public static int Spawn(Player admin, Vector3 direction, bool auto = true, bool bombs = true, float speed = 0f)
        {
            var cfg = Plugin.Instance?.Config;
            if (cfg == null)
                return 0;

            // защита от ресурсного исчерпания - не больше MaxPlanes самолётов одновременно
            if (Planes.Count >= MaxPlanes)
                return 0;

            Vector3 start = admin.Position + direction * 10f + Vector3.up * 5f;
            Vector3 dir = direction.normalized;

            // Самолёт повёрнут боком, доворачиваем на 90 вправо
            object schematic = SpawnSchematic(cfg.BombaSchematic, start, Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 90f, 0f));
            if (schematic == null)
                return 0;

            // Скорость 0 = дефолт из конфига
            if (speed <= 0f)
                speed = cfg?.BombaSpeed ?? 20f;

            var plane = new Plane
            {
                Id = _nextId++,
                Schematic = schematic,
                SoundPlayer = null,
                Position = start,
                Direction = dir,
                Speed = speed,
                Lifetime = 0f,
            };

            Planes.Add(plane);

            if (auto)
            {
                plane.MoveHandle = Timing.RunCoroutine(MoveLoop(plane));
                if (bombs)
                    plane.BombHandle = Timing.RunCoroutine(BombLoop(plane));
                plane.SoundHandle = Timing.RunCoroutine(SoundLoop(plane));
            }

            return plane.Id;
        }

        // спавн самолёта в конкретной точке (для воспроизведения записей)
        public static int SpawnAt(Vector3 position, Quaternion rotation)
        {
            var cfg = Plugin.Instance?.Config;
            if (cfg == null)
                return 0;

            // защита от ресурсного исчерпания - не больше MaxPlanes самолётов одновременно
            if (Planes.Count >= MaxPlanes)
                return 0;

            // Самолёт повёрнут боком, доворачиваем на 90 вправо (как в Spawn)
            object schematic = SpawnSchematic(cfg.BombaSchematic, position, rotation * Quaternion.Euler(0f, 90f, 0f));
            if (schematic == null)
                return 0;

            var plane = new Plane
            {
                Id = _nextId++,
                Schematic = schematic,
                SoundPlayer = null,
                Position = position,
                Direction = rotation * Vector3.forward,
                Speed = cfg?.BombaSpeed ?? 20f,
                Lifetime = 0f,
            };

            Planes.Add(plane);
            return plane.Id;
        }

        // запуск звука у самолёта (для воспроизведения)
        public static void StartSoundPublic(int id)
        {
            Plane plane = Planes.FirstOrDefault(p => p.Id == id);
            if (plane == null)
                return;

            plane.SoundHandle = Timing.RunCoroutine(SoundLoop(plane));
        }

        public static bool Delete(int id)
        {
            Plane plane = Planes.FirstOrDefault(p => p.Id == id);
            if (plane == null)
                return false;

            RemovePlane(plane);
            return true;
        }

        public static bool DeleteAll()
        {
            if (Planes.Count == 0)
                return false;
            Reset();
            return true;
        }

        public static string List()
        {
            if (Planes.Count == 0)
                return "Активных самолётов нет.";
            return string.Join("\n", Planes.Select(p => $"#{p.Id} — {p.Position}"));
        }

        private static void RemovePlane(Plane plane)
        {
            Timing.KillCoroutines(plane.MoveHandle);
            Timing.KillCoroutines(plane.BombHandle);
            Timing.KillCoroutines(plane.SoundHandle);
            if (plane.SoundPlayer != null)
                Audio.SoundService.StopHandle(plane.SoundPlayer);
            DestroySchematic(plane.Schematic);
            Planes.Remove(plane);
        }

        private static IEnumerator<float> MoveLoop(Plane plane)
        {
            var cfg = Plugin.Instance?.Config;
            float speed = plane.Speed > 0f ? plane.Speed : (cfg?.BombaSpeed ?? 20f);

            while (true)
            {
                yield return Timing.WaitForSeconds(0.05f);

                if (plane == null || !Planes.Contains(plane))
                    yield break;

                float dt = 0.05f;
                plane.Lifetime += dt;

                if (plane.Lifetime >= (cfg?.BombaMaxLifetime ?? 120f))
                {
                    ExplodePlane(plane, 40);
                    yield break;
                }

                Vector3 newPos = plane.Position + plane.Direction * (speed * dt);

                if (CheckCollision(plane.Position, newPos, out Vector3 hitPoint))
                {
                    plane.Position = hitPoint;
                    ExplodePlane(plane, 40);
                    yield break;
                }

                plane.Position = newPos;
                SetSchematicPos(plane.Schematic, newPos);
                SetSchematicRot(plane.Schematic, Quaternion.LookRotation(plane.Direction) * Quaternion.Euler(0f, 90f, 0f));
            }
        }

        private static IEnumerator<float> BombLoop(Plane plane)
        {
            var cfg = Plugin.Instance?.Config;
            int perSecond = cfg?.BombaGrenadesPerSecond ?? 10;
            float interval = 1f / Math.Max(1, perSecond);

            while (true)
            {
                yield return Timing.WaitForSeconds(interval);

                if (plane == null || !Planes.Contains(plane))
                    yield break;

                DropGrenade(plane);
            }
        }

        private static IEnumerator<float> SoundLoop(Plane plane)
        {
            var cfg = Plugin.Instance?.Config;
            string sound = cfg?.BombaSound ?? "samlet";
            if (sound.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                sound = sound.Substring(0, sound.Length - 4);
            float volume = cfg?.BombaSoundVolume ?? 2f;
            float range = cfg?.BombaSoundRange ?? 300f;

            yield return Timing.WaitForSeconds(0.1f);

            if (plane == null || !Planes.Contains(plane))
                yield break;

            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!a.FullName.StartsWith("AudioPlayer")) continue;

                    var playerType = a.GetType("AudioPlayer");
                    if (playerType == null) break;

                    var createMethod = playerType
                        .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                        .FirstOrDefault(m => m.Name == "CreateOrGet");

                    if (createMethod == null) break;

                    var pars = createMethod.GetParameters();
                    object[] args = new object[pars.Length];
                    args[0] = $"Bomba-{plane.Id}";
                    for (int i = 1; i < pars.Length; i++)
                    {
                        Type pt = pars[i].ParameterType;
                        if (pt == typeof(bool))
                            args[i] = pars[i].Name == "sendSoundGlobally";
                        else if (pt == typeof(byte))
                            args[i] = (byte)255;
                        else
                            args[i] = null;
                    }

                    object player = createMethod.Invoke(null, args);
                    if (player == null)
                    {
                        Log.Warn("[Bomba] Не удалось создать AudioPlayer для звука самолёта.");
                        break;
                    }

                    var t = player.GetType();
                    t.GetProperty("SendSoundGlobally")?.SetValue(player, true);
                    t.GetProperty("DestroyWhenAllClipsPlayed")?.SetValue(player, false);

                    var addSpeaker = t.GetMethod("AddSpeaker", new[]
                    {
                        typeof(string), typeof(Vector3), typeof(float),
                        typeof(bool), typeof(float), typeof(float)
                    });

                    if (addSpeaker == null)
                    {
                        Log.Warn("[Bomba] AddSpeaker(6) не найден в AudioPlayerApi.");
                        t.GetMethod("Destroy", Type.EmptyTypes)?.Invoke(player, null);
                        break;
                    }

                    addSpeaker.Invoke(player, new object[]
                        { "Main", plane.Position, volume, true, 3f, range });

                    var addClip = t.GetMethod("AddClip", new[]
                    {
                        typeof(string), typeof(float), typeof(bool), typeof(bool)
                    });

                    if (addClip == null)
                    {
                        Log.Warn("[Bomba] AddClip(4) не найден в AudioPlayerApi.");
                        t.GetMethod("Destroy", Type.EmptyTypes)?.Invoke(player, null);
                        break;
                    }

                    addClip.Invoke(player, new object[] { sound, volume, true, true });

                    plane.SoundPlayer = player;
                    if (Plugin.Instance?.Config?.Debug ?? false)
                        Log.Debug($"[Bomba] Запущен зацикленный звук {sound} для самолёта #{plane.Id}");
                    break;
                }
            }
            catch (Exception e)
            {
                Log.Warn($"[Bomba] Ошибка создания звука: {e.Message}");
            }

            var setPos = plane.SoundPlayer?.GetType().GetMethod("SetSpeakerPosition", new[] { typeof(string), typeof(Vector3) });

            while (true)
            {
                yield return Timing.WaitForSeconds(0.2f);

                if (plane == null || !Planes.Contains(plane))
                    yield break;

                if (plane.SoundPlayer != null && setPos != null)
                {
                    try { setPos.Invoke(plane.SoundPlayer, new object[] { "Main", plane.Position }); }
                    catch { yield break; }
                }
            }
        }

        private static void DropGrenade(Plane plane)
        {
            var cfg = Plugin.Instance?.Config;
            Vector3 local = ParseVector3(cfg?.BombaGrenadeLocalPos ?? "-0.956 1.384 2.447");

            Quaternion planeRot = Quaternion.LookRotation(plane.Direction) * Quaternion.Euler(0f, 90f, 0f);
            Vector3 dropPos = plane.Position + planeRot * local;

            // Разброс направления ±20 градусов — гранаты летят в разнобой
            float yaw = (float)(Rng.NextDouble() - 0.5) * 40f;
            float pitch = (float)(Rng.NextDouble() - 0.5) * 40f;
            Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * plane.Direction;
            dir.Normalize();

            try
            {
                // Создаём гранату через Item.Create (работает в Exiled)
                ExplosiveGrenade grenade = (ExplosiveGrenade)Item.Create(ItemType.GrenadeHE);
                if (grenade == null)
                {
                    Log.Warn("[Bomba] Item.Create вернул null.");
                    return;
                }

                // FuseTime 5 секунд — запас, но взрыв произойдёт раньше при касании земли
                grenade.FuseTime = 5f;

                // SpawnActive создаёт активный проектайл-снаряд в мире
                grenade.SpawnActive(dropPos);

                // Пробуем получить Rigidbody из базового объекта
                try
                {
                    foreach (var obj in grenade.Base.GetComponentsInChildren<Rigidbody>(true))
                    {
                        obj.velocity = dir * 12f;
                        obj.angularVelocity = Vector3.zero;
                        obj.useGravity = true;
                        break;
                    }
                }
                catch { }

                // Добавляем компонент взрыва при касании земли
                // Если граната отскочила от земли — взрываем её сразу
                try
                {
                    var go = grenade.Base.gameObject;
                    if (go != null && go.GetComponent<BombaImpactExplode>() == null)
                        go.AddComponent<BombaImpactExplode>();
                }
                catch { }
            }
            catch (Exception e)
            {
                Log.Warn($"[Bomba] Ошибка сброса гранаты: {e.Message}");
            }
        }

        private static void ExplodePlane(Plane plane, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = new Vector3(
                    (float)(Rng.NextDouble() - 0.5) * 6f,
                    (float)(Rng.NextDouble() - 0.5) * 3f,
                    (float)(Rng.NextDouble() - 0.5) * 6f);

                Vector3 pos = plane.Position + offset;
                Map.ExplodeEffect(pos, ProjectileType.FragGrenade);
            }

            RemovePlane(plane);
        }

        private static readonly Vector3 PlaneHitboxSize = new Vector3(2f, 2f, 2f);

        /// <summary>
        /// Только большие кубы на Surface — хитбоксы для самолёта.
        /// Все пилоны/земля имеют хотя бы одну ось > 20 метров.
        /// Обычные стены/двери — меньше.
        /// </summary>
        private static bool IsSurfaceHitbox(Collider collider)
        {
            if (collider == null)
                return false;

            Vector3 size = collider.bounds.size;

            // Все кубы скинутые пользователем имеют минимум одну ось > 20м:
            // x: 1.16-157.9, y: 16-45, z: 8.7-154
            // Поэтому достаточно любой оси > 15 метров
            return size.x > 15f || size.y > 15f || size.z > 15f;
        }

        private static bool CheckCollision(Vector3 from, Vector3 to, out Vector3 hitPoint)
        {
            hitPoint = to;
            Vector3 dir = (to - from).normalized;
            float dist = Vector3.Distance(from, to);

            // Невидимые барьеры на высоте Y 310-316
            if (to.y >= 310f && to.y <= 316f)
                return false;

            // BoxCast — самолёт считается кубом 4x4x4
            // Половина размера для BoxCast = 2
            Vector3 halfExtents = PlaneHitboxSize * 0.5f;

            if (Physics.BoxCast(from, halfExtents, dir, out RaycastHit boxHit, Quaternion.identity, dist))
            {
                if (boxHit.collider == null || boxHit.collider.isTrigger)
                    return false;

                if (boxHit.collider.GetComponentInParent<ReferenceHub>() != null)
                    return false;

                if (IsInvisibleWall(boxHit.collider))
                    return false;

                if (boxHit.point.y >= 310f && boxHit.point.y <= 316f)
                    return false;

                // НА SURFACE: только большие кубы (пилоны/земля) — хитбоксы самолёта
                // Обычные стены, двери, мелкие объекты — игнорируем
                if (!IsSurfaceHitbox(boxHit.collider))
                    return false;

                hitPoint = boxHit.point;
                return true;
            }

            return false;
        }

        private static bool IsInvisibleWall(Collider collider)
        {
            if (collider == null)
                return false;

            // 1. Если есть Renderer и он выключен — невидимая стена
            var renderer = collider.GetComponentInChildren<Renderer>();
            if (renderer != null)
                return !renderer.enabled;

            // 2. Если есть ColliderVisualizer (компонент от viscols) — это невидимый коллайдер
            //    viscols добавляет зелёный визуализатор к невидимым коллайдерам
            try
            {
                if (collider.GetComponent("ColliderVisualizer") != null ||
                    collider.GetComponent("ColliderDebugVisualizer") != null ||
                    collider.GetComponent("ColliderDebug") != null)
                    return true;
            }
            catch { }

            // 3. Если на объекте нет Renderer вообще — это невидимый коллайдер
            //    (стены/полы имеют MeshRenderer, невидимые барьеры — нет)
            if (renderer == null)
            {
                // Проверяем родителя — если у родителя есть видимый рендерер, значит коллайдер видимый
                var parentRenderer = collider.GetComponentInParent<Renderer>();
                if (parentRenderer == null)
                    return true; // нет рендерера ни на себе, ни на родителе — невидимый
            }

            return false;
        }

        private static Vector3 ParseVector3(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return new Vector3(-0.956f, 1.384f, 2.447f);

            string[] parts = s.Split(' ');
            if (parts.Length >= 3 &&
                float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                return new Vector3(x, y, z);
            }
            return new Vector3(-0.956f, 1.384f, 2.447f);
        }

        // ── Стрельба по самолёту ──
        public static void OnPlayerShooting(Exiled.Events.EventArgs.Player.ShootingEventArgs ev)
        {
            if (Planes.Count == 0)
                return;

            if (ev == null || ev.Player == null)
                return;

            Player shooter = ev.Player;
            if (!shooter.IsConnected || !shooter.IsAlive || shooter.IsNPC)
                return;

            if (ev.Firearm == null)
                return;

            if (!Round.IsStarted)
                return;

            Plane target = FindPlaneInLine(shooter);
            if (target == null)
                return;

            var cfg = Plugin.Instance?.Config;

            float dist = Vector3.Distance(shooter.Position, target.Position);
            if (dist >= (cfg?.BombaLongRangeDistance ?? 200f))
            {
                if (Rng.NextDouble() < (cfg?.BombaLongRangeMissChance ?? 0.3f))
                    return;
            }

            if (ev.Firearm != null && ev.Firearm.Type == ItemType.MicroHID)
            {
                target.MicroHidHits++;
                if (target.MicroHidHits >= (cfg?.BombaMicroHidHitsToDestroy ?? 3))
                {
                    ExplodePlane(target, 40);
                    return;
                }
            }
            else
            {
                target.BulletHits++;

                Vector3 toShooter = (shooter.Position - target.Position).normalized;
                Vector3 right = Vector3.Cross(target.Direction, Vector3.up).normalized;
                float side = Vector3.Dot(toShooter, right);
                float deflection = (cfg?.BombaBulletDeflection ?? 1f) * (side >= 0 ? 1f : -1f);

                Quaternion rot = Quaternion.AngleAxis(deflection, Vector3.up);
                target.Direction = rot * target.Direction;
                target.Direction.Normalize();

                if (target.BulletHits >= (cfg?.BombaBulletsPerGrenade ?? 40))
                {
                    target.BulletHits = 0;
                    Vector3 pos = target.Position + new Vector3(
                        (float)(Rng.NextDouble() - 0.5) * 2f,
                        (float)(Rng.NextDouble() - 0.5) * 1f,
                        (float)(Rng.NextDouble() - 0.5) * 2f);
                    Map.ExplodeEffect(pos, ProjectileType.FragGrenade);
                }

                if (target.BulletHits >= (cfg?.BombaBulletsToDestroy ?? 250))
                {
                    ExplodePlane(target, 40);
                }
            }
        }

        private static Plane FindPlaneInLine(Player shooter)
        {
            Plane best = null;
            float bestDist = float.MaxValue;

            foreach (Plane p in Planes)
            {
                Vector3 toPlane = p.Position - shooter.CameraTransform.position;
                float dist = toPlane.magnitude;
                if (dist > 300f)
                    continue;

                float angle = Vector3.Angle(shooter.CameraTransform.forward, toPlane.normalized);
                if (angle > 15f)
                    continue;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = p;
                }
            }

            return best;
        }

        // ── ProjectMER schematic helpers ──
        private static object SpawnSchematic(string name, Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            try
            {
                Type spawnerType = Type.GetType("ProjectMER.Features.ObjectSpawner, ProjectMER");
                Type schematicType = Type.GetType("ProjectMER.Features.Objects.SchematicObject, ProjectMER");
                if (spawnerType == null || schematicType == null)
                {
                    Log.Error("[Bomba] ProjectMER не найден.");
                    return null;
                }

                var method = spawnerType.GetMethod("TrySpawnSchematic",
                    new[] { typeof(string), typeof(Vector3), typeof(Quaternion), schematicType.MakeByRefType() });
                if (method == null)
                {
                    Log.Error("[Bomba] TrySpawnSchematic не найден.");
                    return null;
                }

                object[] args = { name, position, rotation, null };
                bool result = (bool)method.Invoke(null, args);
                if (!result)
                {
                    Log.Error($"[Bomba] Не удалось создать {name}.");
                    return null;
                }
                return args[3];
            }
            catch (Exception ex)
            {
                Log.Warn($"[Bomba] Ошибка создания {name}: {ex.Message}");
                return null;
            }
        }

        private static void InitMer()
        {
            if (_merChecked)
                return;
            _merChecked = true;

            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!a.FullName.StartsWith("ProjectMER"))
                        continue;

                    var schematicType = a.GetType("ProjectMER.Features.Objects.SchematicObject");
                    if (schematicType != null)
                    {
                        var posProp = schematicType.GetProperty("Position");
                        if (posProp != null)
                            _schematicSetPos = (obj, v) => posProp.SetValue(obj, v);

                        var rotProp = schematicType.GetProperty("Rotation");
                        if (rotProp != null)
                            _schematicSetRot = (obj, v) => rotProp.SetValue(obj, v);

                        var destroyMethod = schematicType.GetMethod("Destroy", Type.EmptyTypes);
                        if (destroyMethod != null)
                            _schematicDestroy = obj => { destroyMethod.Invoke(obj, null); return true; };
                    }
                    break;
                }
            }
            catch { }
        }

        private static void SetSchematicPos(object schematic, Vector3 pos)
        {
            if (schematic == null) return;
            if (_schematicSetPos != null) { try { _schematicSetPos(schematic, pos); } catch { } return; }
            try { var prop = schematic.GetType().GetProperty("Position"); prop?.SetValue(schematic, pos); } catch { }
        }

        private static void SetSchematicRot(object schematic, Quaternion rot)
        {
            if (schematic == null) return;
            if (_schematicSetRot != null) { try { _schematicSetRot(schematic, rot); } catch { } return; }
            try { var prop = schematic.GetType().GetProperty("Rotation"); prop?.SetValue(schematic, rot); } catch { }
        }

        private static void DestroySchematic(object schematic)
        {
            if (schematic == null) return;
            if (_schematicDestroy != null) { try { _schematicDestroy(schematic); } catch { } return; }
            try { var method = schematic.GetType().GetMethod("Destroy", Type.EmptyTypes); method?.Invoke(schematic, null); } catch { }
        }
    }
}