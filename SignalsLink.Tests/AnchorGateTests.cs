using System.Reflection;
using SignalsLink.src.signals.chunkanchor;
using Xunit;
using static SignalsLink.Tests.AnchorLifecycleTests;

namespace SignalsLink.Tests
{
    /// <summary>
    /// When chunk anchors are switched off on a server, and what that must never cost.
    ///
    /// Two reasons: the owner does not want them, or a train mod runs without its bridge - an
    /// anchor then leaves a train half loaded across its edge and the train gets stuck. Either way
    /// the anchors' saved settings have to come back untouched once the reason is gone.
    /// </summary>
    public class AnchorGateTests
    {
        [Fact]
        public void Anchors_work_on_an_ordinary_server()
        {
            Assert.Equal(AnchorGate.Open, AnchorGate.Reason(true, trainMod: false, trainBridge: false, allowWithoutBridge: false));
            Assert.Null(AnchorGate.LangKey(AnchorGate.Open));
        }

        [Fact]
        public void A_train_mod_with_its_bridge_is_fine()
        {
            Assert.Equal(AnchorGate.Open, AnchorGate.Reason(true, trainMod: true, trainBridge: true, allowWithoutBridge: false));
        }

        [Fact]
        public void A_train_mod_without_its_bridge_switches_anchors_off()
        {
            string reason = AnchorGate.Reason(true, trainMod: true, trainBridge: false, allowWithoutBridge: false);

            Assert.Equal(AnchorGate.MissingTrainBridge, reason);
            Assert.Equal("signalslink:chunkanchor-disabled-trainbridge", AnchorGate.LangKey(reason));
        }

        [Fact]
        public void The_owner_may_take_that_risk()
        {
            Assert.Equal(AnchorGate.Open, AnchorGate.Reason(true, trainMod: true, trainBridge: false, allowWithoutBridge: true));
        }

        [Fact]
        public void A_bridge_without_the_train_mod_changes_nothing()
        {
            Assert.Equal(AnchorGate.Open, AnchorGate.Reason(true, trainMod: false, trainBridge: true, allowWithoutBridge: false));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void The_owners_switch_comes_first(bool trainMod, bool trainBridge)
        {
            // It is the reason a player can do nothing about but ask, so it is the one they are told.
            string reason = AnchorGate.Reason(false, trainMod, trainBridge, allowWithoutBridge: true);

            Assert.Equal(AnchorGate.ByAdmin, reason);
            Assert.Equal("signalslink:chunkanchor-disabled-admin", AnchorGate.LangKey(reason));
        }

        // ---------------------------------------------------------------- what it must not cost

        [Fact]
        public void Switched_off_anchors_hold_nothing_and_put_nothing_to_sleep()
        {
            Fixture f = SwitchedOff(new Fixture());

            f.Manager.SetColumns(f.Pos, new[] { 0L, 1L });
            f.Manager.Sleep(f.Pos, new[] { 0L }, 42, true);

            Assert.Empty(f.Manager.ColumnsOf(f.Pos));
            Assert.False(f.Manager.IsAsleep(f.Pos));
            Assert.Equal(0, f.Manager.HeldColumns);
        }

        [Fact]
        public void Switched_off_anchors_never_save_over_what_was_saved()
        {
            // A week with anchors off, an autosave every few minutes, a broken anchor, a shutdown:
            // the list written while they worked has to be there when they are switched back on.
            Fixture original = new Fixture(); Call(original.Manager, "Restore");
            original.Manager.SetColumns(original.Pos, new[] { 0L, 1L }); Call(original.Manager, "Store");

            Fixture off = SwitchedOff(new Fixture { Saved = original.Saved });
            Call(off.Manager, "Store");
            off.Manager.Release(off.Pos);
            off.Manager.Dispose();

            Assert.Same(original.Saved, off.Saved);

            Fixture back = new Fixture { Saved = off.Saved }; Call(back.Manager, "Restore");
            Assert.Equal(2, back.Manager.ColumnsOf(back.Pos).Count);
        }

        private static Fixture SwitchedOff(Fixture fixture)
        {
            typeof(ChunkAnchors).GetProperty(nameof(ChunkAnchors.DisabledReason))
                .GetSetMethod(true).Invoke(fixture.Manager, new object[] { AnchorGate.ByAdmin });

            return fixture;
        }
    }
}
