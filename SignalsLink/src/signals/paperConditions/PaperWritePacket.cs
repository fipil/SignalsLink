using ProtoBuf;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// What the player confirmed in the dialog. The write itself happens on the server, because a
    /// client that could set a device's orders directly could set anyone's.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PaperWritePacket
    {
        public int X;
        public int Y;
        public int Z;

        /// <summary>The new orders, or null to clear them.</summary>
        public string Text;
    }
}
