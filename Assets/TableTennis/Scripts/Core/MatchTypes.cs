using System;
using Unity.Netcode;
using UnityEngine;

namespace TableTennis
{
    public enum LaunchKind : byte
    {
        /// <summary>Ball is held in front of the serving player's racket.</summary>
        Held = 0,
        Serve = 1,
        Hit = 2,
    }

    public enum MatchPhase : byte
    {
        WaitingForOpponent = 0,
        WaitingForServe = 1,
        Rally = 2,
        PointOver = 3,
        GameOver = 4,
    }

    public enum PointReason : byte
    {
        None = 0,
        Out = 1,
        Net = 2,
        Missed = 3,
        DoubleBounce = 4,
        ServeFault = 5,
        OwnSide = 6,
    }

    /// <summary>
    /// The full description of a ball flight: everything after it follows deterministically from
    /// <see cref="BallSimulation"/>, so only these launches travel over the network.
    /// </summary>
    [Serializable]
    public struct BallLaunch : INetworkSerializable, IEquatable<BallLaunch>
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public double Time;
        public byte Hitter;
        public LaunchKind Kind;

        public int Receiver => 1 - Hitter;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref Time);
            serializer.SerializeValue(ref Hitter);
            serializer.SerializeValue(ref Kind);
        }

        public bool Equals(BallLaunch other) =>
            Position == other.Position && Velocity == other.Velocity && Time.Equals(other.Time) &&
            Hitter == other.Hitter && Kind == other.Kind;

        public override bool Equals(object obj) => obj is BallLaunch other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Position, Velocity, Time, Hitter, Kind);
    }

    /// <summary>Authoritative match state, owned by the host and replicated to the client.</summary>
    [Serializable]
    public struct MatchState : INetworkSerializable, IEquatable<MatchState>
    {
        public const int PointsToWin = 11;

        public MatchPhase Phase;
        public int BallSeq;
        public BallLaunch Ball;
        public byte Score0;
        public byte Score1;
        public byte FirstServer;
        public byte LastPointWinner;
        public PointReason LastPointReason;
        public double PhaseStartTime;

        public int Score(int player) => player == 0 ? Score0 : Score1;

        public int CurrentServer
        {
            get
            {
                int total = Score0 + Score1;
                // Serve changes every two points, and every point once both players reach 10.
                int turns = (Score0 >= PointsToWin - 1 && Score1 >= PointsToWin - 1) ? total : total / 2;
                return (FirstServer + turns) % 2;
            }
        }

        public int Winner
        {
            get
            {
                if (Score0 >= PointsToWin && Score0 - Score1 >= 2) return 0;
                if (Score1 >= PointsToWin && Score1 - Score0 >= 2) return 1;
                return -1;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref BallSeq);
            serializer.SerializeValue(ref Ball);
            serializer.SerializeValue(ref Score0);
            serializer.SerializeValue(ref Score1);
            serializer.SerializeValue(ref FirstServer);
            serializer.SerializeValue(ref LastPointWinner);
            serializer.SerializeValue(ref LastPointReason);
            serializer.SerializeValue(ref PhaseStartTime);
        }

        public bool Equals(MatchState o) =>
            Phase == o.Phase && BallSeq == o.BallSeq && Ball.Equals(o.Ball) && Score0 == o.Score0 &&
            Score1 == o.Score1 && FirstServer == o.FirstServer && LastPointWinner == o.LastPointWinner &&
            LastPointReason == o.LastPointReason && PhaseStartTime.Equals(o.PhaseStartTime);

        public override bool Equals(object obj) => obj is MatchState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Phase, BallSeq, Score0, Score1, PhaseStartTime);
    }
}
