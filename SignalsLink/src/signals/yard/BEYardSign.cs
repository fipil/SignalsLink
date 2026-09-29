using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.yard
{
    /// <summary>
    /// The name plate of a storage yard. Tiles carry no block entity of their own — a yard of four
    /// hundred of them would tick and save for a shape the world already describes — so this is the
    /// one block that holds anything, and all it holds is the name and the size it is written in.
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

        /// <summary>What the editor offers, and so what is accepted.</summary>
        public const float MinFontSize = 14f;
        public const float MaxFontSize = 40f;

        private string yardName = "";

        /// <summary>Zero until set: the size the block asks for is used then.</summary>
        private float fontSize;

        private BlockEntitySignRenderer renderer;

        public string YardName
        {
            get => yardName;
            set
            {
                yardName = Clamp(value);
                Redraw();
                MarkDirty(true);
            }
        }

        /// <summary>The size the name is written in, picked in the editor.</summary>
        public float FontSize => fontSize > 0 ? fontSize : Block?.Attributes?["fontSize"].AsFloat(20f) ?? 20f;

        /// <summary>The editor's range; anything that is not a size at all means "as the block says".</summary>
        public static float ClampFontSize(float size)
        {
            if (!float.IsFinite(size) || size <= 0) return 0;

            return GameMath.Clamp(size, MinFontSize, MaxFontSize);
        }

        private void SetNameAndSize(string name, float size)
        {
            fontSize = ClampFontSize(size);
            YardName = name;
        }

        private void Redraw()
        {
            if (renderer == null) return;

            renderer.fontSize = FontSize;
            renderer.SetNewText(yardName, ColorUtil.WhiteArgb);
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
                Redraw();
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
                FontSize = FontSize,
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
                Redraw();
            }
        }
        /// <summary>Opens the little form the player types the name into. Client side only.</summary>
        public void OpenNameDialog(IPlayer byPlayer)
        {
            if (Api is not ICoreClientAPI capi) return;

            var dialog = new GuiDialogBlockEntityTextInput(
                Lang.Get("signalslink:yardsign-dialogtitle"), Pos, yardName, capi, GetTextConfig());

            // Picking a size in the editor rewrites the text as well, so this hears about both.
            dialog.OnTextChanged = text => SendNameToServer(text, dialog.FontSize);
            dialog.TryOpen();
        }

        private void SendNameToServer(string text, float size)
        {
            using var stream = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(stream);
            writer.Write(Clamp(text) ?? "");
            writer.Write(size);

            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdSetName, stream.ToArray());
        }

        public const int PacketIdSetName = 1042;

        /// <summary>What the game's own text editor sends when Save is pressed.</summary>
        public const int PacketIdEditorSave = 1002;

        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            if (packetid == PacketIdEditorSave)
            {
                EditSignPacket saved = SerializerUtil.Deserialize<EditSignPacket>(data);
                if (saved != null) SetNameAndSize(saved.Text, saved.FontSize);
                return;
            }

            if (packetid != PacketIdSetName)
            {
                base.OnReceivedClientPacket(fromPlayer, packetid, data);
                return;
            }

            using var stream = new System.IO.MemoryStream(data);
            using var reader = new System.IO.BinaryReader(stream);

            string name = reader.ReadString();

            // The size came later; a packet without it keeps the size the sign has.
            float size = stream.Position + sizeof(float) <= stream.Length ? reader.ReadSingle() : fontSize;

            SetNameAndSize(name, size);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            yardName = Clamp(tree.GetString("yardName", ""));
            fontSize = ClampFontSize(tree.GetFloat("fontSize", 0));

            // The name arrives on the client through this path, both on chunk load and on every
            // later change - so this is where the text gets rasterised, and nowhere near a frame.
            Redraw();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetString("yardName", yardName ?? "");
            tree.SetFloat("fontSize", fontSize);
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
