using System;
using ProtoBuf;
using signals.src.signalNetwork;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// A physical connection between two hose anchors (anchor-to-anchor). Mirror of the
    /// Signals <c>WireConnection</c>, but for the ManagedHose network. Carries no signal.
    /// </summary>
    [ProtoContract()]
    public class HoseConnection : IEquatable<HoseConnection>
    {
        [ProtoMember(1)]
        public NodePos pos1;
        [ProtoMember(2)]
        public NodePos pos2;

        public HoseConnection() { }

        public HoseConnection(NodePos pos1, NodePos pos2)
        {
            this.pos1 = pos1;
            this.pos2 = pos2;
        }

        public bool Equals(HoseConnection other)
        {
            if (other == null) return false;
            // Undirected connection: (a,b) == (b,a)
            return (pos1 == other.pos1 && pos2 == other.pos2)
                || (pos1 == other.pos2 && pos2 == other.pos1);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as HoseConnection);
        }

        public override int GetHashCode()
        {
            // Symmetric hash so (a,b) and (b,a) fall into the same bucket.
            int h1 = pos1?.GetHashCode() ?? 0;
            int h2 = pos2?.GetHashCode() ?? 0;
            return h1 ^ h2;
        }

        public static bool operator ==(HoseConnection left, HoseConnection right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null);
            return left.Equals(right);
        }

        public static bool operator !=(HoseConnection left, HoseConnection right)
        {
            return !(left == right);
        }
    }
}
