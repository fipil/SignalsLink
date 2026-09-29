using System.Reflection;
using System.Text.Json;
using SignalsLink.Testing;
using SignalsLink.src.signals.link;
using signals.src;
using signals.src.hangingwires;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using static SignalsLink.Tests.AnchorLifecycleTests;

namespace SignalsLink.Tests;

public class RigArenaTests
{
    [Fact]
    public void Reservation_roundtrips_without_serializing_computed_game_objects()
    {
        var location = new ArenaLocation(100,30,200,2);
        string json = JsonSerializer.Serialize(location);
        Assert.DoesNotContain("Origin", json);
        Assert.Equal(location, JsonSerializer.Deserialize<ArenaLocation>(json));
    }
    [Fact]
    public void A_new_arena_never_adopts_existing_blocks_even_if_they_match_the_blueprint()
    {
        var f = new Fixture(); f.Place(f.Layout.Target, "game:chest-east");
        Assert.Contains("unexpected block", f.Rig.Preflight(false));
        Assert.Equal(0, f.Writes);
        Assert.Null(f.Rig.Preflight(true));
    }
    [Fact]
    public void Preflight_refuses_unloaded_chunks_before_any_mutation()
    {
        var f = new Fixture(); f.Loaded = false;
        Assert.Contains("unloaded chunk", f.Rig.Preflight(false)); Assert.Equal(0, f.Writes);
    }
    [Fact]
    public void Cleanup_preserves_foreign_blocks_and_marks_itself_incomplete()
    {
        var f = new Fixture(); f.Place(f.Layout.Target, "game:chest-east");
        var foreign = f.Layout.At(8,3,4); f.Place(foreign, "game:anvil-iron");
        Assert.False(f.Rig.Cleanup()); Assert.False(f.Blocks.ContainsKey(f.Layout.Target));
        Assert.Equal("game:anvil-iron", f.Blocks[foreign].Code.ToString());
    }
    [Fact]
    public void Cleanup_removes_owned_blocks_but_does_not_load_chunks()
    {
        var f = new Fixture(); f.Place(f.Layout.Target, "game:chest-east");
        f.Loaded = false; Assert.False(f.Rig.Cleanup()); Assert.Equal(0, f.Writes);
        f.Loaded = true; Assert.True(f.Rig.Cleanup()); Assert.Empty(f.Blocks);
    }
    [Fact]
    public void Foreign_wires_are_rejected_and_their_blocks_survive_cleanup()
    {
        var f = new Fixture(); f.Place(f.Layout.Output, "signals:connection-down");
        var foreign = new WireConnection(new NodePos(f.Layout.Output,0), new NodePos(f.Layout.At(30,1,2),0));
        f.Wires.data.connections.Add(foreign);
        Assert.Contains("foreign wire", f.Rig.Preflight(true));
        Assert.False(f.Rig.Cleanup()); Assert.Contains(foreign, f.Wires.data.connections);
        Assert.True(f.Blocks.ContainsKey(f.Layout.Output));
    }
    [Fact]
    public void Fluid_is_never_erased_as_part_of_cleanup()
    {
        var f = new Fixture { HasFluid = true };
        Assert.Contains("unexpected fluid", f.Rig.Preflight(true));
        Assert.False(f.Rig.Cleanup()); Assert.Equal(0, f.Writes);
    }
    [Fact]
    public void Loss_of_creative_mode_aborts_the_environment_check()
    {
        var f = new Fixture { Creative = false };
        Assert.Contains("creative", f.Rig.CheckEnvironment());
    }
    [Fact]
    public void Report_failure_can_cancel_before_the_step_mutates_the_world()
    {
        int writes = 0; RigRunner runner = null;
        runner = new RigRunner(new[] { new RigStep("build", () => writes++, () => true, () => "") },
            (_,_) => runner.Stop(RigStatus.Error, "disk full"));
        runner.Tick(0); Assert.Equal(0, writes); Assert.Equal(RigStatus.Error, runner.Status);
    }

    sealed class Fixture
    {
        public ArenaLayout Layout = new(new ArenaLocation(0,0,0,0));
        public Dictionary<BlockPos,Block> Blocks = new();
        public HangingWiresMod Wires = new();
        public LinkNetworkMod Links = new();
        public bool Loaded = true, HasFluid, Creative = true;
        public int Writes;
        public ChuteRig Rig;
        public Fixture()
        {
            var signals = new SignalNetworkMod();
            var chunk = Proxy.Make<IWorldChunk>((m,a) => Proxy.Unhandled);
            object Handle(MethodInfo m, object[] a)
            {
                switch (m.Name)
                {
                    case "GetChunkAtBlockPos": return Loaded ? chunk : null;
                    case "GetBlockEntity": return null;
                    case "GetBlock":
                        if (a.Length > 1 && (int)a[1] == BlockLayersAccess.Fluid)
                            return new Block { BlockId = HasFluid ? 2 : 0, Code = new AssetLocation(HasFluid ? "game:water-still-7" : "game:air") };
                        return Blocks.TryGetValue((BlockPos)a[0], out var b) ? b : new Block { Code = new AssetLocation("game:air") };
                    case "SetBlock":
                        Assert.Equal(0, (int)a[0]); Writes++; Blocks.Remove((BlockPos)a[1]); return null;
                    case "GetEntitiesAround": return Array.Empty<Entity>();
                    case "GetModSystem":
                        var t = m.GetGenericArguments()[0];
                        if (t == typeof(HangingWiresMod)) return Wires;
                        if (t == typeof(SignalNetworkMod)) return signals;
                        if (t == typeof(LinkNetworkMod)) return Links;
                        return null;
                    default: return Proxy.Unhandled;
                }
            }

            var api = Proxy.Make<ICoreServerAPI>(Handle);
            Rig = new ChuteRig(api, Layout, (_,_) => { }, () => Creative ? null : "caller must remain creative");
        }
        public void Place(BlockPos pos, string code) => Blocks[pos.Copy()] = new Block { BlockId = 1, Code = new AssetLocation(code) };
    }
}
