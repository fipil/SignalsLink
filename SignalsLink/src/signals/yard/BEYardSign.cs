using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.yard
{
    /// <summary>
    /// The name plate of a storage yard. Tiles carry no block entity of their own — a yard of four
    /// hundred of them would tick and save for a shape the world already describes — so this is the
    /// one block that holds anything, and all it holds is the name.
    ///
    /// The name is what lets a paper say WHICH yard it means (<c>load yard nadrazi</c>) when a dock
    /// can see more than one.
    /// </summary>
    public class BEYardSign : BlockEntity
    {
        /// <summary>
        /// A hard limit, deliberately not a shrinking font. A name nobody can read from across the
        /// station is not doing the job the sign exists for.
        /// </summary>
        public const int MaxNameLength = 24;

        private string yardName = "";

        private BlockEntitySignRenderer renderer;

        public string YardName
        {
            get => yardName;
            set
            {
                yardName = Clamp(value);
                renderer?.SetNewText(yardName, ColorUtil.WhiteArgb);
                MarkDirty(true);
            }
        }

        public static string Clamp(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            name = name.Replace("\r", " ").Replace("\n", " ").Trim();
            return name.Length <= MaxNameLength ? name : name.Substring(0, MaxNameLength);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (api is ICoreClientAPI capi)
            {
                renderer = new YardSignRenderer(Pos, capi, Block, GetTextConfig());
                renderer.SetNewText(yardName, ColorUtil.WhiteArgb);
            }
        }

        /// <summary>
        /// Where the text sits on the board and how big it is. Taken from the block so the numbers
        /// live next to the shape they belong to.
        /// </summary>
        private TextAreaConfig GetTextConfig()
        {
            JsonObject attributes = Block?.Attributes;

            return new TextAreaConfig
            {
                MaxWidth = attributes?["maxWidth"].AsInt(200) ?? 200,
                MaxHeight = attributes?["maxHeight"].AsInt(96) ?? 96,
                FontSize = attributes?["fontSize"].AsFloat(20f) ?? 20f,
                textVoxelWidth = attributes?["textVoxelWidth"].AsFloat(12f) ?? 12f,
                textVoxelHeight = attributes?["textVoxelHeight"].AsFloat(5f) ?? 5f,
                BoldFont = false,
                VerticalAlign = EnumVerticalAlign.Middle,
            };
        }

        public override void OnExchanged(Block block)
        {
            base.OnExchanged(block);
            // Placement exchanges the inventory variant for the direction facing the player.
            // Recreate the text plane as well, otherwise it keeps the previous orientation.
            if (Api is ICoreClientAPI capi)
            {
                renderer?.Dispose();
                renderer = new YardSignRenderer(Pos, capi, block, GetTextConfig());
                renderer.SetNewText(yardName, ColorUtil.WhiteArgb);
            }
        }
        /// <summary>Opens the little form the player types the name into. Client side only.</summary>
        public void OpenNameDialog(IPlayer byPlayer)
        {
            if (Api is not ICoreClientAPI capi) return;

            var dialog = new GuiDialogBlockEntityTextInput(
                Lang.Get("signalslink:yardsign-dialogtitle"), Pos, yardName, capi, GetTextConfig());

            dialog.OnTextChanged = text => SendNameToServer(text);
            dialog.TryOpen();
        }

        private void SendNameToServer(string text)
        {
            using var stream = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(stream);
            writer.Write(Clamp(text) ?? "");

            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdSetName, stream.ToArray());
        }

        public const int PacketIdSetName = 1042;

        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            if (packetid != PacketIdSetName)
            {
                base.OnReceivedClientPacket(fromPlayer, packetid, data);
                return;
            }

            using var stream = new System.IO.MemoryStream(data);
            using var reader = new System.IO.BinaryReader(stream);

            YardName = reader.ReadString();
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            yardName = Clamp(tree.GetString("yardName", ""));

            // The name arrives on the client through this path, both on chunk load and on every
            // later change - so this is where the text gets rasterised, and nowhere near a frame.
            renderer?.SetNewText(yardName, ColorUtil.WhiteArgb);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetString("yardName", yardName ?? "");
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            renderer?.Dispose();
            renderer = null;
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            renderer?.Dispose();
            renderer = null;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            dsc.AppendLine(string.IsNullOrEmpty(yardName)
                ? Lang.Get("signalslink:yardsign-unnamed")
                : Lang.Get("signalslink:yardsign-named", yardName));
        }
    }
}
