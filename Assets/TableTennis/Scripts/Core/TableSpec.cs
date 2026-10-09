using UnityEngine;

namespace TableTennis
{
    /// <summary>
    /// Fixed playfield dimensions. The table is centred on the world origin with its length along Z.
    /// Player 0 stands at -Z, player 1 at +Z.
    /// </summary>
    public static class TableSpec
    {
        public const float TopY = 0.785f;
        public const float HalfLength = 1.37f;
        public const float HalfWidth = 0.7625f;
        public const float NetHeight = 0.1525f;
        public const float NetHalfSpan = HalfWidth + 0.1525f;
        public const float BallRadius = 0.02f;

        public const float Gravity = 9.81f;
        public const float TableRestitution = 0.88f;
        public const float FloorRestitution = 0.55f;
        public const float RoomHalfExtent = 4.9f;

        /// <summary>Distance of each player's hitting plane behind their end line.</summary>
        public const float RacketPlaneOffset = 0.22f;

        public const float RacketMinY = TopY - 0.05f;
        public const float RacketMaxY = TopY + 0.85f;
        public const float RacketMaxX = 1.15f;

        /// <summary>-1 for player 0 (near -Z end), +1 for player 1.</summary>
        public static float SideSign(int player) => player == 0 ? -1f : 1f;

        public static int SideOf(float z) => z < 0f ? 0 : 1;

        public static float RacketPlaneZ(int player) => SideSign(player) * (HalfLength + RacketPlaneOffset);

        public static Vector3 ClampRacket(Vector3 p, int player)
        {
            p.x = Mathf.Clamp(p.x, -RacketMaxX, RacketMaxX);
            p.y = Mathf.Clamp(p.y, RacketMinY, RacketMaxY);
            p.z = RacketPlaneZ(player);
            return p;
        }
    }
}
