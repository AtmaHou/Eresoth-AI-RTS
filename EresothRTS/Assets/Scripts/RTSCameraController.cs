using UnityEngine;

namespace Eresoth
{
    /// <summary>RTS 镜头：WASD/方向键/鼠标边缘平移，滚轮缩放，Q/E 旋转。</summary>
    public class RTSCameraController : MonoBehaviour
    {
        public Transform cam;
        public float moveSpeed = 28f;
        public float edge = 14f;
        public float minDist = 14f, maxDist = 45f;

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 f = transform.forward; f.y = 0; f.Normalize();
            Vector3 r = transform.right; r.y = 0; r.Normalize();

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move -= f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= r;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += r;

            var mp = Input.mousePosition;
            if (mp.x < edge) move -= r;
            if (mp.x > Screen.width - edge) move += r;
            if (mp.y < edge) move -= f;
            if (mp.y > Screen.height - edge) move += f;

            transform.position += move.normalized * moveSpeed * dt;
            var p = transform.position;
            float mapEdge = Game.MapHalfSize - 5f;
            p.x = Mathf.Clamp(p.x, -mapEdge, mapEdge);
            p.z = Mathf.Clamp(p.z, -mapEdge, mapEdge);
            transform.position = p;

            // 缩放：沿相机局部方向推拉
            float s = Input.mouseScrollDelta.y;
            if (Mathf.Abs(s) > 0.01f && cam != null)
            {
                Vector3 dir = cam.localPosition.normalized;
                float dist = Mathf.Clamp(cam.localPosition.magnitude - s * 6f, minDist, maxDist);
                cam.localPosition = dir * dist;
            }

            // 旋转
            if (Input.GetKey(KeyCode.Q)) transform.Rotate(0, -80 * dt, 0);
            if (Input.GetKey(KeyCode.E)) transform.Rotate(0, 80 * dt, 0);
        }
    }
}
