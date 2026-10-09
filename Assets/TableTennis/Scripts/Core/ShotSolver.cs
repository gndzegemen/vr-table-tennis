using UnityEngine;

namespace TableTennis
{
    /// <summary>Computes launch velocities that land the ball on a chosen point (ballistic, no spin or drag).</summary>
    public static class ShotSolver
    {
        const float NetClearance = TableSpec.BallRadius + 0.035f;
        const float LandingY = TableSpec.TopY + TableSpec.BallRadius;

        /// <summary>
        /// Velocity that carries the ball from <paramref name="from"/> to <paramref name="target"/> (on the table
        /// surface) in about <paramref name="flightTime"/> seconds, lengthening the flight until the arc clears the net.
        /// </summary>
        public static Vector3 SolveDrive(Vector3 from, Vector3 target, float flightTime)
        {
            target.y = LandingY;
            float t = Mathf.Max(0.25f, flightTime);
            Vector3 v = Ballistic(from, target, t);
            for (int i = 0; i < 40 && !ClearsNet(from, v); i++)
            {
                t += 0.04f;
                v = Ballistic(from, target, t);
            }
            return v;
        }

        /// <summary>
        /// Serve: the ball must bounce once on the server's half, then on the receiver's half near <paramref name="target"/>.
        /// </summary>
        public static Vector3 SolveServe(Vector3 from, Vector3 target, int server)
        {
            target.y = LandingY;
            float h = Mathf.Max(0.05f, from.y - LandingY);
            Vector3 flat = target - from;
            flat.y = 0f;
            float distance = flat.magnitude;
            Vector3 dir = flat / Mathf.Max(distance, 0.001f);
            float sign = TableSpec.SideSign(server);
            float g = TableSpec.Gravity;

            Vector3 best = Vector3.zero;
            float bestScore = float.MaxValue;
            // Try a range of initial vertical speeds; keep the one whose first bounce sits nicely in the server's half.
            for (float vy0 = -2.5f; vy0 <= 1.5f; vy0 += 0.1f)
            {
                // Time to fall h with initial vertical speed vy0 (positive is up).
                float disc = vy0 * vy0 + 2f * g * h;
                float t1 = (vy0 + Mathf.Sqrt(disc)) / g;
                float impactVy = vy0 - g * t1;
                float upAfter = -impactVy * TableSpec.TableRestitution;
                float t2 = 2f * upAfter / g;
                float u = distance / (t1 + t2);
                if (u < 1.5f || u > 9f) continue;

                Vector3 firstBounce = from + dir * (u * t1);
                float depthInOwnHalf = firstBounce.z * sign; // positive inside own half
                if (depthInOwnHalf < 0.15f || depthInOwnHalf > TableSpec.HalfLength - 0.05f) continue;

                // Net clearance after the first bounce.
                float distToNet = Mathf.Abs(firstBounce.z) / Mathf.Max(Mathf.Abs(dir.z), 0.01f);
                float tn = distToNet / u;
                float yAtNet = LandingY + upAfter * tn - 0.5f * g * tn * tn;
                if (yAtNet < TableSpec.TopY + TableSpec.NetHeight + NetClearance) continue;

                // Prefer a low, quick serve with the first bounce around the middle of the own half.
                float score = Mathf.Abs(depthInOwnHalf - TableSpec.HalfLength * 0.55f) + (yAtNet - TableSpec.TopY) * 0.8f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new Vector3(dir.x * u, vy0, dir.z * u);
                }
            }

            if (bestScore < float.MaxValue)
                return best;

            // Fallback: a gentle lob straight to the target (counts as a fault if it skips the own-half bounce).
            return SolveDrive(from, target, 0.9f);
        }

        static Vector3 Ballistic(Vector3 from, Vector3 to, float t)
        {
            Vector3 d = to - from;
            return new Vector3(d.x / t, (d.y + 0.5f * TableSpec.Gravity * t * t) / t, d.z / t);
        }

        static bool ClearsNet(Vector3 from, Vector3 v)
        {
            if (Mathf.Abs(v.z) < 0.01f)
                return true;
            float tn = -from.z / v.z;
            if (tn <= 0f) return true; // moving away from the net
            float y = from.y + v.y * tn - 0.5f * TableSpec.Gravity * tn * tn;
            return y >= TableSpec.TopY + TableSpec.NetHeight + NetClearance;
        }
    }
}
