using System;
using Exiled.API.Enums;
using Exiled.API.Features;
using UnityEngine;

namespace EventHUD.Bomba
{
    /// <summary>
    /// Взрывает гранату при первом касании земли/стены.
    /// Добавляется к гранатам, сброшенным с самолёта.
    /// </summary>
    public sealed class BombaImpactExplode : MonoBehaviour
    {
        private bool _exploded;

        private void OnCollisionEnter(Collision collision)
        {
            if (_exploded)
                return;

            // Игнорируем столкновение с самим самолётом и другими гранатами
            if (collision.collider == null)
                return;

            // Не взрываемся от столкновения с игроками (пусть отскочат)
            if (collision.collider.GetComponentInParent<ReferenceHub>() != null)
                return;

            _exploded = true;

            try
            {
                // Взрыв в точке касания
                Map.ExplodeEffect(transform.position, ProjectileType.FragGrenade);

                // Уничтожаем гранату
                Destroy(gameObject);
            }
            catch (Exception e)
            {
                Log.Warn($"[Bomba] Ошибка взрыва при касании: {e.Message}");
            }
        }
    }
}