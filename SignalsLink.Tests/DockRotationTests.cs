using SignalsLink.src.signals.manageddock;
using Vintagestory.API.MathTools;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The dock turns by an angle on its block entity, the way a vanilla crate does, rather than by
    /// a block variant. Four codes cannot say "roughly facing me", and a code per angle would be a
    /// hundred blocks in the creative inventory.
    /// </summary>
    public class DockRotationTests
    {
        [Fact]
        public void The_circle_is_cut_into_even_steps()
        {
            Assert.Equal(0, BlockManagedDock.Snap(0));
            Assert.Equal(GameMath.TWOPI / BlockManagedDock.Steps, BlockManagedDock.Snap(GameMath.TWOPI / BlockManagedDock.Steps), 5);
        }

        [Fact]
        public void An_angle_between_steps_goes_to_the_nearer_one()
        {
            float step = GameMath.TWOPI / BlockManagedDock.Steps;

            Assert.Equal(0, BlockManagedDock.Snap(step * 0.4f), 5);
            Assert.Equal(step, BlockManagedDock.Snap(step * 0.6f), 5);
        }

        [Fact]
        public void A_full_turn_comes_back_to_nothing()
        {
            // Without this the angle creeps up every time somebody faces north and the crate ends
            // up storing a number nobody can read.
            Assert.Equal(0, BlockManagedDock.Snap(GameMath.TWOPI), 5);
        }

        [Fact]
        public void Every_step_survives_the_rounding()
        {
            float step = GameMath.TWOPI / BlockManagedDock.Steps;

            for (int i = 0; i < BlockManagedDock.Steps; i++)
            {
                Assert.Equal(i * step, BlockManagedDock.Snap(i * step), 4);
            }
        }

        // ---------------------------------------------------------------- the anchors follow

        [Fact]
        public void A_point_on_the_north_face_swings_round_to_the_east()
        {
            // A quarter turn: the anchors sit on one face and have to arrive on the next.
            Vec3f turned = BlockManagedDock.TurnedAround(new Vec3f(0.5f, 0.9f, 0.1f), GameMath.PIHALF);

            Assert.Equal(0.9f, turned.X, 4);
            Assert.Equal(0.9f, turned.Y, 4);
            Assert.Equal(0.5f, turned.Z, 4);
        }

        [Fact]
        public void Half_a_turn_puts_it_on_the_far_side()
        {
            Vec3f turned = BlockManagedDock.TurnedAround(new Vec3f(0.5f, 0.9f, 0.1f), GameMath.PI);

            Assert.Equal(0.5f, turned.X, 4);
            Assert.Equal(0.9f, turned.Z, 4);
        }

        [Fact]
        public void No_turn_leaves_it_where_it_was()
        {
            Vec3f point = new Vec3f(0.3125f, 0.9f, 0.0625f);
            Vec3f turned = BlockManagedDock.TurnedAround(point, 0);

            Assert.Equal(point.X, turned.X, 5);
            Assert.Equal(point.Z, turned.Z, 5);
        }

        [Fact]
        public void Height_is_never_touched()
        {
            // It turns level. An anchor that drifted up or down would part company with its box.
            for (int i = 0; i < BlockManagedDock.Steps; i++)
            {
                float angle = i * GameMath.TWOPI / BlockManagedDock.Steps;

                Assert.Equal(0.84375f, BlockManagedDock.TurnedAround(new Vec3f(0.3f, 0.84375f, 0.1f), angle).Y, 5);
            }
        }
    }
}
