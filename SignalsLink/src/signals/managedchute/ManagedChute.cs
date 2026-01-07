using signals.src.transmission;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute
{
    public class ManagedChute : BlockConnection, IBlockItemFlow
    {
        public string Type { get; set; }

        public string Side { get; set; }

        public string Vertical { get; set; }

        public string[] PullFaces => this.Attributes["pullFaces"].AsArray<string>(Array.Empty<string>());

        public string[] PushFaces => this.Attributes["pushFaces"].AsArray<string>(Array.Empty<string>());

        public string[] AcceptFaces
        {
            get => this.Attributes["acceptFromFaces"].AsArray<string>(Array.Empty<string>());
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            string str1 = this.Variant["type"];
            this.Type = str1 != null ? string.Intern(str1) : (string)null;
            string str2 = this.Variant["side"];
            this.Side = str2 != null ? string.Intern(str2) : (string)null;
            string str3 = this.Variant["vertical"];
            this.Vertical = str3 != null ? string.Intern(str3) : (string)null;
        }

        public bool HasItemFlowConnectorAt(BlockFacing facing)
        {
            return ((IEnumerable<string>)this.PullFaces).Contains<string>(facing.Code) || ((IEnumerable<string>)this.PushFaces).Contains<string>(facing.Code) || ((IEnumerable<string>)this.AcceptFaces).Contains<string>(facing.Code);
        }

        public override bool TryPlaceBlock(
          IWorldAccessor world,
          IPlayer byPlayer,
          ItemStack itemstack,
          BlockSelection blockSel,
          ref string failureCode)
        {
            ManagedChute blockChute = (ManagedChute)null;
            BlockFacing[] blockFacingArray = this.OrientForPlacement(world.BlockAccessor, byPlayer, blockSel);
            
             if (this.Type == "straight")
            {
                string str = blockFacingArray[0].Axis == EnumAxis.X ? "we" : "ns";
                if (blockSel.Face.IsVertical)
                    str = "ud";
                blockChute = this.api.World.GetBlock(this.CodeWithVariant("side", str)) as ManagedChute;
            }

            if (blockChute != null && blockChute.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode) && blockChute.CanStay(world, blockSel.Position))
            {
                world.BlockAccessor.SetBlock(blockChute.BlockId, blockSel.Position);
                world.Logger.Audit("{0} placed a chute at {1}", (object)byPlayer.PlayerName, (object)blockSel.Position);
                return true;
            }
            if (this.Type == "cross")
                blockChute = this.api.World.GetBlock(this.CodeWithVariant("side", "ground")) as ManagedChute;
            if (blockChute == null || !blockChute.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode) || !blockChute.CanStay(world, blockSel.Position))
                return false;
            world.BlockAccessor.SetBlock(blockChute.BlockId, blockSel.Position);
            world.Logger.Audit("{0} placed a chute at {1}", (object)byPlayer.PlayerName, (object)blockSel.Position);
            return true;
        }

        protected virtual BlockFacing[] OrientForPlacement(
          IBlockAccessor worldmap,
          IPlayer player,
          BlockSelection bs)
        {
            BlockFacing[] blockFacingArray = Block.SuggestedHVOrientation(player, bs);
            BlockPos position = bs.Position;
            BlockFacing blockFacing = (BlockFacing)null;
            BlockFacing opposite = bs.Face.Opposite;
            BlockFacing vert1 = (BlockFacing)null;
            if (opposite.IsHorizontal)
            {
                if (this.HasConnector(worldmap, position.AddCopy(opposite), bs.Face, out vert1))
                {
                    blockFacing = opposite;
                }
                else
                {
                    BlockFacing cw = opposite.GetCW();
                    if (this.HasConnector(worldmap, position.AddCopy(cw), cw.Opposite, out vert1))
                        blockFacing = cw;
                    else if (this.HasConnector(worldmap, position.AddCopy(cw.Opposite), cw, out vert1))
                        blockFacing = cw.Opposite;
                    else if (this.HasConnector(worldmap, position.AddCopy(bs.Face), bs.Face.Opposite, out vert1))
                        blockFacing = bs.Face;
                }
                if (this.Type == "3way" && blockFacing != null)
                {
                    BlockFacing cw = blockFacing.GetCW();
                    BlockFacing vert2;
                    if (this.HasConnector(worldmap, position.AddCopy(cw), cw.Opposite, out vert2) && !this.HasConnector(worldmap, position.AddCopy(cw.Opposite), cw, out vert2))
                        blockFacing = cw;
                }
            }
            else
            {
                vert1 = opposite;
                bool flag = false;
                blockFacing = this.HasConnector(worldmap, position.EastCopy(), BlockFacing.WEST, out vert1) ? BlockFacing.EAST : (BlockFacing)null;
                if (this.HasConnector(worldmap, position.WestCopy(), BlockFacing.EAST, out vert1))
                {
                    flag = blockFacing != null;
                    blockFacing = BlockFacing.WEST;
                }
                if (this.HasConnector(worldmap, position.NorthCopy(), BlockFacing.SOUTH, out vert1))
                {
                    flag = blockFacing != null;
                    blockFacing = BlockFacing.NORTH;
                }
                if (this.HasConnector(worldmap, position.SouthCopy(), BlockFacing.NORTH, out vert1))
                {
                    flag = blockFacing != null;
                    blockFacing = BlockFacing.SOUTH;
                }
                if (flag)
                    blockFacing = (BlockFacing)null;
            }
            if (vert1 == null)
            {
                BlockFacing vert3;
                bool flag1 = this.HasConnector(worldmap, position.UpCopy(), BlockFacing.DOWN, out vert3);
                bool flag2 = this.HasConnector(worldmap, position.DownCopy(), BlockFacing.UP, out vert3);
                if (flag1 && !flag2)
                    vert1 = BlockFacing.UP;
                else if (flag2 && !flag1)
                    vert1 = BlockFacing.DOWN;
            }
            if (vert1 != null)
                blockFacingArray[1] = vert1;
            blockFacingArray[0] = blockFacing ?? blockFacingArray[0].Opposite;
            return blockFacingArray;
        }

        protected virtual bool HasConnector(
          IBlockAccessor ba,
          BlockPos pos,
          BlockFacing face,
          out BlockFacing vert)
        {
            if (ba.GetBlock(pos) is BlockChute block)
            {
                vert = !block.HasItemFlowConnectorAt(BlockFacing.UP) || block.HasItemFlowConnectorAt(BlockFacing.DOWN) ? (!block.HasItemFlowConnectorAt(BlockFacing.DOWN) || block.HasItemFlowConnectorAt(BlockFacing.UP) ? (BlockFacing)null : BlockFacing.UP) : BlockFacing.DOWN;
                return block.HasItemFlowConnectorAt(face);
            }
            vert = (BlockFacing)null;
            return ba.GetBlock(pos).GetBlockEntity<BlockEntityContainer>(pos) != null;
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            if (this.CanStay(world, pos))
                return;
            world.BlockAccessor.BreakBlock(pos, (IPlayer)null);
        }

        private bool CanStay(IWorldAccessor world, BlockPos pos)
        {
            BlockPos position = new BlockPos();
            IBlockAccessor blockAccessor = world.BlockAccessor;
            if (this.PullFaces != null)
            {
                foreach (string pullFace in this.PullFaces)
                {
                    BlockFacing blockFacing = BlockFacing.FromCode(pullFace);
                    Block block = world.BlockAccessor.GetBlock(position.Set(pos).Add(blockFacing));
                    if (block.CanAttachBlockAt(world.BlockAccessor, (Block)this, pos, blockFacing) || (block is IBlockItemFlow blockItemFlow ? (blockItemFlow.HasItemFlowConnectorAt(blockFacing.Opposite) ? 1 : 0) : 0) != 0 || blockAccessor.GetBlock(pos).GetBlockEntity<BlockEntityContainer>(position) != null)
                        return true;
                }
            }
            if (this.PushFaces != null)
            {
                foreach (string pushFace in this.PushFaces)
                {
                    BlockFacing blockFacing = BlockFacing.FromCode(pushFace);
                    Block block = world.BlockAccessor.GetBlock(position.Set(pos).Add(blockFacing));
                    if (block.CanAttachBlockAt(world.BlockAccessor, (Block)this, pos, blockFacing) || (block is IBlockItemFlow blockItemFlow ? (blockItemFlow.HasItemFlowConnectorAt(blockFacing.Opposite) ? 1 : 0) : 0) != 0 || blockAccessor.GetBlock(pos).GetBlockEntity<BlockEntityContainer>(position) != null)
                        return true;
                }
            }
            return false;
        }

        public override BlockDropItemStack[] GetDropsForHandbook(
          ItemStack handbookStack,
          IPlayer forPlayer)
        {
            return new BlockDropItemStack[1]
            {
      new BlockDropItemStack(handbookStack)
            };
        }

        public override ItemStack[] GetDrops(
          IWorldAccessor world,
          BlockPos pos,
          IPlayer byPlayer,
          float dropQuantityMultiplier = 1f)
        {
            Block block = (Block)null;
            if (this.Type == "elbow" || this.Type == "3way")
                block = this.api.World.GetBlock(this.CodeWithVariants(new string[2]
                {
        "vertical",
        "side"
                }, new string[2] { "down", "east" }));
            if (this.Type == "t" || this.Type == "straight")
                block = this.api.World.GetBlock(this.CodeWithVariant("side", "ns"));
            if (this.Type == "cross")
                block = this.api.World.GetBlock(this.CodeWithVariant("side", "ground"));
            return new ItemStack[1] { new ItemStack(block) };
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            return this.GetDrops(world, pos, (IPlayer)null, 1f)[0];
        }

        public override AssetLocation GetRotatedBlockCode(int angle)
        {
            int num = GameMath.Mod(angle / 90, 4);
            switch (this.Type)
            {
                case "elbow":
                    BlockFacing blockFacing1 = BlockFacing.FromCode(this.Side);
                    return this.CodeWithVariant("side", BlockFacing.HORIZONTALS[GameMath.Mod(blockFacing1.Index + num + 2, 4)].Code.ToLowerInvariant());
                case "3way":
                    BlockFacing blockFacing2 = BlockFacing.FromCode(this.Side);
                    return this.CodeWithVariant("side", BlockFacing.HORIZONTALS[GameMath.Mod(blockFacing2.Index + num, 4)].Code.ToLowerInvariant());
                case "t":
                    if ((this.Side.Equals("ns") || this.Side.Equals("we")) && (num == 1 || num == 3))
                        return this.CodeWithVariant("side", this.Side.Equals("ns") ? "we" : "ns");
                    BlockFacing blockFacing3;
                    switch (this.Side)
                    {
                        case "ud-n":
                            blockFacing3 = BlockFacing.NORTH;
                            break;
                        case "ud-e":
                            blockFacing3 = BlockFacing.EAST;
                            break;
                        case "ud-s":
                            blockFacing3 = BlockFacing.SOUTH;
                            break;
                        case "ud-w":
                            blockFacing3 = BlockFacing.WEST;
                            break;
                        default:
                            blockFacing3 = BlockFacing.NORTH;
                            break;
                    }
                    BlockFacing blockFacing4 = blockFacing3;
                    ReadOnlySpan<char> readOnlySpan1 = (ReadOnlySpan<char>)"ud-";
                    char reference = BlockFacing.HORIZONTALS[GameMath.Mod(blockFacing4.Index + num, 4)].Code.ToLowerInvariant()[0];
                    ReadOnlySpan<char> readOnlySpan2 = new ReadOnlySpan<char>(ref reference);
                    return this.CodeWithVariant("side", readOnlySpan1.ToString() + readOnlySpan2.ToString());
                case "straight":
                    return this.Side.Equals("ud") || num == 0 || num == 2 ? this.Code : this.CodeWithVariant("side", this.Side.Equals("ns") ? "we" : "ns");
                case "cross":
                    return (this.Side.Equals("ns") || this.Side.Equals("we")) && (num == 1 || num == 3) ? this.CodeWithVariant("side", this.Side.Equals("ns") ? "we" : "ns") : this.Code;
                default:
                    return this.Code;
            }
        }

        public override AssetLocation GetVerticallyFlippedBlockCode()
        {
            return base.GetVerticallyFlippedBlockCode();
        }
    }

}
