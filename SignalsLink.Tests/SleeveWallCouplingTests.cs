using Newtonsoft.Json.Linq;
using signals.src.signalNetwork;
using SignalsLink.src.signals.link;
using SignalsLink.src.signals.sleeve;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SignalsLink.Tests;

public class SleeveWallCouplingTests
{
    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("z")]
    public void Full_block_selection_resolves_both_exterior_anchors_in_every_axis(string axis)
    {
        var json = ReadObject("blocktypes/sleevewallcoupling-legacy.json");
        var nodes = (JArray)json["attributes"]["linkNodes"].DeepClone();
        foreach (JObject node in nodes)
        {
            node["rotateY"] = node["rotateYbytype"]?["*-" + axis] ?? 0;
            node["rotateZ"] = node["rotateZbytype"]?["*-" + axis] ?? 0;
        }
        var block = new Probe(nodes.ToObject<LinkAnchor[]>());
        block.VariantStrict["axis"] = axis;
        var pos = new BlockPos(12, 8, -4, 0);
        ILinkAnchor anchor = block;
        Assert.Equal(LinkKind.Sleeve, anchor.AcceptedLinkKind);
        Assert.Equal(2, anchor.GetLinkAnchors(null, pos).Length);
        Assert.Equal(3, block.GetSelectionBoxes(System.Reflection.DispatchProxy.Create<IBlockAccessor, SelectionAccessorProxy>(), pos).Length);
        Assert.True(block.DoPartialSelection(null, pos));
        var box = block.GetSelectionBoxes(System.Reflection.DispatchProxy.Create<IBlockAccessor, SelectionAccessorProxy>(), pos)[2];
        Assert.Equal(new float[] { 0, 0, 0, 1, 1, 1 }, new[] { box.X1, box.Y1, box.Z1, box.X2, box.Y2, box.Z2 });
        foreach (var face in BlockFacing.ALLFACES)
        {
            var hit = new Vec3d(.5 + face.Normali.X * .5, .5 + face.Normali.Y * .5, .5 + face.Normali.Z * .5);
            var selection = new BlockSelection { Position = pos, Face = face, HitPosition = hit, SelectionBoxIndex = 0 };
            int normal = axis == "x" ? face.Normali.X : axis == "y" ? face.Normali.Y : face.Normali.Z;
            selection.SelectionBoxIndex = normal == 0 ? 2 : normal < 0 ? 0 : 1;
            var node = anchor.GetNodePosForLink(null, selection);
            if (normal == 0) { Assert.Null(node); continue; }
            Assert.NotNull(node);
            Assert.Equal(normal < 0 ? 0 : 1, node.index);
            var centre = anchor.GetLinkAnchorPosInBlock(node);
            float along = axis == "x" ? centre.X : axis == "y" ? centre.Y : centre.Z;
            Assert.Equal(normal < 0 ? -.75f / 16 : 16.75f / 16, along, 4);
            Assert.False(anchor.AllowsMultipleLinks(node));
            // Clicking the material surrounding the aperture cannot connect to either anchor.
            var corner = hit.Clone();
            if (axis == "x") corner.Y = .1; else corner.X = .1;
            selection.HitPosition = corner;
            selection.SelectionBoxIndex = 2;
            Assert.Null(anchor.GetNodePosForLink(null, selection));
        }
        Assert.False(anchor.CanAttachLink(null, new NodePos(pos, 2)));
        Assert.False(anchor.CanAttachLink(null, new NodePos(pos, -1)));
    }

    public static IEnumerable<object[]> Orientations =>
        from orientation in BlockFacing.ALLFACES
        from side in BlockFacing.ALLFACES
        select new object[] { orientation.Code, side.Code };

    [Theory]
    [MemberData(nameof(Orientations))]
    public void Rotated_wall_anchors_follow_the_regular_coupling_and_resolve_from_the_exterior(string orientation, string side)
    {
        string suffix = $"-{orientation}-{side}";
        var wallJson = ReadObject("blocktypes/sleevewallcoupling.json");
        var regularJson = ReadObject("blocktypes/sleevecoupling.json");
        var wallNodes = ResolveNodes(wallJson, "sleevewallcoupling-rock-granite" + suffix);
        var regularNodes = ResolveNodes(regularJson, "sleevecoupling" + suffix);
        var block = new Probe(wallNodes);
        var pos = new BlockPos(0, 0, 0, 0);
        foreach (var regularNode in regularNodes)
        {
            var reference = regularNode.RotatedCopy();
            var direction = BlockFacing.ALLFACES.MaxBy(face =>
                (reference.MidX - .5) * face.Normali.X + (reference.MidY - .5) * face.Normali.Y + (reference.MidZ - .5) * face.Normali.Z);
            var selection = new BlockSelection
            {
                Position = pos, Face = direction, SelectionBoxIndex = regularNode.Index,
                HitPosition = new Vec3d(.5 + direction.Normali.X * .5, .5 + direction.Normali.Y * .5, .5 + direction.Normali.Z * .5)
            };
            var selectionBoxes = block.GetSelectionBoxes(System.Reflection.DispatchProxy.Create<IBlockAccessor, SelectionAccessorProxy>(), pos);
            Assert.Equal(3, selectionBoxes.Length);
            var selectedBox = selectionBoxes[selection.SelectionBoxIndex];
            var expectedBox = wallNodes[regularNode.Index].RotatedCopy();
            Assert.Equal(expectedBox.ToString(), selectedBox.ToString());
            // All sides of the protruding collar select the same anchor.
            foreach (var face in BlockFacing.ALLFACES)
            {
                selection.Face = face;
                Assert.Equal(regularNode.Index, block.GetNodePosForLink(null, selection).index);
            }
            var selected = block.GetNodePosForLink(null, selection);
            Assert.True(selected != null, $"face={direction.Code} reference={reference} wall={wallNodes[regularNode.Index].RotatedCopy()}");
            Assert.Equal(regularNode.Index, selected.index);
            var centre = block.GetLinkAnchorPosInBlock(selected);
            Assert.Equal(.5 + direction.Normali.X * (8.75 / 16), centre.X, 4);
            Assert.Equal(.5 + direction.Normali.Y * (8.75 / 16), centre.Y, 4);
            Assert.Equal(.5 + direction.Normali.Z * (8.75 / 16), centre.Z, 4);
        }
        Assert.Null(block.GetNodePosForLink(null, new BlockSelection { Position = pos, SelectionBoxIndex = 2 }));
        Assert.Null(block.GetNodePosForLink(null, null));
        Assert.Single(block.GetCollisionBoxes(null, pos));
        foreach (var rotation in regularJson["shape"].Children<JProperty>().Where(p => p.Name.StartsWith("rotate")))
            Assert.True(JToken.DeepEquals(rotation.Value, wallJson["shape"][rotation.Name]));
        Assert.True(JToken.DeepEquals(regularJson["behaviors"], wallJson["behaviors"]));
    }

    private static LinkAnchor[] ResolveNodes(JObject block, string code)
    {
        var nodes = (JArray)block["attributes"]["linkNodes"].DeepClone();
        foreach (JObject node in nodes)
        foreach (var axis in new[] { "X", "Y", "Z" })
        {
            var variants = node.Properties().FirstOrDefault(p =>
                p.Name.Equals("rotate" + axis + "bytype", StringComparison.OrdinalIgnoreCase));
            node["rotate" + axis] = variants?.Value.Children<JProperty>()
                .FirstOrDefault(p => WildcardUtil.Match(p.Name, code))?.Value ?? new JValue(0);
        }
        return nodes.ToObject<LinkAnchor[]>();
    }

    [Theory]
    [InlineData("sleevewallcoupling.json")]
    [InlineData("sleevewallcoupling-legacy.json")]
    public void Drops_deserialize_with_the_game_schema_and_yield_one_coupling(string file)
    {
        var drops = ReadObject("blocktypes/" + file)["drops"].ToObject<BlockDropItemStack[]>();
        var drop = Assert.Single(drops);
        Assert.NotNull(drop.Quantity);
        Assert.Equal(1f, drop.Quantity.avg);
        Assert.Equal(0f, drop.Quantity.var);
    }

    [Fact]
    public void Material_variants_match_wall_chute_recipes_and_all_have_a_crafting_route()
    {
        var block = ReadObject("blocktypes/sleevewallcoupling.json");
        var recipes = (JArray)Read("recipes/grid/sleevewallcoupling.json");
        var reference = (JArray)Read("recipes/grid/managedchute.json");
        var skips = block["skipVariants"].Values<string>().ToArray();
        var types = block["variantgroups"][0]["states"].Values<string>();
        var materials = block["variantgroups"][1]["states"].Values<string>();
        var outputs = new HashSet<string>();
        for (int i = 0; i < 3; i++)
        {
            var recipe = recipes[i];
            Assert.Equal(reference[i + 1]["allowedVariants"]["material"].ToString(), recipe["allowedVariants"]["material"].ToString());
            Assert.Equal("signalslink:sleevecoupling-*", (string)recipe["ingredients"]["C"]["code"]);
            foreach (string material in recipe["allowedVariants"]["material"].Values<string>())
                outputs.Add(((string)recipe["output"]["code"]).Replace("{material}", material).Split(':')[1]);
        }
        int count = 0;
        foreach (string type in types)
        foreach (string material in materials)
        {
            string code = $"sleevewallcoupling-{type}-{material}-north-down";
            if (skips.Any(pattern => WildcardUtil.Match(pattern, code))) continue;
            Assert.Contains(code, outputs);
            Assert.Contains(((JObject)block["texturesByType"]).Properties(), p => WildcardUtil.Match(p.Name, code));
            count++;
        }
        Assert.Equal(35, count);
        Assert.Equal(35, outputs.Count);
        Assert.Equal("signalslink:sleevecoupling-north-down", (string)recipes[3]["output"]["code"]);
        Assert.Equal("signalslink:sleevewallcoupling-*", (string)recipes[3]["ingredients"]["C"]["code"]);
        Assert.True((bool)recipes[3]["ingredients"]["H"]["isTool"]);
        Assert.True((bool)recipes[3]["ingredients"]["W"]["isTool"]);
    }

    [Fact]
    public void Shape_has_an_open_copper_passage_and_only_collars_protrude()
    {
        var shape = ReadObject("shapes/block/sleeve/sleevewallcoupling.json");
        foreach (JObject element in shape["elements"])
        {
            var from = element["from"].Values<float>().ToArray();
            var to = element["to"].Values<float>().ToArray();
            Assert.False(from[1] < 8 && to[1] > 8 && from[2] < 8 && to[2] > 8);
            if (from[0] < 0 || to[0] > 16)
            {
                Assert.StartsWith("collar-", (string)element["name"]);
                Assert.All(((JObject)element["faces"]).Properties(), p => Assert.Equal("#copper1", (string)p.Value["texture"]));
            }
            Assert.All(((JObject)element["faces"]).Properties(), p =>
                Assert.All(p.Value["uv"].Values<float>(), value => Assert.InRange(value, 0, 16)));
        }
        Assert.Equal("signalslink:block/copper1", (string)shape["textures"]["copper1"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    public void Sealing_mode_persists_per_block_and_invalid_values_default_to_cellar(int mode)
    {
        var be = new BESleeveWallCoupling();
        var world = System.Reflection.DispatchProxy.Create<IWorldAccessor, WorldProxy>();
        be.CreateBehaviors(new Block { Code = new AssetLocation("signalslink:sleevewallcoupling-rock-granite-x") }, world);
        var input = new TreeAttribute(); input.SetInt("retentionMode", mode);
        be.FromTreeAttributes(input, world);
        var saved = new TreeAttribute(); be.ToTreeAttributes(saved);
        Assert.Equal(mode == 99 ? RetentionMode.Cooling : mode, saved.GetInt("retentionMode"));
        Assert.Equal(RetentionMode.Cooling, new BESleeveWallCoupling().RetentionMode);
        Assert.True(be.SupportsRetentionMode);
    }

    [Fact]
    public void Network_crosses_the_coupling_but_a_sleeve_cannot_bypass_a_wall()
    {
        var world = System.Reflection.DispatchProxy.Create<IWorldAccessor, WorldProxy>();
        var access = System.Reflection.DispatchProxy.Create<IBlockAccessor, AccessorProxy>();
        ((WorldProxy)(object)world).Accessor = access;
        var scene = (AccessorProxy)(object)access;
        var wallPos = new BlockPos(0, 0, 0, 0);
        var start = new NodePos(new BlockPos(-3, 0, 0, 0), 0);
        var finish = new NodePos(new BlockPos(3, 0, 0, 0), 0);
        var nodes = ReadObject("blocktypes/sleevewallcoupling.json")["attributes"]["linkNodes"].ToObject<LinkAnchor[]>();
        var wall = new Probe(nodes) { CollisionBoxes = new[] { new Cuboidf(0, 0, 0, 1, 1, 1) } };
        wall.VariantStrict["orientation"] = "north";
        wall.VariantStrict["side"] = "west";
        wall.Attributes = new Vintagestory.API.Datastructures.JsonObject(
            ReadObject("blocktypes/sleevewallcoupling.json")["attributes"]);
        scene.Blocks[wallPos] = wall;
        scene.Blocks[start.blockPos] = new Endpoint();
        scene.Blocks[finish.blockPos] = new Endpoint();
        var first = new LinkConnection(start, new NodePos(wallPos, 0), LinkKind.Sleeve);
        var second = new LinkConnection(new NodePos(wallPos, 1), finish, LinkKind.Sleeve);
        Assert.Equal(LinkPathResult.Clear, LinkPathChecker.Check(world, first, out _));
        Assert.Equal(LinkPathResult.Clear, LinkPathChecker.Check(world, second, out _));
        Assert.Equal(LinkPathResult.Blocked, LinkPathChecker.Check(world, new LinkConnection(start, finish, LinkKind.Sleeve), out var blocked));
        Assert.Equal(wallPos, blocked);
        scene.Entity = new InfoEntity();
        Assert.Contains("retention-visible", wall.GetPlacedBlockInfo(world, wallPos, null));
        var network = new LinkNetworkMod();
        network.data.connections.Add(first); network.data.connections.Add(second);
        Assert.Equal(finish, Assert.Single(network.GetOtherEndpoints(world, start)).Endpoint);
        Assert.Equal(start, Assert.Single(network.GetOtherEndpoints(world, finish)).Endpoint);
        // A second wall next to the coupling is not exempt merely because a sleeve ends nearby.
        scene.Blocks[new BlockPos(-1, 0, 0, 0)] = new Block { Replaceable = 0,
            CollisionBoxes = new[] { new Cuboidf(0, 0, 0, 1, 1, 1) } };
        Assert.Equal(LinkPathResult.Blocked, LinkPathChecker.Check(world, first, out _));
    }

    private sealed class Endpoint : BlockSleeveCoupling
    {
        public Endpoint() { linkAnchors = new[] { new LinkAnchor(0, "con-sleeve", .4f, .4f, .4f, .6f, .6f, .6f) }; }
    }
    private sealed class InfoEntity : BESleeveWallCoupling
    {
        public override void GetBlockInfo(IPlayer player, System.Text.StringBuilder text) => text.Append("retention-visible");
    }
    public class AccessorProxy : System.Reflection.DispatchProxy
    {
        public BlockEntity Entity;
        public readonly Dictionary<BlockPos, Block> Blocks = new();
        private readonly Block air = new Block { Replaceable = 10000 };
        private readonly IWorldChunk chunk = System.Reflection.DispatchProxy.Create<IWorldChunk, EmptyChunkProxy>();
        protected override object Invoke(System.Reflection.MethodInfo method, object[] args) => method.Name switch
        {
            "GetBlock" => Blocks.GetValueOrDefault((BlockPos)args[0], air),
            "GetChunkAtBlockPos" => chunk,
            "GetBlockEntity" => Entity,
            _ => throw new NotSupportedException(method.Name)
        };
    }
    public class SelectionAccessorProxy : System.Reflection.DispatchProxy
    {
        protected override object Invoke(System.Reflection.MethodInfo method, object[] args) =>
            method.Name == "GetChunkAtBlockPos" ? null : throw new NotSupportedException(method.Name);
    }
    public class EmptyChunkProxy : System.Reflection.DispatchProxy
    {
        protected override object Invoke(System.Reflection.MethodInfo method, object[] args) => throw new NotSupportedException(method.Name);
    }
    public class WorldProxy : System.Reflection.DispatchProxy
    {
        public IBlockAccessor Accessor;
        protected override object Invoke(System.Reflection.MethodInfo method, object[] args) =>
            method.Name switch { "get_Side" => EnumAppSide.Client, "get_BlockAccessor" => Accessor, _ => throw new NotSupportedException(method.Name) };
    }

    private sealed class Probe : BlockSleeveWallCoupling
    {
        public Probe(LinkAnchor[] anchors)
        {
            linkAnchors = anchors;
            BlockBehaviors = Array.Empty<BlockBehavior>();
            SelectionBoxes = new[] { new Cuboidf(0, 0, 0, 1, 1, 1) };
            CollisionBoxes = new[] { new Cuboidf(0, 0, 0, 1, 1, 1) };
        }
    }
    private static JObject ReadObject(string path) => (JObject)Read(path);
    private static JToken Read(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SignalsLink.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return JToken.Parse(File.ReadAllText(Path.Combine(dir.FullName, "SignalsLink", "assets", "signalslink", path)));
    }
}