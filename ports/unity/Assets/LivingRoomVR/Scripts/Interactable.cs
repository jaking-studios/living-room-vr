using UnityEngine;

namespace LivingRoomVR
{
    /// One chore object in a room (pillow, book, frame, lamp, tv, plant, trash).
    public class Interactable : MonoBehaviour
    {
        public string type;
        public bool bad;          // needs fixing
        public bool isTask;       // part of this room's chore list
        public Transform homeParent;
        public Vector3 homeLocalPos;
        public Quaternion homeLocalRot;
        public Renderer[] renderers;
        public float tiltAngle;
        public Vector3 tiltAxis;

        public void SetHighlight(float h)
        {
            if (renderers == null) return;
            foreach (var r in renderers) foreach (var m in r.materials) if (m.HasProperty("_Highlight")) m.SetFloat("_Highlight", h);
        }
    }
}
