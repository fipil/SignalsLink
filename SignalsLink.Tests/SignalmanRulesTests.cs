using SignalsLink.YTT.src.probe;
using SignalsLink.YTT.src.signalman;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The signalman's arithmetic: what a pin value orders, what the output says, what a power
    /// level means in degrees, how often an order is given, and what the probe accepts.
    /// </summary>
    public class SignalmanRulesTests
    {
        // ---------------------------------------------------------------- the input pin

        [Theory]
        [InlineData(0, SignalmanCommand.Nothing)]
        [InlineData(1, SignalmanCommand.Throttle)]
        [InlineData(10, SignalmanCommand.Throttle)]
        [InlineData(11, SignalmanCommand.Nothing)]
        [InlineData(14, SignalmanCommand.Nothing)]
        [InlineData(15, SignalmanCommand.Whistle)]
        public void A_pin_value_is_an_order_or_nothing(byte input, SignalmanCommand expected)
        {
            Assert.Equal(expected, SignalmanRules.CommandFor(input));
        }

        // ---------------------------------------------------------------- the output pin

        [Fact]
        public void The_output_says_nothing_moving_or_standing()
        {
            Assert.Equal(0, SignalmanRules.OutputFor(false, false));
            Assert.Equal(0, SignalmanRules.OutputFor(false, true));
            Assert.Equal(1, SignalmanRules.OutputFor(true, false));
            Assert.Equal(2, SignalmanRules.OutputFor(true, true));
        }

        // ---------------------------------------------------------------- power in degrees

        [Theory]
        [InlineData(10, 1200, 1200)]   // ten is exactly the safe maximum
        [InlineData(5, 1200, 600)]
        [InlineData(7, 1200, 850)]     // 840, snapped to the dialog's fifties
        [InlineData(1, 1200, 100)]     // 120 rounds down to 100
        [InlineData(3, 1200, 350)]     // 360 rounds up to 350
        [InlineData(10, 1800, 1500)]   // never past the dialog's own ceiling
        [InlineData(0, 1200, 100)]     // below the range counts as one
        [InlineData(12, 1200, 1200)]   // above it counts as ten
        public void A_level_is_tenths_of_the_safe_maximum_in_fifties(int level, double maximum, int expected)
        {
            Assert.Equal(expected, SignalmanRules.TargetTemperature(level, maximum));
        }

        // ---------------------------------------------------------------- once per train

        [Fact]
        public void An_order_is_given_once_to_each_train()
        {
            SignalmanMemory memory = new SignalmanMemory();

            Assert.True(memory.ShouldApply(41, 7));
            memory.Applied(41, 7);

            Assert.False(memory.ShouldApply(41, 7));   // the same train, the same order
            Assert.True(memory.ShouldApply(42, 7));    // another train
            Assert.True(memory.ShouldApply(41, 8));    // the same train, a changed order
        }

        [Fact]
        public void A_train_that_left_gets_the_order_again_when_it_returns()
        {
            SignalmanMemory memory = new SignalmanMemory();

            memory.Applied(41, 7);
            memory.Forget();

            Assert.True(memory.ShouldApply(41, 7));
        }


        // ---------------------------------------------------------------- the mode

        [Fact]
        public void The_wrench_cycles_both_right_left()
        {
            Assert.Equal(SignalmanMode.Right, SignalmanRules.NextMode(SignalmanMode.Both));
            Assert.Equal(SignalmanMode.Left, SignalmanRules.NextMode(SignalmanMode.Right));
            Assert.Equal(SignalmanMode.Both, SignalmanRules.NextMode(SignalmanMode.Left));
        }

        [Fact]
        public void A_mode_variant_reads_back_as_itself()
        {
            foreach (SignalmanMode mode in new[] { SignalmanMode.Both, SignalmanMode.Right, SignalmanMode.Left })
            {
                Assert.Equal(mode, SignalmanRules.ModeFor(SignalmanRules.VariantOf(mode)));
            }

            Assert.Equal(SignalmanMode.Both, SignalmanRules.ModeFor(null));
        }

        [Theory]
        [InlineData(SignalmanMode.Both, TravelDirection.Right, true)]
        [InlineData(SignalmanMode.Both, TravelDirection.Left, true)]
        [InlineData(SignalmanMode.Both, TravelDirection.Unknown, true)]
        [InlineData(SignalmanMode.Right, TravelDirection.Right, true)]
        [InlineData(SignalmanMode.Right, TravelDirection.Left, false)]
        [InlineData(SignalmanMode.Right, TravelDirection.Unknown, false)]
        [InlineData(SignalmanMode.Left, TravelDirection.Left, true)]
        [InlineData(SignalmanMode.Left, TravelDirection.Right, false)]
        [InlineData(SignalmanMode.Left, TravelDirection.Unknown, false)]
        public void A_one_way_mode_takes_only_trains_it_can_place(SignalmanMode mode, TravelDirection direction, bool expected)
        {
            Assert.Equal(expected, SignalmanRules.ModeMatches(mode, direction));
        }

        [Fact]
        public void A_centimetre_is_not_a_direction()
        {
            Assert.Equal(TravelDirection.Unknown, SignalmanRules.DirectionOf(0.005));
            Assert.Equal(TravelDirection.Right, SignalmanRules.DirectionOf(0.3));
            Assert.Equal(TravelDirection.Left, SignalmanRules.DirectionOf(-0.3));
        }

        [Fact]
        public void The_players_pin_stops_at_fifteen()
        {
            Assert.Equal(0, SignalmanRules.PlayersOutput(0));
            Assert.Equal(3, SignalmanRules.PlayersOutput(3));
            Assert.Equal(15, SignalmanRules.PlayersOutput(40));
        }

        // ---------------------------------------------------------------- which way it went

        [Fact]
        public void A_train_seen_moving_keeps_its_direction_while_it_stands()
        {
            TravelDirections directions = new TravelDirections();

            Assert.Equal(TravelDirection.Unknown, directions.Observe(7, new[] { (1L, 10.0) }, 0));
            Assert.Equal(TravelDirection.Right, directions.Observe(7, new[] { (1L, 11.0) }, 250));
            Assert.Equal(TravelDirection.Right, directions.Observe(7, new[] { (1L, 11.0) }, 500));
            Assert.Equal(TravelDirection.Right, directions.Observe(7, new[] { (1L, 11.0) }, 750));
        }

        [Fact]
        public void A_train_first_seen_standing_has_no_direction()
        {
            TravelDirections directions = new TravelDirections();

            directions.Observe(7, new[] { (1L, 10.0) }, 0);
            Assert.Equal(TravelDirection.Unknown, directions.Observe(7, new[] { (1L, 10.0) }, 250));
        }

        [Fact]
        public void Reversing_changes_the_direction()
        {
            TravelDirections directions = new TravelDirections();

            directions.Observe(7, new[] { (1L, 10.0) }, 0);
            directions.Observe(7, new[] { (1L, 11.0) }, 250);
            Assert.Equal(TravelDirection.Left, directions.Observe(7, new[] { (1L, 10.5) }, 500));
        }

        [Fact]
        public void A_train_gone_long_enough_is_forgotten()
        {
            TravelDirections directions = new TravelDirections();

            directions.Observe(7, new[] { (1L, 10.0) }, 0);
            directions.Observe(7, new[] { (1L, 11.0) }, 250);
            directions.Forget(250 + TravelDirections.MemoryMs);

            Assert.Equal(TravelDirection.Unknown, directions.Observe(7, new[] { (1L, 11.0) }, 250 + TravelDirections.MemoryMs));
        }

        [Fact]
        public void Two_looks_in_the_same_tick_do_not_count_as_movement()
        {
            TravelDirections directions = new TravelDirections();

            directions.Observe(7, new[] { (1L, 10.0) }, 0);
            directions.Observe(7, new[] { (1L, 11.0) }, 250);
            Assert.Equal(TravelDirection.Right, directions.Observe(7, new[] { (1L, 9.0) }, 250));
        }

        // ---------------------------------------------------------------- the probe

        private sealed class ConfigLikeToday
        {
            public readonly double MaxTemperatureCelsius = 1200;
        }

        private sealed class ConfigAsProperties
        {
            public double MaxTemperatureCelsius => 1200;
        }

        private sealed class ConfigRetyped
        {
            public readonly float MaxTemperatureCelsius = 1200;
        }

        private sealed class ControllerLikeToday
        {
            public double LimitTemperatureC { get; private set; } = -1;
            public bool ThermostatLockedToMaximum => false;
            public ConfigLikeToday Configuration { get; private set; } = new ConfigLikeToday();
        }

        private sealed class ControllerWithPropertyConfig
        {
            public double LimitTemperatureC { get; set; }
            public bool ThermostatLockedToMaximum => false;
            public ConfigAsProperties Configuration => new ConfigAsProperties();
        }

        private sealed class ControllerWithReadOnlyLimit
        {
            public double LimitTemperatureC => 0;
            public bool ThermostatLockedToMaximum => false;
            public ConfigLikeToday Configuration => new ConfigLikeToday();
        }

        private sealed class ControllerWithRetypedMaximum
        {
            public double LimitTemperatureC { get; private set; }
            public bool ThermostatLockedToMaximum => false;
            public ConfigRetyped Configuration => new ConfigRetyped();
        }

        [Fact]
        public void Todays_controller_offers_a_throttle()
        {
            ThrottleMembers throttle = ThrottleMembers.On(typeof(ControllerLikeToday));

            Assert.NotNull(throttle);
            Assert.NotNull(throttle.Limit.GetSetMethod(true));
        }

        [Fact]
        public void The_private_setter_really_sets()
        {
            // The whole point of the write path: a private setter is reached through reflection.
            ControllerLikeToday controller = new ControllerLikeToday();
            ThrottleMembers throttle = ThrottleMembers.On(typeof(ControllerLikeToday));

            throttle.Limit.SetValue(controller, 850.0);

            Assert.Equal(850.0, controller.LimitTemperatureC);
            Assert.Equal(1200.0, ThrottleMembers.Read(throttle.MaxTemperature, controller.Configuration));
        }

        [Fact]
        public void A_maximum_that_became_a_property_still_reads()
        {
            ThrottleMembers throttle = ThrottleMembers.On(typeof(ControllerWithPropertyConfig));

            Assert.NotNull(throttle);
            Assert.Equal(1200.0, ThrottleMembers.Read(throttle.MaxTemperature, new ConfigAsProperties()));
        }

        [Fact]
        public void A_limit_with_no_setter_at_all_means_no_throttle()
        {
            Assert.Null(ThrottleMembers.On(typeof(ControllerWithReadOnlyLimit)));
        }

        [Fact]
        public void A_retyped_maximum_means_no_throttle()
        {
            Assert.Null(ThrottleMembers.On(typeof(ControllerWithRetypedMaximum)));
        }

        private sealed class BehaviorLikeToday
        {
            internal bool ServerTryPlayWhistle() => true;
        }

        private sealed class BehaviorWhoseWhistleTakesAnArgument
        {
            internal bool ServerTryPlayWhistle(float volume) => true;
        }

        [Fact]
        public void The_whistle_is_found_even_though_it_is_internal()
        {
            Assert.NotNull(YttSurface.WhistleOn(typeof(BehaviorLikeToday)));
        }

        [Fact]
        public void A_whistle_with_a_new_signature_is_not_the_whistle()
        {
            Assert.Null(YttSurface.WhistleOn(typeof(BehaviorWhoseWhistleTakesAnArgument)));
            Assert.Null(YttSurface.WhistleOn(null));
        }
    }
}
