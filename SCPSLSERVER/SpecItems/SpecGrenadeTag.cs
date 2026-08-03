namespace EventHUD.SpecItems
{
    using UnityEngine;

    public sealed class SpecGrenadeTag : MonoBehaviour
    {
        public static bool HasTag(GameObject obj)
        {
            return obj != null && obj.GetComponent<SpecGrenadeTag>() != null;
        }
    }
}