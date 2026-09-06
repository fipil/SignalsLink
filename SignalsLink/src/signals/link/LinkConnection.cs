using System;
using ProtoBuf;
using signals.src.signalNetwork;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// Which kind of line a connection is. <see cref="Hose"/> must stay 0: protobuf leaves a
    /// missing field at its default, so every connection written before this field existed loads
    /// back as a hose and no save migration is needed.
    /// </summary>
    public static class LinkKind
    {
        /// <summary>ManagedHose - liquids.</summary>
        public const byte Hose = 0;
        /// <summary>ManagedSleeve - items and blocks.</summary>
        public const byte Sleeve = 1;

        /// <summary>How many kinds exist; sizes the per-kind lookup tables.</summary>
        public const int Count = 2;
    }

    /// <summary>
    /// A physical connection between two link anchors (anchor-to-anchor). Mirror of the
    /// Signals <c>WireConnection</c>, but for the hose/sleeve network. Carries no signal.
    /// </summary>
    [ProtoContract()]
    public class LinkConnection : IEquatable<LinkConnection>
    {
        [ProtoMember(1)]
        public NodePos pos1;
        [ProtoMember(2)]
        public NodePos pos2;

        /// <summary>
        /// See <see cref="LinkKind"/>. Deliberately NOT part of the connection's identity: an
        /// anchor accepts exactly one kind, so the anchor pair already determines it, and keeping
        /// it out of Equals/GetHashCode leaves the saved HashSet semantics unchanged.
        /// </summary>
        [ProtoMember(3)]
        public byte kind;

        public LinkConnection() { }

        public LinkConnection(NodePos pos1, NodePos pos2, byte kind = LinkKind.Hose)
        {
            this.pos1 = pos1;
            this.pos2 = pos2;
            this.kind = kind;
        }

        public bool Equals(LinkConnection other)
        {
            if (other == null) return false;
            // Undirected connection: (a,b) == (b,a)
            return (pos1 == other.pos1 && pos2 == other.pos2)
                || (pos1 == other.pos2 && pos2 == other.pos1);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as LinkConnection);
        }

        public override int GetHashCode()
        {
            // Symmetric hash so (a,b) and (b,a) fall into the same bucket.
            int h1 = pos1?.GetHashCode() ?? 0;
            int h2 = pos2?.GetHashCode() ?? 0;
            return h1 ^ h2;
        }

        public static bool operator ==(LinkConnection left, LinkConnection right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null);
            return left.Equals(right);
        }

        public static bool operator !=(LinkConnection left, LinkConnection right)
        {
            return !(left == right);
        }
    }
}
