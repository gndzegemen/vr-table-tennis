using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.SinglePlayer;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TableTennis
{
    /// <summary>
    /// Runs a session (single player against the AI, hosting, or joining) and drives the local side of the match:
    /// mouse and AI rackets, the deterministic ball flight, hit detection and the rally judge.
    /// </summary>
    public class TableTennisGame : MonoBehaviour
    {
        enum SessionMode
        {
            None,
            SinglePlayer,
            Host,
            Client,
        }

        const ushort EditorPort = 7777;
        const float RacketSendInterval = 1f / 30f;
        const string RoomCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        const string InviteQueryKey = "oda";

        [Header("Networking")]
        [SerializeField] NetworkManager m_NetworkManager;
        [SerializeField] UnityTransport m_UnityTransport;
        [SerializeField] WebRtcTransport m_WebRtcTransport;
        [SerializeField] SinglePlayerTransport m_SinglePlayerTransport;
        [SerializeField] GameObject m_MatchPrefab;

        [Header("Scene")]
        [SerializeField] Camera m_Camera;
        [SerializeField] Racket[] m_Rackets = new Racket[2];
        [SerializeField] Transform m_Ball;
        [SerializeField] Transform m_BallShadow;
        [SerializeField] Transform m_ReturnMarker;
        [SerializeField] TrailRenderer m_BallTrail;
        [SerializeField] GameUI m_UI;
        [SerializeField] GameAudio m_Audio;

        [Header("Testing")]
        [Tooltip("Editor/development builds only: an AI plays the local racket. Also enabled by ?autopilot=1 in the page URL.")]
        [SerializeField] bool m_DebugAutoPilot;

        SessionMode m_Mode;
        int m_LocalPlayer;
        // AI brains per player: the single player opponent, plus the optional debug autopilot.
        readonly AiOpponent[] m_Brains = new AiOpponent[2];
        bool m_Leaving;
        string m_RoomCode;
        MatchNetwork m_Match;

        // Ball flight currently simulated.
        readonly BallSimulation m_Sim = new BallSimulation();
        readonly RallyJudge m_Judge = new RallyJudge();
        readonly List<BallContact> m_Contacts = new List<BallContact>(16);
        int m_SimSeq = -1;
        bool m_PointReported;

        // A launch we sent but the host has not confirmed yet (shown immediately to hide latency).
        bool m_HasPending;
        int m_PendingSeq;
        BallLaunch m_PendingLaunch;
        float m_PendingSince;

        // Presentation bookkeeping.
        MatchPhase m_LastPhase = (MatchPhase)255;
        double m_LastPointTime = -1;
        float m_NextRacketSend;
        bool m_RemoteRacketSeen;

        bool IsNetworkRunning => m_NetworkManager != null && m_NetworkManager.IsListening;

        void Awake()
        {
            if (m_NetworkManager == null || m_MatchPrefab == null || m_Camera == null || m_UI == null || m_Ball == null ||
                m_Rackets == null || m_Rackets.Length != 2 || m_Rackets[0] == null || m_Rackets[1] == null)
            {
                Debug.LogError("TableTennisGame is missing scene references; run Tools > Table Tennis > Build Scene.", this);
                enabled = false;
                return;
            }

            // Normally already registered through NGO's DefaultNetworkPrefabs list.
            if (!m_NetworkManager.NetworkConfig.Prefabs.Contains(m_MatchPrefab))
                m_NetworkManager.AddNetworkPrefab(m_MatchPrefab);
            m_NetworkManager.NetworkConfig.ConnectionApproval = true;
            m_NetworkManager.ConnectionApprovalCallback = ApproveConnection;
            m_NetworkManager.OnClientConnectedCallback += OnClientConnected;
            m_NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            m_NetworkManager.OnTransportFailure += OnTransportFailure;
            MatchNetwork.Spawned += OnMatchSpawned;
            if (m_WebRtcTransport != null)
                m_WebRtcTransport.RoomOpened += OnRoomOpened;

            m_UI.SinglePlayerRequested += StartSinglePlayer;
            m_UI.HostRequested += StartHosting;
            m_UI.JoinRequested += Join;
            m_UI.LeaveRequested += () => Leave(string.Empty);
            m_UI.RematchRequested += RequestRematch;
            m_UI.CopyInviteRequested += CopyInvite;

            Application.runInBackground = true;
        }

        void Start()
        {
            ShowMenu(string.Empty);
            string invite = ReadQueryParameter(InviteQueryKey)?.ToUpperInvariant();
            if (!string.IsNullOrEmpty(invite))
                m_UI.ShowMainMenu("Davet kodu bulundu. Katılmak için \"Katıl\"a bas.", invite);

            // Development builds only: hooks for automated two-tab tests.
            if (Debug.isDebugBuild)
            {
                string autoHost = ReadQueryParameter("autohost");
                if (!string.IsNullOrEmpty(autoHost))
                    StartHosting(autoHost.ToUpperInvariant());
                else if (!string.IsNullOrEmpty(invite) && ReadQueryParameter("autojoin") == "1")
                    Join(invite);
            }
        }

        void OnDestroy()
        {
            MatchNetwork.Spawned -= OnMatchSpawned;
            if (m_NetworkManager != null)
            {
                m_NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                m_NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                m_NetworkManager.OnTransportFailure -= OnTransportFailure;
            }
        }

        // ================================================================== sessions

        void StartSinglePlayer(AiDifficulty difficulty)
        {
            if (!PrepareStart()) return;
            m_Mode = SessionMode.SinglePlayer;
            m_LocalPlayer = 0;
            ApplyAutoPilot();
            m_Brains[1] = new AiOpponent(1, difficulty);
            m_NetworkManager.NetworkConfig.NetworkTransport = m_SinglePlayerTransport;
            if (!m_NetworkManager.StartHost())
            {
                Leave("Oyun başlatılamadı.");
                return;
            }
            SpawnMatch(hostControlsBoth: true);
            m_Match.ServerBeginMatch(0);
            string level = difficulty switch { AiDifficulty.Easy => "KOLAY", AiDifficulty.Hard => "ZOR", _ => "NORMAL" };
            EnterGameView("SEN", "BİLGİSAYAR (" + level + ")");
        }

        void StartHosting() => StartHosting(GenerateRoomCode());

        void StartHosting(string roomCode)
        {
            if (!PrepareStart()) return;
            m_Mode = SessionMode.Host;
            m_LocalPlayer = 0;
            ApplyAutoPilot();
            m_RoomCode = roomCode;

            bool useWebRtc = m_WebRtcTransport != null && m_WebRtcTransport.IsSupported;
            if (useWebRtc)
            {
                m_WebRtcTransport.RoomCode = m_RoomCode;
                m_NetworkManager.NetworkConfig.NetworkTransport = m_WebRtcTransport;
            }
            else
            {
                m_UnityTransport.SetConnectionData("127.0.0.1", EditorPort, "0.0.0.0");
                m_NetworkManager.NetworkConfig.NetworkTransport = m_UnityTransport;
            }

            if (!m_NetworkManager.StartHost())
            {
                Leave("Oda kurulamadı.");
                return;
            }
            SpawnMatch(hostControlsBoth: false);
            m_Match.ServerWaitForOpponent();

            if (useWebRtc)
            {
                m_UI.ShowLobby(m_RoomCode, "Kodu arkadaşına gönder ya da davet linkini paylaş.", true);
                m_UI.SetLobbyStatus("Oda hazırlanıyor…");
            }
            else
            {
                m_UI.ShowLobby("EDİTÖR", "Editör/masaüstü testi: UnityTransport, port " + EditorPort +
                    ".\nDiğer oyuncu kod alanına bu bilgisayarın IP'sini (ör. 127.0.0.1) yazsın.", false);
            }
            PlaceCamera(0);
        }

        void Join(string code)
        {
            code = (code ?? string.Empty).Trim().ToUpperInvariant();
            bool useWebRtc = m_WebRtcTransport != null && m_WebRtcTransport.IsSupported;
            if (useWebRtc && code.Length < 4)
            {
                m_UI.ShowMainMenu("Lütfen geçerli bir oda kodu gir.");
                return;
            }
            if (!PrepareStart()) return;
            m_Mode = SessionMode.Client;
            m_LocalPlayer = 1;
            ApplyAutoPilot();

            if (useWebRtc)
            {
                m_WebRtcTransport.RoomCode = code;
                m_NetworkManager.NetworkConfig.NetworkTransport = m_WebRtcTransport;
            }
            else
            {
                string address = code.Contains(".") ? code : "127.0.0.1";
                m_UnityTransport.SetConnectionData(address, EditorPort);
                m_NetworkManager.NetworkConfig.NetworkTransport = m_UnityTransport;
            }

            if (!m_NetworkManager.StartClient())
            {
                Leave("Bağlantı başlatılamadı.");
                return;
            }
            m_UI.ShowConnecting(useWebRtc ? "\"" + code + "\" odasına bağlanılıyor…" : "Bağlanılıyor…");
        }

        bool PrepareStart()
        {
            if (m_NetworkManager.IsListening || m_NetworkManager.ShutdownInProgress)
            {
                m_UI.ShowMainMenu("Önceki oturum kapanıyor, lütfen tekrar dene.");
                return false;
            }
            m_Leaving = false;
            m_Match = null;
            m_Brains[0] = m_Brains[1] = null;
            m_HasPending = false;
            m_SimSeq = -1;
            m_LastPhase = (MatchPhase)255;
            m_LastPointTime = -1;
            m_RemoteRacketSeen = false;
            m_Rackets[0].ResetToHome();
            m_Rackets[1].ResetToHome();
            return true;
        }

        void SpawnMatch(bool hostControlsBoth)
        {
            var instance = Instantiate(m_MatchPrefab);
            m_Match = instance.GetComponent<MatchNetwork>();
            m_Match.HostControlsBothPlayers = hostControlsBoth;
            instance.GetComponent<NetworkObject>().Spawn();
        }

        void OnMatchSpawned(MatchNetwork match)
        {
            m_Match = match;
            match.RacketReceived += OnRemoteRacket;
            if (m_Mode == SessionMode.Client)
                EnterGameView("SEN", "RAKİP");
        }

        void EnterGameView(string localName, string remoteName)
        {
            m_UI.ShowHud(localName, remoteName);
            PlaceCamera(m_LocalPlayer);
        }

        void Leave(string message)
        {
            if (m_Leaving) return;
            m_Leaving = true;

            string transportError = m_WebRtcTransport != null ? m_WebRtcTransport.LastError : string.Empty;
            if (m_Match != null)
                m_Match.RacketReceived -= OnRemoteRacket;
            if (m_NetworkManager.IsListening)
                m_NetworkManager.Shutdown();

            m_Mode = SessionMode.None;
            m_Match = null;
            m_Brains[0] = m_Brains[1] = null;
            m_HasPending = false;
            if (string.IsNullOrEmpty(message) && !string.IsNullOrEmpty(transportError))
                message = DescribeTransportError(transportError);
            ShowMenu(message);
        }

        void ShowMenu(string message)
        {
            m_UI.ShowMainMenu(message);
            m_Ball.gameObject.SetActive(false);
            if (m_BallShadow != null) m_BallShadow.gameObject.SetActive(false);
            if (m_ReturnMarker != null) m_ReturnMarker.gameObject.SetActive(false);
            Cursor.visible = true;
        }

        // ================================================================== NGO callbacks

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            bool isHostItself = request.ClientNetworkId == NetworkManager.ServerClientId;
            bool roomFree = m_Mode == SessionMode.Host && m_NetworkManager.ConnectedClientsIds.Count < 2 &&
                            (m_Match == null || m_Match.State.Phase == MatchPhase.WaitingForOpponent);
            response.Approved = isHostItself || roomFree;
            if (!response.Approved)
                response.Reason = "Oda dolu.";
        }

        void OnClientConnected(ulong clientId)
        {
            if (m_Mode == SessionMode.Host && clientId != NetworkManager.ServerClientId && m_Match != null)
            {
                m_Match.ServerBeginMatch(UnityEngine.Random.value < 0.5f ? 0 : 1);
                EnterGameView("SEN", "RAKİP");
            }
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (m_Leaving) return;
            if (m_Mode == SessionMode.Host && clientId != NetworkManager.ServerClientId)
            {
                Leave("Rakip oyundan ayrıldı.");
            }
            else if (m_Mode == SessionMode.Client)
            {
                string reason = m_NetworkManager.DisconnectReason;
                bool wasInGame = m_Match != null;
                if (!string.IsNullOrEmpty(reason)) Leave(reason);
                else if (wasInGame) Leave("Host ile bağlantı koptu.");
                else Leave(string.Empty);
            }
        }

        void OnTransportFailure()
        {
            Leave(string.Empty);
        }

        void OnRoomOpened()
        {
            if (m_Mode == SessionMode.Host)
                m_UI.SetLobbyStatus("Rakip bekleniyor…");
        }

        static string DescribeTransportError(string error)
        {
            switch (error)
            {
                case "peer-unavailable": return "Bu kodla açık bir oda bulunamadı.";
                case "unavailable-id": return "Oda kodu kullanımda, tekrar dene.";
                case "timeout": return "Bağlantı zaman aşımına uğradı.";
                case "network":
                case "server-error":
                case "socket-error":
                case "socket-closed": return "Eşleştirme sunucusuna ulaşılamadı. İnternet bağlantını kontrol et.";
                case "browser-incompatible": return "Tarayıcın WebRTC desteklemiyor.";
                case "library-load-failed": return "Ağ kütüphanesi yüklenemedi.";
                case "connection-closed": return "Bağlantı kurulamadı.";
                default: return "Bağlantı hatası: " + error;
            }
        }

        // ================================================================== per-frame match

        void Update()
        {
            var match = m_Match;
            if (match == null || !match.IsSpawned || m_Mode == SessionMode.None)
                return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !m_UI.IsGameOverVisible &&
                match.State.Phase != MatchPhase.WaitingForOpponent)
                m_UI.SetPauseOpen(!m_UI.IsPauseOpen);

            var state = match.State;
            double now = match.Now;
            float dt = Time.deltaTime;

            UpdateLocalRacket(state);
            for (int p = 0; p < 2; p++)
                m_Brains[p]?.Tick(m_Rackets[p], CurrentSeq(state), CurrentLaunch(state), now, dt);

            UpdateBall(state, now);
            HandleServeInput(state, now);
            UpdatePresentation(state, now);
        }

        int CurrentSeq(in MatchState state) => UsePending(state) ? m_PendingSeq : state.BallSeq;

        BallLaunch CurrentLaunch(in MatchState state) => UsePending(state) ? m_PendingLaunch : state.Ball;

        bool UsePending(in MatchState state)
        {
            if (!m_HasPending) return false;
            if (state.BallSeq >= m_PendingSeq || Time.unscaledTime - m_PendingSince > 1.5f)
            {
                m_HasPending = false;
                return false;
            }
            return true;
        }

        bool ControlsPlayer(int player)
        {
            switch (m_Mode)
            {
                case SessionMode.SinglePlayer: return true;
                case SessionMode.Host: return player == 0;
                case SessionMode.Client: return player == 1;
                default: return false;
            }
        }

        bool IsAi(int player) => m_Brains[player] != null;

        void ApplyAutoPilot()
        {
            bool enabledByUrl = ReadQueryParameter("autopilot") == "1";
            if (Debug.isDebugBuild && (m_DebugAutoPilot || enabledByUrl))
                m_Brains[m_LocalPlayer] = new AiOpponent(m_LocalPlayer, AiDifficulty.Hard);
        }

        void UpdateLocalRacket(in MatchState state)
        {
            var racket = m_Rackets[m_LocalPlayer];
            var mouse = Mouse.current;
            if (mouse != null && !IsAi(m_LocalPlayer) && !m_UI.IsMenuOpen && state.Phase != MatchPhase.WaitingForOpponent)
            {
                Ray ray = m_Camera.ScreenPointToRay(mouse.position.ReadValue());
                var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, TableSpec.RacketPlaneZ(m_LocalPlayer)));
                if (plane.Raycast(ray, out float distance))
                    racket.SetTarget(ray.GetPoint(distance));
            }

            Cursor.visible = m_UI.IsMenuOpen || state.Phase == MatchPhase.WaitingForOpponent;

            if (m_Mode != SessionMode.SinglePlayer && Time.unscaledTime >= m_NextRacketSend)
            {
                m_NextRacketSend = Time.unscaledTime + RacketSendInterval;
                m_Match.RacketRpc((byte)m_LocalPlayer, racket.Position);
            }
        }

        void OnRemoteRacket(int player, Vector3 position)
        {
            if (player < 0 || player > 1 || ControlsPlayer(player))
                return;
            m_Rackets[player].SetTarget(position);
            if (!m_RemoteRacketSeen)
            {
                m_RemoteRacketSeen = true;
                m_Rackets[player].transform.position = TableSpec.ClampRacket(position, player);
            }
        }

        void UpdateBall(in MatchState state, double now)
        {
            int seq = CurrentSeq(state);
            BallLaunch launch = CurrentLaunch(state);

            if (seq != m_SimSeq)
                BeginFlight(seq, launch, now);

            if (launch.Kind == LaunchKind.Held)
            {
                bool showHeld = state.Phase == MatchPhase.WaitingForServe;
                m_Ball.gameObject.SetActive(showHeld);
                if (showHeld)
                    m_Ball.position = m_Rackets[launch.Hitter].HeldBallPosition;
                UpdateShadow();
                return;
            }

            m_Ball.gameObject.SetActive(true);
            m_Contacts.Clear();
            m_Sim.AdvanceTo(now, m_Contacts);

            // Only the receiver's controller judges this flight (it is the only one who can still affect it).
            bool judgeHere = ControlsPlayer(launch.Receiver) && state.Phase == MatchPhase.Rally && seq == state.BallSeq;

            bool returned = false;
            for (int i = 0; i < m_Contacts.Count && !returned; i++)
            {
                var contact = m_Contacts[i];
                PlayContactSound(contact, now);
                if (!judgeHere || m_Judge.Decided)
                    continue;

                if (contact.Type == ContactType.RacketPlane && contact.Side == launch.Receiver)
                {
                    if (m_Judge.ReceiverMayHit && TryReturn(launch.Receiver, contact, state))
                        returned = true;
                    continue;
                }
                m_Judge.Process(contact);
            }

            if (returned)
            {
                // A new flight started from the hit; show it right away.
                UpdateBall(m_Match.State, now);
                return;
            }

            if (judgeHere && m_Judge.Decided && !m_PointReported)
            {
                m_PointReported = true;
                m_Match.ReportPointRpc(m_Judge.Winner, m_Judge.Reason, state.BallSeq);
            }

            m_Ball.position = m_Sim.PositionAt(now);
            UpdateShadow();
        }

        void BeginFlight(int seq, in BallLaunch launch, double now)
        {
            m_SimSeq = seq;
            m_Sim.Reset(launch);
            m_Judge.Reset(launch);
            m_PointReported = false;
            if (m_BallTrail != null && launch.Kind != LaunchKind.Hit)
                m_BallTrail.Clear();

            if (launch.Kind != LaunchKind.Held && now - launch.Time < 0.25)
                m_Audio?.PlayRacket(launch.Kind == LaunchKind.Serve ? 0.7f : 1f);

            // Show the human player where the ball will cross their hitting plane.
            if (m_ReturnMarker != null)
            {
                BallContact crossing = default;
                bool show = launch.Kind != LaunchKind.Held && launch.Receiver == m_LocalPlayer &&
                            BallSimulation.PredictReturnPoint(launch, m_LocalPlayer, out crossing);
                m_ReturnMarker.gameObject.SetActive(show);
                if (show)
                    m_ReturnMarker.position = crossing.Position;
            }
        }

        bool TryReturn(int player, in BallContact contact, in MatchState state)
        {
            var racket = m_Rackets[player];
            if (!racket.IsInHitZone(contact.Position))
                return false;

            Vector3 velocity = IsAi(player) ? m_Brains[player].ChooseShot(contact.Position) : HumanShot(racket, contact.Position);
            var launch = new BallLaunch
            {
                Position = contact.Position,
                Velocity = velocity,
                Time = contact.Time,
                Hitter = (byte)player,
                Kind = LaunchKind.Hit,
            };
            SubmitLaunch(launch, state.BallSeq);
            if (m_ReturnMarker != null)
                m_ReturnMarker.gameObject.SetActive(false);
            return true;
        }

        void SubmitLaunch(in BallLaunch launch, int expectedSeq)
        {
            m_HasPending = true;
            m_PendingSeq = expectedSeq + 1;
            m_PendingLaunch = launch;
            m_PendingSince = Time.unscaledTime;
            m_Match.RequestLaunchRpc(launch, expectedSeq);
        }

        bool SmashHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;

        Vector3 HumanShot(Racket racket, Vector3 from)
        {
            int player = racket.Player;
            float side = TableSpec.SideSign(player);
            Vector3 v = racket.Velocity;
            Vector3 offset = from - racket.Position;
            bool smash = SmashHeld;

            float depth = Mathf.Clamp(0.55f + 0.09f * v.y + (smash ? 0.15f : 0f), 0.2f, 1.15f);
            float x = Mathf.Clamp(from.x * 0.25f + v.x * 0.3f + offset.x * 2.2f, -1.5f, 1.5f);
            float speed = new Vector2(v.x, v.y).magnitude;
            float flight = Mathf.Lerp(0.95f, 0.55f, Mathf.Clamp01(speed / 4f));
            if (smash) flight *= 0.78f;

            var target = new Vector3(x, TableSpec.TopY, -side * depth * TableSpec.HalfLength);
            return ShotSolver.SolveDrive(from, target, flight);
        }

        void HandleServeInput(in MatchState state, double now)
        {
            if (state.Phase != MatchPhase.WaitingForServe || UsePending(state))
                return;

            int server = state.Ball.Hitter;
            if (!ControlsPlayer(server) || now - state.PhaseStartTime < 0.4)
                return;

            var racket = m_Rackets[server];
            Vector3 from = racket.HeldBallPosition;
            Vector3 velocity;

            if (IsAi(server))
            {
                if (now - state.PhaseStartTime < m_Brains[server].ServeDelay)
                    return;
                velocity = m_Brains[server].ChooseServe(from);
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse == null || !mouse.leftButton.wasPressedThisFrame || m_UI.IsMenuOpen)
                    return;
                float side = TableSpec.SideSign(server);
                Vector3 v = racket.Velocity;
                float x = Mathf.Clamp(racket.Position.x * 0.5f + v.x * 0.25f, -TableSpec.HalfWidth * 0.85f, TableSpec.HalfWidth * 0.85f);
                float depth = Mathf.Clamp(0.6f + 0.05f * v.y, 0.35f, 0.9f);
                velocity = ShotSolver.SolveServe(from, new Vector3(x, TableSpec.TopY, -side * depth * TableSpec.HalfLength), server);
            }

            SubmitLaunch(new BallLaunch
            {
                Position = from,
                Velocity = velocity,
                Time = now,
                Hitter = (byte)server,
                Kind = LaunchKind.Serve,
            }, state.BallSeq);
        }

        void PlayContactSound(in BallContact contact, double now)
        {
            if (m_Audio == null || now - contact.Time > 0.2)
                return;
            switch (contact.Type)
            {
                case ContactType.Table: m_Audio.PlayTable(); break;
                case ContactType.Net: m_Audio.PlayNet(); break;
                case ContactType.Floor: m_Audio.PlayFloor(); break;
            }
        }

        void UpdateShadow()
        {
            if (m_BallShadow == null)
                return;
            bool visible = m_Ball.gameObject.activeSelf;
            m_BallShadow.gameObject.SetActive(visible);
            if (!visible)
                return;
            Vector3 p = m_Ball.position;
            bool overTable = Mathf.Abs(p.x) <= TableSpec.HalfWidth && Mathf.Abs(p.z) <= TableSpec.HalfLength && p.y >= TableSpec.TopY;
            float groundY = overTable ? TableSpec.TopY + 0.002f : 0.002f;
            m_BallShadow.position = new Vector3(p.x, groundY, p.z);
            float height = Mathf.Max(0f, p.y - groundY);
            float scale = Mathf.Lerp(0.045f, 0.02f, Mathf.Clamp01(height / 1.2f));
            m_BallShadow.localScale = new Vector3(scale, 0.0005f, scale);
        }

        void UpdatePresentation(in MatchState state, double now)
        {
            int local = m_LocalPlayer;
            int remote = 1 - local;
            m_UI.SetScore(state.Score(local), state.Score(remote));

            if (state.Phase == MatchPhase.PointOver && state.PhaseStartTime != m_LastPointTime)
            {
                m_LastPointTime = state.PhaseStartTime;
                bool mine = state.LastPointWinner == local;
                m_UI.ShowMessage((mine ? "+1 SEN" : "+1 RAKİP") + "\n<size=30>" + DescribeReason(state.LastPointReason, mine) + "</size>",
                    mine ? GameUI.GoodColor : GameUI.BadColor);
                m_Audio?.PlayPoint();
                Debug.Log($"[TableTennis] point P{state.LastPointWinner} ({state.LastPointReason}) score {state.Score0}-{state.Score1}");
                if (m_ReturnMarker != null) m_ReturnMarker.gameObject.SetActive(false);
            }

            switch (state.Phase)
            {
                case MatchPhase.WaitingForServe:
                    bool myServe = state.Ball.Hitter == local;
                    m_UI.SetHint(myServe ? "Servis sende — sol tıkla" : "Rakip servis atıyor…");
                    break;
                case MatchPhase.Rally:
                    m_UI.SetHint(string.Empty);
                    break;
                case MatchPhase.WaitingForOpponent:
                    m_UI.SetHint(string.Empty);
                    break;
            }

            if (state.Phase != m_LastPhase)
            {
                if (state.Phase == MatchPhase.GameOver)
                {
                    bool won = state.Winner == local;
                    bool canRematch = m_Mode != SessionMode.Client;
                    m_UI.ShowGameOver(won, state.Score(local) + " - " + state.Score(remote), canRematch,
                        canRematch ? string.Empty : "Host yeni maçı başlatabilir.");
                    m_UI.SetHint(string.Empty);
                }
                else if (m_LastPhase == MatchPhase.GameOver)
                {
                    m_UI.HideGameOver();
                }
                m_LastPhase = state.Phase;
            }
        }

        static string DescribeReason(PointReason reason, bool forLocalPlayer)
        {
            switch (reason)
            {
                case PointReason.Out: return "Top dışarıda";
                case PointReason.Net: return "File";
                case PointReason.Missed: return forLocalPlayer ? "Rakip karşılayamadı" : "Topu karşılayamadın";
                case PointReason.DoubleBounce: return "Çift sekme";
                case PointReason.ServeFault: return "Servis hatası";
                case PointReason.OwnSide: return "Top kendi sahasına düştü";
                default: return string.Empty;
            }
        }

        void RequestRematch()
        {
            if (m_Match != null && m_Mode != SessionMode.Client)
                m_Match.RequestRematchRpc();
        }

        // ================================================================== helpers

        void PlaceCamera(int player)
        {
            float side = TableSpec.SideSign(player);
            m_Camera.transform.position = new Vector3(0f, TableSpec.TopY + 0.62f, side * (TableSpec.HalfLength + 1.35f));
            m_Camera.transform.LookAt(new Vector3(0f, TableSpec.TopY - 0.1f, -side * 0.35f));
        }

        static string GenerateRoomCode()
        {
            var chars = new char[5];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = RoomCodeAlphabet[UnityEngine.Random.Range(0, RoomCodeAlphabet.Length)];
            return new string(chars);
        }

        string InviteLink
        {
            get
            {
                string url = Application.absoluteURL ?? string.Empty;
                int cut = url.IndexOfAny(new[] { '?', '#' });
                if (cut >= 0) url = url.Substring(0, cut);
                return url + "?" + InviteQueryKey + "=" + m_RoomCode;
            }
        }

        void CopyInvite()
        {
            if (string.IsNullOrEmpty(m_RoomCode))
                return;
            WebRtcTransport.CopyToClipboard(InviteLink);
            m_UI.SetLobbyStatus("Davet linki kopyalandı. Rakip bekleniyor…");
        }

        static string ReadQueryParameter(string key)
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url))
                return null;
            int q = url.IndexOf('?');
            if (q < 0)
                return null;
            string query = url.Substring(q + 1);
            int hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);
            foreach (var part in query.Split('&'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0] == key)
                    return Uri.UnescapeDataString(kv[1]).Trim();
            }
            return null;
        }

        // Slow orbit behind the menus.
        void LateUpdate()
        {
            if (m_Mode != SessionMode.None)
                return;
            float angle = Time.unscaledTime * 6f;
            var offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, -3.4f);
            m_Camera.transform.position = new Vector3(offset.x, 2.1f, offset.z);
            m_Camera.transform.LookAt(new Vector3(0f, TableSpec.TopY, 0f));
        }
    }
}
