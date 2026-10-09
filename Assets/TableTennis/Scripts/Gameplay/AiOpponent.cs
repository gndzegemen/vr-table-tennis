using UnityEngine;

namespace TableTennis
{
    public enum AiDifficulty
    {
        Easy,
        Normal,
        Hard,
    }

    /// <summary>Computer opponent for single player. Drives a racket and decides its shots and serves.</summary>
    public class AiOpponent
    {
        readonly int m_Player;
        readonly float m_Speed;
        readonly float m_AimError;
        readonly float m_MistakeChance;
        readonly float m_Reaction;
        readonly Vector2 m_FlightTime;

        int m_PlannedSeq = -1;
        Vector3 m_Target;
        double m_MoveAfter;

        public float ServeDelay { get; }

        public AiOpponent(int player, AiDifficulty difficulty)
        {
            m_Player = player;
            switch (difficulty)
            {
                case AiDifficulty.Easy:
                    m_Speed = 2.0f; m_AimError = 0.11f; m_MistakeChance = 0.10f; m_Reaction = 0.30f;
                    m_FlightTime = new Vector2(0.85f, 1.05f); ServeDelay = 1.4f;
                    break;
                case AiDifficulty.Hard:
                    m_Speed = 4.5f; m_AimError = 0.035f; m_MistakeChance = 0.02f; m_Reaction = 0.10f;
                    m_FlightTime = new Vector2(0.5f, 0.75f); ServeDelay = 0.8f;
                    break;
                default:
                    m_Speed = 3.0f; m_AimError = 0.07f; m_MistakeChance = 0.05f; m_Reaction = 0.18f;
                    m_FlightTime = new Vector2(0.65f, 0.9f); ServeDelay = 1.1f;
                    break;
            }
            m_Target = Home;
        }

        Vector3 Home => TableSpec.ClampRacket(new Vector3(0f, TableSpec.TopY + 0.25f, 0f), m_Player);

        /// <summary>Re-plans when a new ball flight starts and moves the racket toward the plan.</summary>
        public void Tick(Racket racket, int seq, in BallLaunch launch, double now, float dt)
        {
            if (seq != m_PlannedSeq)
            {
                m_PlannedSeq = seq;
                Plan(launch);
                m_MoveAfter = now + m_Reaction;
            }

            if (now < m_MoveAfter)
                return;

            Vector3 next = Vector3.MoveTowards(racket.Position, m_Target, m_Speed * dt);
            racket.SetTarget(next);
        }

        void Plan(in BallLaunch launch)
        {
            if (launch.Kind == LaunchKind.Held)
            {
                m_Target = launch.Hitter == m_Player
                    ? TableSpec.ClampRacket(new Vector3(Random.Range(-0.45f, 0.45f), TableSpec.TopY + Random.Range(0.18f, 0.32f), 0f), m_Player)
                    : Home;
                return;
            }

            if (launch.Receiver == m_Player && BallSimulation.PredictReturnPoint(launch, m_Player, out var crossing))
            {
                Vector2 error = Random.insideUnitCircle * m_AimError;
                m_Target = TableSpec.ClampRacket(crossing.Position + new Vector3(error.x, error.y, 0f), m_Player);
            }
            else
            {
                m_Target = Home;
            }
        }

        public Vector3 ChooseShot(Vector3 from)
        {
            float side = TableSpec.SideSign(m_Player);
            float x = Random.Range(-1f, 1f) * (TableSpec.HalfWidth - 0.15f);
            float depth = Random.Range(0.45f, 0.88f);
            if (Random.value < m_MistakeChance)
            {
                // An unforced error: wide or long.
                if (Random.value < 0.5f) x = Mathf.Sign(x) * (TableSpec.HalfWidth + Random.Range(0.05f, 0.3f));
                else depth = Random.Range(1.05f, 1.3f);
            }
            var target = new Vector3(x, TableSpec.TopY, -side * depth * TableSpec.HalfLength);
            return ShotSolver.SolveDrive(from, target, Random.Range(m_FlightTime.x, m_FlightTime.y));
        }

        public Vector3 ChooseServe(Vector3 from)
        {
            float side = TableSpec.SideSign(m_Player);
            var target = new Vector3(Random.Range(-0.5f, 0.5f), TableSpec.TopY, -side * Random.Range(0.45f, 0.8f) * TableSpec.HalfLength);
            return ShotSolver.SolveServe(from, target, m_Player);
        }
    }
}
