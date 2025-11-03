using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace SignalsLink.EP.src.messages
{
    [ProtoContract]
    public class EpSwitchSwitchedMessage
    {
        [ProtoMember(1)]
        public bool IsOn { get; set; }

        [ProtoMember(2)]
        public BlockPos Pos { get; set; }
    }
}
