using System.Collections.Generic;
using UnityEngine;

namespace TableTennis
{
    public enum ContactType : byte
    {
        Table,
        Net,
        Floor,
        Wall,
        /// <summary>Ball crossed a player's hitting plane behind their end line.</summary>
        RacketPlane,
    }

    public struct BallContact
    {
        public ContactType Type;
        /// <summary>Table half or racket plane owner (0 or 1); -1 when not applicable.</summary>
        public int Side;
        public double Time;
        public Vector3 Position;
        public Vector3 Velocity;
    }

    /// <summary>
    /// Deterministic fixed-step ball flight. Every peer runs the same steps from the same
    /// <see cref="BallLaunch"/>, so the ball needs no continuous network synchronisation.
    /// </summary>
    public class BallSimulation
    {
        public const double StepSeconds = 1.0 / 240.0;
        const int MaxCatchUpSteps = 240 * 12;

        Vector3 m_Position;
        Vector3 m_Velocity;
        double m_Time;
        bool m_HitFloor;
        bool m_HitWall;

        public Vector3 Position => m_Position;
        public Vector3 Velocity => m_Velocity;
        public double Time => m_Time;

        public void Reset(in BallLaunch launch)
        {
            m_Position = launch.Position;
            m_Velocity = launch.Velocity;
            m_Time = launch.Time;
            m_HitFloor = false;
            m_HitWall = false;
        }

        /// <summary>Steps the flight up to <paramref name="time"/>, appending contacts in order.</summary>
        public void AdvanceTo(double time, List<BallContact> contacts)
        {
            int steps = 0;
            while (m_Time + StepSeconds <= time && steps < MaxCatchUpSteps)
            {
                Step(contacts);
                steps++;
            }

            // Far behind (e.g. a tab that was in the background): skip ahead without simulating.
            if (steps == MaxCatchUpSteps && m_Time + StepSeconds <= time)
                m_Time = time;
        }

        /// <summary>Position at <paramref name="time"/>, extrapolated from the last fixed step.</summary>
        public Vector3 PositionAt(double time)
        {
            float dt = Mathf.Clamp((float)(time - m_Time), 0f, (float)StepSeconds);
            return m_Position + m_Velocity * dt;
        }

