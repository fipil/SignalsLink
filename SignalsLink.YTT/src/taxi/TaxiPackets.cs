using ProtoBuf;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>Client to server: the ticket was used; what can this train do for me?</summary>
    [ProtoContract]
    public sealed class TaxiOpenRequest
    {
        [ProtoMember(1)] public int Nothing;
    }

    /// <summary>Server to client: the stations to pick from, or why there are none.</summary>
    [ProtoContract]
    public sealed class TaxiStationsPacket
    {
        [ProtoMember(1)] public long LocomotiveId;
        [ProtoMember(2)] public string[] Names = System.Array.Empty<string>();
        [ProtoMember(3)] public long[] Hashes = System.Array.Empty<long>();
        [ProtoMember(4)] public int[] Dimensions = System.Array.Empty<int>();

        /// <summary>0 ready, 1 pending (another packet follows), 2 could not be told.</summary>
        [ProtoMember(5)] public int Reach;

        [ProtoMember(6)] public bool HasRide;
        [ProtoMember(7)] public long ActiveHash;
        [ProtoMember(8)] public bool ActiveWait;
        [ProtoMember(9)] public string ActiveName = "";

        /// <summary>A lang key suffix when the ticket cannot be used here; empty otherwise.</summary>
        [ProtoMember(10)] public string Error = "";
    }

    /// <summary>Client to server: take me there.</summary>
    [ProtoContract]
    public sealed class TaxiOrderPacket
    {
        [ProtoMember(1)] public long LocomotiveId;
        [ProtoMember(2)] public long Hash;
        [ProtoMember(3)] public int Dimension;
        [ProtoMember(4)] public bool Wait;
    }

    /// <summary>Client to server: stop here (and wait, or not).</summary>
    [ProtoContract]
    public sealed class TaxiStopPacket
    {
        [ProtoMember(1)] public long LocomotiveId;
        [ProtoMember(2)] public bool Wait;
    }
}
