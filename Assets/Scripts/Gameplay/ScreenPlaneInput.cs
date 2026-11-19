using UnityEngine;

namespace Guochao
{
    // Pure math: maps raw screen axes (WASD) onto the camera's ground plane.
    // W always walks toward the top of the screen, never along a hardcoded
    // world axis; diagonals stay clamped to the same length as cardinals.
    public static class ScreenPlaneInput
    {
        public static Vector3 Direction(Camera camera, Vector2 input)
        {
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 forward, right;
            if (camera != null)
            {
                Vector3 screenRight = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up);
                Vector3 screenUp = Vector3.ProjectOnPlane(camera.transform.up, Vector3.up);
                // Each ground axis has zero projection onto the OTHER screen
                // axis. Flattening camera.forward/right alone fails under roll.
                right = Vector3.Cross(Vector3.up, screenUp);
                forward = Vector3.Cross(screenRight, Vector3.up);
                if (right.sqrMagnitude < 1e-6f) right = screenRight;
                if (forward.sqrMagnitude < 1e-6f)
                    forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
                if (Vector3.Dot(right, screenRight) < 0) right = -right;
                if (Vector3.Dot(forward, screenUp) < 0) forward = -forward;
            }
            else { forward = Vector3.forward; right = Vector3.right; }
            if (forward.sqrMagnitude < 1e-6f || right.sqrMagnitude < 1e-6f ||
                Vector3.Cross(forward.normalized, right.normalized).sqrMagnitude < 1e-6f)
            {
                // A horizon-level view cannot represent both screen axes on a
                // flat floor. Keep a deterministic finite basis in that case;
                // true top-down views use camera.up above, without Euler gimbal.
                if (camera != null) right = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up);
                if (right.sqrMagnitude > 1e-6f)
                    forward = Vector3.Cross(right.normalized, Vector3.up);
                else if (forward.sqrMagnitude > 1e-6f)
                    right = Vector3.Cross(Vector3.up, forward.normalized);
                else { forward = Vector3.forward; right = Vector3.right; }
            }
            Vector3 direction = forward.normalized * input.y + right.normalized * input.x;
            // Rolled-camera ground axes need not be orthogonal. Preserve analog
            // magnitude and equal diagonal world speed after combining them.
            return direction.sqrMagnitude > 1e-8f ? direction.normalized * input.magnitude : Vector3.zero;
        }
    }
}
