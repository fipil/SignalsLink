using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Proxy = SignalsLink.Tests.AnchorLifecycleTests.Proxy;

namespace SignalsLink.Tests;

public class FruitPressDriveTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_automated_press_resumes_after_load_even_on_the_first_frame(bool vanillaRestoredAnimation)
    {
        var original = new PressFixture();
        Assert.True(FruitPressMash.Press(original));
        var saved = new TreeAttribute();
        original.Drive.ToTreeAttributes(saved);

        var loaded = new PressFixture();
        if (vanillaRestoredAnimation) loaded.TurnScrewManually();
        loaded.Drive.FromTreeAttributes(saved, loaded.Api.World);
        loaded.Drive.Initialize(loaded.Api, null);
        Assert.NotNull(loaded.AfterInitialize);
        loaded.AfterInitialize(0);
        Assert.True(FruitPressMash.IsPressing(loaded));

        loaded.Animator.GetAnimationState("compress").CurrentFrame = 8;
        var mash = new Item { Code = new AssetLocation("game:pressedmash-apple"),
            Attributes = JsonObject.FromJson("""{"juiceableProperties":{"liquidStack":{"type":"item","code":"game:juiceportion-apple"}}}""") };
        loaded.MashSlot.Itemstack = new ItemStack(mash);
        loaded.MashSlot.Itemstack.Attributes.SetDouble("juiceableLitresLeft", 10);
        loaded.SqueezeTick(0.05f);
        Assert.InRange(loaded.MashSlot.Itemstack.Attributes.GetDouble("squeezeRel", 1), 0.1, 0.99);
        if (!vanillaRestoredAnimation)
        {
            // On this path the mod also has to restart vanilla's liquid-transfer listener.
            loaded.TotalHours = 0.1;
            loaded.LiquidTick(0.025f);
            Assert.True(FruitPressMash.LitresTransfered(loaded.MashSlot.Itemstack) > 0);
        }
    }

    [Fact]
    public void Reading_a_manual_press_does_not_adopt_it_even_after_load()
    {
        var press = new PressFixture();
        press.TurnScrewManually();
        press.Drive.FromTreeAttributes(new TreeAttribute(), press.Api.World);
        press.Drive.Initialize(press.Api, null);
        Assert.True(FruitPressMash.IsPressing(press));
        Assert.Null(press.AfterInitialize);
        Assert.Null(press.SqueezeTick);
    }

    [Fact]
    public void A_player_releasing_the_screw_cancels_a_pending_restore()
    {
        var original = new PressFixture();
        Assert.True(FruitPressMash.Press(original));
        var saved = new TreeAttribute();
        original.Drive.ToTreeAttributes(saved);
        var loaded = new PressFixture();
        loaded.Drive.FromTreeAttributes(saved, loaded.Api.World);
        loaded.Drive.Initialize(loaded.Api, null);
        loaded.Drive.OnReceivedClientPacket(null, 1003, null);
        loaded.AfterInitialize(0);
        Assert.False(FruitPressMash.IsPressing(loaded));
        Assert.Null(loaded.SqueezeTick);
    }

    [Theory]
    [InlineData(1002)]
    [InlineData(1003)]
    [InlineData(1004)]
    public void A_screw_packet_vanilla_refused_leaves_the_drive_running(int packetId)
    {
        // The behavior hears every packet, also one vanilla threw away for a missing permission.
        var press = new PressFixture();
        Assert.True(FruitPressMash.Press(press));

        press.Drive.OnReceivedClientPacket(null, packetId, null);

        Assert.True(FruitPressMash.IsPressing(press));
        var saved = new TreeAttribute();
        press.Drive.ToTreeAttributes(saved);
        Assert.True(saved.GetBool("signalslinkAutomaticPress"));
    }

    [Fact]
    public void A_press_without_the_drive_refuses_to_press()
    {
        var press = new PressFixture();
        press.Behaviors.Remove(press.Drive);

        Assert.False(FruitPressMash.Press(press));
        Assert.False(FruitPressMash.Press(press));
        Assert.False(FruitPressMash.IsPressing(press));
    }

    private sealed class PressFixture : BlockEntityFruitPress
    {
        public readonly FruitPressDrive Drive;
        public readonly ServerAnimator Animator;
        public Action<float> AfterInitialize;
        public Action<float> SqueezeTick;
        public Action<float> LiquidTick;
        public double TotalHours;
        private long nextId;

        public PressFixture()
        {
            var juice = new Item { Code = new AssetLocation("game:juiceportion-apple") };
            Api = Proxy.Make<ICoreServerAPI>((m, a) => m.Name switch
            {
                "get_Side" => EnumAppSide.Server,
                "get_TotalHours" => TotalHours,
                "GetItem" => juice,
                _ => Proxy.Unhandled
            });
            Pos = new Vintagestory.API.MathTools.BlockPos(0, 0, 0, 0);
            var animatable = new BEBehaviorAnimatable(this);
            animatable.Initialize(Api, null);
            Animator = new ServerAnimator(() => 1, [new Animation { Code = "compress", QuantityFrames = 10 }],
                [], new Dictionary<int, AnimationJoint>());
            animatable.animUtil.animator = Animator;
            Drive = new FruitPressDrive(this);
            Behaviors.Add(animatable);
            Behaviors.Add(Drive);
        }

        public void TurnScrewManually() => GetBehavior<BEBehaviorAnimatable>().animUtil
            .activeAnimationsByAnimCode["compress"] = new AnimationMetaData { Code = "compress" };

        public override long RegisterDelayedCallback(Action<float> callback, int interval)
        {
            AfterInitialize = callback;
            return ++nextId;
        }

        public override long RegisterGameTickListener(Action<float> callback, int interval, int offset = 0)
        {
            if (interval == 50) SqueezeTick = callback;
            if (interval == 25) LiquidTick = callback;
            return ++nextId;
        }

        public override void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null) { }
    }
}
