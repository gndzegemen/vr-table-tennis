using System;
using Unity.Netcode;
using UnityEngine;

namespace TableTennis
{
    /// <summary>
    /// Host-authoritative match state. Spawned by the host for every session (single player included,
    /// where it runs over <c>SinglePlayerTransport</c>). Players submit launches and point reports through
    /// RPCs; the host validates them against the current state and replicates the result.
    /// </summary>
    public class MatchNetwork : NetworkBehaviour
    {
        const float PointPauseSeconds = 1.8f;

        public static MatchNetwork Instance { get; private set; }
        public static event Action<MatchNetwork> Spawned;

        /// <summary>Raised on the receiving peer when the remote player's racket position arrives.</summary>
        public event Action<int, Vector3> RacketReceived;

        readonly NetworkVariable<MatchState> m_State = new NetworkVariable<MatchState>();

        /// <summary>Set by the host before play: in single player the host controls both rackets.</summary>
        public bool HostControlsBothPlayers { get; set; }

        public MatchState State => m_State.Value;

        public double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Spawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer)
                return;

            var s = m_State.Value;
            if (s.Phase == MatchPhase.PointOver && Now - s.PhaseStartTime >= PointPauseSeconds)
            {
                if (s.Winner >= 0)
                {
                    s.Phase = MatchPhase.GameOver;
                    s.PhaseStartTime = Now;
                    m_State.Value = s;
                }
                else
                {
                    ServerEnterServe(s);
                }
            }
        }

        // ---------------------------------------------------------------- host API

        public void ServerWaitForOpponent()
        {
            var s = m_State.Value;
            s.Phase = MatchPhase.WaitingForOpponent;
            s.PhaseStartTime = Now;
            m_State.Value = s;
        }

        public void ServerBeginMatch(int firstServer)
        {
            var s = m_State.Value;
            s.Score0 = 0;
            s.Score1 = 0;
            s.FirstServer = (byte)firstServer;
            s.LastPointReason = PointReason.None;
            ServerEnterServe(s);
        }

        void ServerEnterServe(MatchState s)
        {
            s.Phase = MatchPhase.WaitingForServe;
            s.BallSeq++;
            s.Ball = new BallLaunch { Kind = LaunchKind.Held, Hitter = (byte)s.CurrentServer, Time = Now };
            s.PhaseStartTime = Now;
            m_State.Value = s;
        }

        bool CanControl(ulong sender, int player)
        {
            bool fromHost = sender == NetworkManager.ServerClientId;
            if (HostControlsBothPlayers)
                return fromHost;
            return (player == 0) == fromHost;
        }

        // ---------------------------------------------------------------- RPCs

        /// <summary>A serve or a return by <see cref="BallLaunch.Hitter"/>, valid only against <paramref name="expectedSeq"/>.</summary>
        [Rpc(SendTo.Server)]
        public void RequestLaunchRpc(BallLaunch launch, int expectedSeq, RpcParams rpcParams = default)
        {
            if (!CanControl(rpcParams.Receive.SenderClientId, launch.Hitter))
                return;

            var s = m_State.Value;
            if (s.BallSeq != expectedSeq)
                return;

            bool valid = launch.Kind switch
            {
                LaunchKind.Serve => s.Phase == MatchPhase.WaitingForServe && launch.Hitter == s.Ball.Hitter,
                LaunchKind.Hit => s.Phase == MatchPhase.Rally && launch.Hitter == s.Ball.Receiver,
                _ => false,
            };
            if (!valid)
                return;

            s.BallSeq++;
            s.Ball = launch;
            s.Phase = MatchPhase.Rally;
            s.PhaseStartTime = launch.Time;
            m_State.Value = s;
        }

        /// <summary>Sent by the receiver's peer when its rally judge decides the point.</summary>
        [Rpc(SendTo.Server)]
        public void ReportPointRpc(int winner, PointReason reason, int seq, RpcParams rpcParams = default)
        {
            var s = m_State.Value;
            if (s.Phase != MatchPhase.Rally || s.BallSeq != seq || (winner != 0 && winner != 1))
                return;
            if (!CanControl(rpcParams.Receive.SenderClientId, s.Ball.Receiver))
                return;

            if (winner == 0) s.Score0++;
            else s.Score1++;
            s.LastPointWinner = (byte)winner;
            s.LastPointReason = reason;
            s.Phase = MatchPhase.PointOver;
            s.PhaseStartTime = Now;
            m_State.Value = s;
        }

        [Rpc(SendTo.Server)]
        public void RequestRematchRpc()
        {
            var s = m_State.Value;
            if (s.Phase != MatchPhase.GameOver)
                return;
            ServerBeginMatch(1 - s.FirstServer);
        }

        [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
        public void RacketRpc(byte player, Vector3 position)
        {
            RacketReceived?.Invoke(player, position);
        }
    }
}
