namespace TableTennis
{
    /// <summary>
    /// Applies table tennis rules to the contacts of one ball flight. Only the peer controlling the
    /// receiver runs it, because only the receiver can still change the outcome of that flight.
    /// </summary>
    public class RallyJudge
    {
        BallLaunch m_Launch;
        bool m_ServeBouncedOwnSide;
        int m_ReceiverBounces;
        bool m_TouchedNet;

        public bool Decided { get; private set; }
        public int Winner { get; private set; }
        public PointReason Reason { get; private set; }

        /// <summary>True once the ball has bounced exactly once on the receiver's half, so they may return it.</summary>
        public bool ReceiverMayHit => !Decided && m_ReceiverBounces == 1;

        public void Reset(in BallLaunch launch)
        {
            m_Launch = launch;
            m_ServeBouncedOwnSide = false;
            m_ReceiverBounces = 0;
            m_TouchedNet = false;
            Decided = launch.Kind == LaunchKind.Held;
            Winner = -1;
            Reason = PointReason.None;
        }

        public void Process(in BallContact contact)
        {
            if (Decided) return;

            int hitter = m_Launch.Hitter;
            int receiver = m_Launch.Receiver;

            switch (contact.Type)
            {
                case ContactType.Net:
                    m_TouchedNet = true;
                    break;

                case ContactType.Table:
                    if (m_Launch.Kind == LaunchKind.Serve && !m_ServeBouncedOwnSide)
                    {
                        if (contact.Side == hitter) m_ServeBouncedOwnSide = true;
                        else Award(receiver, PointReason.ServeFault);
                    }
                    else if (contact.Side == receiver)
                    {
                        m_ReceiverBounces++;
                        if (m_ReceiverBounces >= 2) Award(hitter, PointReason.DoubleBounce);
                    }
                    else
                    {
                        Award(receiver, m_Launch.Kind == LaunchKind.Serve ? PointReason.ServeFault : PointReason.OwnSide);
                    }
                    break;

                case ContactType.Floor:
                case ContactType.Wall:
                    if (m_ReceiverBounces >= 1) Award(hitter, PointReason.Missed);
                    else Award(receiver, m_TouchedNet ? PointReason.Net : PointReason.Out);
                    break;
            }
        }

        void Award(int winner, PointReason reason)
        {
            Decided = true;
            Winner = winner;
            Reason = reason;
        }
    }
}