        public void Step(List<BallContact> contacts)
        {
            float dt = (float)StepSeconds;
            Vector3 prev = m_Position;
            m_Velocity.y -= TableSpec.Gravity * dt;
            Vector3 next = prev + m_Velocity * dt;
            double nextTime = m_Time + StepSeconds;

            // Racket planes (one per player) are checked on the raw path before collisions alter it.
            for (int player = 0; player < 2; player++)
            {
                float planeZ = TableSpec.RacketPlaneZ(player);
                if ((prev.z - planeZ) * (next.z - planeZ) <= 0f && prev.z != next.z && m_Velocity.z * TableSpec.SideSign(player) > 0f)
                {
                    float f = (planeZ - prev.z) / (next.z - prev.z);
                    contacts?.Add(new BallContact
                    {
                        Type = ContactType.RacketPlane,
                        Side = player,
                        Time = m_Time + StepSeconds * f,
                        Position = Vector3.Lerp(prev, next, f),
                        Velocity = m_Velocity,
                    });
                }
            }

            // Net: thin vertical sheet at z = 0.
            if ((prev.z < 0f) != (next.z < 0f))
            {
                float f = -prev.z / (next.z - prev.z);
                Vector3 at = Vector3.Lerp(prev, next, f);
                if (Mathf.Abs(at.x) <= TableSpec.NetHalfSpan &&
                    at.y - TableSpec.BallRadius <= TableSpec.TopY + TableSpec.NetHeight &&
                    at.y + TableSpec.BallRadius >= TableSpec.TopY)
                {
                    float back = prev.z < 0f ? -1f : 1f;
                    next = new Vector3(at.x, at.y, back * TableSpec.BallRadius);
                    m_Velocity = new Vector3(m_Velocity.x * 0.5f, m_Velocity.y * 0.4f, -m_Velocity.z * 0.15f);
                    contacts?.Add(new BallContact { Type = ContactType.Net, Side = -1, Time = m_Time + StepSeconds * f, Position = next, Velocity = m_Velocity });
                }
            }

            // Table top.
            float tableContactY = TableSpec.TopY + TableSpec.BallRadius;
            if (m_Velocity.y < 0f && prev.y >= tableContactY && next.y < tableContactY)
            {
                float f = (prev.y - tableContactY) / (prev.y - next.y);
                Vector3 at = Vector3.Lerp(prev, next, f);
                if (Mathf.Abs(at.x) <= TableSpec.HalfWidth && Mathf.Abs(at.z) <= TableSpec.HalfLength)
                {
                    m_Velocity.y = -m_Velocity.y * TableSpec.TableRestitution;
                    next = new Vector3(next.x, tableContactY, next.z);
                    contacts?.Add(new BallContact { Type = ContactType.Table, Side = TableSpec.SideOf(at.z), Time = m_Time + StepSeconds * f, Position = at, Velocity = m_Velocity });
                }
            }

            // Floor.
            if (next.y < TableSpec.BallRadius)
            {
                next.y = TableSpec.BallRadius;
                if (m_Velocity.y < 0f)
                {
                    if (!m_HitFloor)
                        contacts?.Add(new BallContact { Type = ContactType.Floor, Side = TableSpec.SideOf(next.z), Time = nextTime, Position = next, Velocity = m_Velocity });
                    m_HitFloor = true;
                    m_Velocity.y = -m_Velocity.y * TableSpec.FloorRestitution;
                    m_Velocity.x *= 0.8f;
                    m_Velocity.z *= 0.8f;
                    if (m_Velocity.y < 0.4f) m_Velocity.y = 0f;
                }
                // Rolling friction.
                m_Velocity.x *= 1f - 1.5f * dt;
                m_Velocity.z *= 1f - 1.5f * dt;
            }

            // Room walls.
            if (Mathf.Abs(next.x) > TableSpec.RoomHalfExtent || Mathf.Abs(next.z) > TableSpec.RoomHalfExtent)
            {
                if (Mathf.Abs(next.x) > TableSpec.RoomHalfExtent)
                {
                    next.x = Mathf.Sign(next.x) * TableSpec.RoomHalfExtent;
                    m_Velocity.x = -m_Velocity.x * 0.5f;
                }
                if (Mathf.Abs(next.z) > TableSpec.RoomHalfExtent)
                {
                    next.z = Mathf.Sign(next.z) * TableSpec.RoomHalfExtent;
                    m_Velocity.z = -m_Velocity.z * 0.5f;
                }
                if (!m_HitWall)
                    contacts?.Add(new BallContact { Type = ContactType.Wall, Side = TableSpec.SideOf(next.z), Time = nextTime, Position = next, Velocity = m_Velocity });
                m_HitWall = true;
            }

            m_Position = next;
            m_Time = nextTime;
        }

        static readonly List<BallContact> s_PredictContacts = new List<BallContact>(16);
        static readonly BallSimulation s_Predictor = new BallSimulation();

        /// <summary>
        /// Predicts where a ball launched by <paramref name="launch"/> will cross <paramref name="player"/>'s
        /// racket plane after bouncing exactly once on that player's half. Returns false if it will not.
        /// </summary>
        public static bool PredictReturnPoint(in BallLaunch launch, int player, out BallContact crossing)
        {
            s_Predictor.Reset(launch);
            int bouncesOnSide = 0;
            for (int i = 0; i < 240 * 4; i++)
            {
                s_PredictContacts.Clear();
                s_Predictor.Step(s_PredictContacts);
                foreach (var c in s_PredictContacts)
                {
                    switch (c.Type)
                    {
                        case ContactType.Table when c.Side == player:
                            bouncesOnSide++;
                            if (bouncesOnSide > 1) { crossing = default; return false; }
                            break;
                        case ContactType.RacketPlane when c.Side == player:
                            crossing = c;
                            if (bouncesOnSide == 1) return true;
                            return false;
                        case ContactType.Floor:
                        case ContactType.Wall:
                            crossing = default;
                            return false;
                    }
                }
            }
            crossing = default;
            return false;
        }
    }
}
