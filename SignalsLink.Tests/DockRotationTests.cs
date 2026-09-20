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
        public void A_point_on_the_north_face_swings_round_to_the_west()
        {
            // A quarter turn: the anchors sit on one face and have to arrive on the next. WEST,
            // because that is the way the engine turns a mesh and a box by a positive angle. This
            // said east while turning was switched off and nothing could show it was the mirror.
            Vec3f turned = BlockManagedDock.TurnedAround(new Vec3f(0.5f, 0.9f, 0.1f), GameMath.PIHALF);

            Assert.Equal(0.1f, turned.X, 4);
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
        public void A_wire_ends_in_the_middle_of_the_box_that_was_clicked()
        {
            // The one thing that must not drift apart: Signals asks for the box and for the end
            // of the wire separately. The two anchors as the blocktype draws them.
            Cuboidf[] anchors =
            {
                new Cuboidf(0.3125f, 0.84375f, 0.0625f, 0.4375f, 0.96875f, 0.1875f),
                new Cuboidf(0.5625f, 0.84375f, 0.0625f, 0.6875f, 0.96875f, 0.1875f),
            };

            foreach (Cuboidf anchor in anchors)
            for (int i = 0; i < BlockManagedDock.Steps; i++)
            {
                float angle = i * GameMath.TWOPI / BlockManagedDock.Steps;

                Cuboidf box = BlockManagedDock.TurnedBox(anchor, angle);
                Vec3f end = BlockManagedDock.TurnedAround(new Vec3f(anchor.MidX, anchor.MidY, anchor.MidZ), angle);

                Assert.Equal(box.MidX, end.X, 4);
                Assert.Equal(box.MidY, end.Y, 4);
                Assert.Equal(box.MidZ, end.Z, 4);
            }
        }

        [Fact]
        public void Only_the_anchors_turn_and_the_body_stays()
        {
            // Found in game: a body box turned to an odd angle swells out of the block, and the
            // crate could be walked through.
            Cuboidf anchor = new Cuboidf(0.3125f, 0.84375f, 0.0625f, 0.4375f, 0.96875f, 0.1875f);
            Cuboidf body = new Cuboidf(0, 0, 0, 1, 0.8125f, 1);

            Cuboidf[] turned = BlockManagedDock.TurnedAnchors(new[] { anchor, body }, 1, GameMath.TWOPI / 16 * 3);

            Assert.Same(body, turned[1]);
            Assert.NotEqual(anchor.MidX, turned[0].MidX, 3);
        }

        [Theory]
        [InlineData(0, -3)]    // north of the crate
        [InlineData(3, 0)]     // east
        [InlineData(0, 3)]     // south
        [InlineData(-3, 0)]    // west
        [InlineData(2, 2)]
        [InlineData(-2, 2)]
        [InlineData(-2, -2)]
        [InlineData(2, -2)]
        [InlineData(3, 1)]     // off the sixteen positions: near enough is near enough
        public void The_anchors_face_whoever_set_it_down(double dx, double dz)
        {
            // The middle of the working face, north as the shape is drawn.
            Vec3f face = BlockManagedDock.TurnedAround(new Vec3f(0.5f, 0.9f, 0.1f), BlockManagedDock.AngleTowards(dx, dz));

            double length = System.Math.Sqrt(dx * dx + dz * dz);
            double towards = ((face.X - 0.5) * dx + (face.Z - 0.5) * dz) / (0.4 * length);

            // 1 is dead on; a sixteenth of a circle either way still looks at them.
            Assert.True(towards > 0.95, "cos of the angle between the face and the player was " + towards);
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
