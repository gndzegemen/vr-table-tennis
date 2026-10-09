using UnityEngine;

namespace TableTennis
{
    /// <summary>
    /// One player's racket. It always moves on that player's hitting plane; the game sets a target position
    /// (from the mouse, the AI or the network) and the racket follows it while tracking its own velocity.
    /// </summary>
    public class Racket : MonoBehaviour
    {
        /// <summary>Half extents of the forgiving hit zone around the blade centre, in metres.</summary>
        public static readonly Vector2 HitHalfExtents = new Vector2(0.15f, 0.14f);

        [SerializeField] int m_Player;
        [Tooltip("How quickly the racket catches up with its target (higher is snappier).")]
        [SerializeField] float m_FollowSharpness = 30f;
        [SerializeField] float m_MaxTilt = 25f;

        Vector3 m_Target;
        Vector3 m_Velocity;
        Vector3 m_LastPosition;

        public int Player => m_Player;
        public Vector3 Position => transform.position;
        public Vector3 Velocity => m_Velocity;
        public float FollowSharpness { get => m_FollowSharpness; set => m_FollowSharpness = value; }

        public Vector3 HeldBallPosition
        {
            get
            {
                Vector3 p = transform.position;
                p.z -= TableSpec.SideSign(m_Player) * 0.1f;
                p.y = Mathf.Clamp(p.y, TableSpec.TopY + 0.12f, TableSpec.TopY + 0.5f);
                return p;
            }
        }

        void Awake()
        {
            ResetToHome();
        }

        public void ResetToHome()
        {
            m_Target = TableSpec.ClampRacket(new Vector3(0f, TableSpec.TopY + 0.22f, 0f), m_Player);
            transform.position = m_Target;
            m_LastPosition = m_Target;
            m_Velocity = Vector3.zero;
            ApplyRotation();
        }

        public void SetTarget(Vector3 target)
        {
            m_Target = TableSpec.ClampRacket(target, m_Player);
        }

        /// <summary>
        /// Tests both the drawn position and the target the player is steering toward this frame, so the smoothing
        /// lag (and low frame rates) never cost a hit the player actually lined up.
        /// </summary>
        public bool IsInHitZone(Vector3 ballPosition) =>
            InZone(ballPosition - transform.position) || InZone(ballPosition - m_Target);

        static bool InZone(Vector3 d)
        {
            float nx = d.x / HitHalfExtents.x;
            float ny = d.y / HitHalfExtents.y;
            return nx * nx + ny * ny <= 1f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            transform.position = Vector3.Lerp(transform.position, m_Target, 1f - Mathf.Exp(-m_FollowSharpness * dt));
            Vector3 instant = (transform.position - m_LastPosition) / dt;
            m_Velocity = Vector3.Lerp(m_Velocity, instant, 1f - Mathf.Exp(-dt / 0.06f));
            m_LastPosition = transform.position;
            ApplyRotation();
        }

        void ApplyRotation()
        {
            // Blade faces the net; lean it with sideways motion and position for a bit of life.
            float side = TableSpec.SideSign(m_Player);
            Quaternion facing = Quaternion.LookRotation(new Vector3(0f, 0f, -side), Vector3.up);
            float roll = Mathf.Clamp(-m_Velocity.x * side * 6f + transform.position.x * side * 15f, -m_MaxTilt, m_MaxTilt);
            float pitch = Mathf.Clamp(-m_Velocity.y * 5f, -m_MaxTilt, m_MaxTilt);
            transform.rotation = facing * Quaternion.Euler(pitch, 0f, roll);
        }
    }
}
