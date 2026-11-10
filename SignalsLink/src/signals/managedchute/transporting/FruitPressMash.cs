using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>Finding the fruit press from either of its two blocks.</summary>
    public static class FruitPressAccess
    {
        /// <summary>The press whose TOP block stands at <paramref name="pos"/>; null for anything else.</summary>
        public static BlockEntityFruitPress TopAt(IBlockAccessor ba, BlockPos pos)
        {
            if (ba == null || pos == null || ba.GetBlock(pos) is not BlockFruitPressTop) return null;
            return ba.GetBlockEntity(pos.DownCopy()) as BlockEntityFruitPress;
        }

        /// <summary>The press at <paramref name="pos"/>, whichever of its two blocks that is.</summary>
        public static BlockEntityFruitPress At(IBlockAccessor ba, BlockPos pos)
        {
            if (ba == null || pos == null) return null;
            return ba.GetBlockEntity(pos) as BlockEntityFruitPress ?? TopAt(ba, pos);
        }
    }

    /// <summary>
    /// The mash side of the fruit press, done the way BlockEntityFruitPress.InteractMashContainer
    /// does it, minus the player. Private state the vanilla class keeps for itself (dry count,
    /// animation meta, tick listener) is reached by reflection.
    /// </summary>
    public static class FruitPressMash
    {
        public const double DefaultCapacityLitres = 10.0;
        public const string LitresLeftAttr = "juiceableLitresLeft";
        public const string LitresTransferedAttr = "juiceableLitresTransfered";
        private const string CompressAnim = "compress";

        private static readonly Type PressType = typeof(BlockEntityFruitPress);
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static FieldInfo Field<T>(string name)
        {
            var field = PressType.GetField(name, Private);
            return field?.FieldType == typeof(T) && !field.IsStatic ? field : null;
        }

        private static MethodInfo Method(string name, params Type[] parameters)
        {
            var method = PressType.GetMethod(name, Private, null, parameters, null);
            return method?.ReturnType == typeof(void) ? method : null;
        }

        private static readonly FieldInfo DryStackSizeField = Field<int>("dryStackSize");
        private static readonly FieldInfo CapacityField = Field<double>("juiceableLitresCapacity");
        private static readonly FieldInfo CompressAnimMetaField = Field<AnimationMetaData>("compressAnimMeta");
        private static readonly FieldInfo ListenerIdField = Field<long>("listenerId");
        private static readonly FieldInfo SqueezeSoundPlayedField = Field<bool>("squeezeSoundPlayed");
        private static readonly FieldInfo LastTransferHoursField = Field<double>("lastLiquidTransferTotalHours");
        private static readonly MethodInfo ConvertDryMashMethod = Method("convertDryMash");
        private static readonly MethodInfo ServerTickMethod = Method("onTick100msServer", typeof(float));
        private static readonly MethodInfo UpdateSqueezeRelMethod = Method("updateSqueezeRel", typeof(RunningAnimation));

        internal static bool CanDrive => CompressAnimMetaField != null && ListenerIdField != null
            && SqueezeSoundPlayedField != null && LastTransferHoursField != null && ServerTickMethod != null
            && UpdateSqueezeRelMethod != null;

        internal static void CheckCompatibility(ILogger logger)
        {
            void Check(MemberInfo member, string name, string operations)
            {
                if (member == null) logger.Error("[SignalsLink] Incompatible BlockEntityFruitPress member {0}. Disabled: {1}.", name, operations);
            }
            Check(DryStackSizeField, "dryStackSize", "insertion and dry mash extraction");
            Check(CapacityField, "juiceableLitresCapacity", "insertion");
            Check(CompressAnimMetaField, "compressAnimMeta", "automatic pressing and release");
            Check(ListenerIdField, "listenerId", "automatic pressing and release");
            Check(SqueezeSoundPlayedField, "squeezeSoundPlayed", "automatic pressing");
            Check(LastTransferHoursField, "lastLiquidTransferTotalHours", "automatic pressing");
            Check(ServerTickMethod, "onTick100msServer", "automatic pressing");
            Check(UpdateSqueezeRelMethod, "updateSqueezeRel", "automatic pressing");
            Check(ConvertDryMashMethod, "convertDryMash", "dry mash extraction");
        }

        public static double LitresLeft(ItemStack mash) => mash?.Attributes?.GetDouble(LitresLeftAttr) ?? 0;
        public static double LitresTransfered(ItemStack mash) => mash?.Attributes?.GetDouble(LitresTransferedAttr) ?? 0;

        /// <summary>Squeezed out, the way the press itself decides it (convertDryMash).</summary>
        public static bool IsPressedOut(ItemStack mash) => LitresLeft(mash) < 0.01;

        public static double Capacity(BlockEntityFruitPress press)
            => CapacityField?.GetValue(press) is double d ? d : DefaultCapacityLitres;

        public static int DryStackSize(BlockEntityFruitPress press)
            => DryStackSizeField?.GetValue(press) is int n ? n : 0;

        /// <summary>How many of <paramref name="wanted"/> items still fit, by litres. Vanilla's loop.</summary>
        public static int FitCount(int wanted, float litresPerItem, double currentLitres, double capacity)
        {
            int n = wanted;
            while (n > 0 && Math.Round(n * (double)litresPerItem + currentLitres, 2, MidpointRounding.ToZero) > capacity) n--;
            return n;
        }

        #region Insert

        public static bool CanInsert(BlockEntityFruitPress press, ItemStack stack)
        {
            if (DryStackSizeField == null || CapacityField == null || press == null || stack == null || IsPressing(press)) return false;

            JuiceableProperties props = press.getJuiceableProps(stack);
            if (props == null) return false;
            if (!props.LitresPerItem.HasValue && !stack.Attributes.HasAttribute(LitresLeftAttr)) return false;

            ItemStack mash = press.MashSlot.Itemstack;
            if (mash == null) return true;

            // Wet mash is only ever started in an empty press; merging two mashes is left to the player.
            if (!props.LitresPerItem.HasValue) return false;

            double current = LitresLeft(mash) + LitresTransfered(mash);
            if (Math.Round(current, 2, MidpointRounding.ToZero) >= Capacity(press)) return false;
            if (!SameMash(press, mash, props)) return false;

            return FitCount(1, props.LitresPerItem.Value, current, Capacity(press)) > 0;
        }

        private static bool SameMash(BlockEntityFruitPress press, ItemStack mash, JuiceableProperties props)
        {
            ItemStack pressed = props.PressedStack?.ResolvedItemstack;
            if (pressed == null) return false;

            return mash.Equals(press.Api.World, pressed,
                GlobalConstants.IgnoredStackAttributes.Append(LitresLeftAttr, LitresTransferedAttr, "squeezeRel"));
        }

        /// <summary>Puts up to <paramref name="wanted"/> items of <paramref name="src"/> in. Returns how many went.</summary>
        public static int Insert(BlockEntityFruitPress press, ItemSlot src, int wanted)
        {
            ItemStack stack = src?.Itemstack;
            if (!CanInsert(press, stack) || wanted < 1) return 0;

            JuiceableProperties props = press.getJuiceableProps(stack);
            ItemStack mash = press.MashSlot.Itemstack;

            if (!props.LitresPerItem.HasValue)
            {
                ItemStack placed = stack.GetEmptyClone();
                placed.StackSize = 1;
                press.MashSlot.Itemstack = placed;
                SetDryStackSize(press, placed);
                src.TakeOut(1);
                Done(press, src);
                return 1;
            }

            double current = LitresLeft(mash) + LitresTransfered(mash);
            int n = FitCount(Math.Min(wanted, stack.StackSize), props.LitresPerItem.Value, current, Capacity(press));
            if (n <= 0) return 0;

            if (mash == null)
            {
                mash = props.PressedStack.ResolvedItemstack.Clone();
                press.MashSlot.Itemstack = mash;
            }

            mash.Attributes.SetDouble(LitresLeftAttr, LitresLeft(mash) + n * (double)props.LitresPerItem.Value);
            mash.Attributes.SetDouble(LitresTransferedAttr, LitresTransfered(mash));
            mash.StackSize = 1;
            SetDryStackSize(press, mash);

            src.TakeOut(n);
            Done(press, src);
            return n;
        }

        private static void SetDryStackSize(BlockEntityFruitPress press, ItemStack mash)
        {
            float ratio = press.getJuiceableProps(mash)?.PressedDryRatio ?? 1f;
            int dry = GameMath.RoundRandom(press.Api.World.Rand, (float)(LitresLeft(mash) + LitresTransfered(mash)) * ratio);
            DryStackSizeField?.SetValue(press, dry);
        }

        private static void Done(BlockEntityFruitPress press, ItemSlot src)
        {
            src.MarkDirty();
            MashChanged(press);
        }

        /// <summary>The mash slot changed on the server: sync, and tell clients to redraw the mash.</summary>
        private static void MashChanged(BlockEntityFruitPress press)
        {
            press.MashSlot.MarkDirty();
            press.MarkDirty(true);
            press.Api?.ModLoader.GetModSystem<BucketSlotSync>()?.SendMashChanged(press.Api, press);
        }

        #endregion

        #region Take

        public static bool CanTake(BlockEntityFruitPress press)
            => press != null && !IsPressing(press) && !press.MashSlot.Empty
                && (!IsPressedOut(press.MashSlot.Itemstack) || (ConvertDryMashMethod != null && DryStackSizeField != null));

        /// <summary>
        /// What a take would hand over, without touching the press: the wet mash as it is, or the
        /// dry result once pressed out. Needed because convertDryMash must run exactly once.
        /// </summary>
        public static ItemStack PreviewTake(BlockEntityFruitPress press)
        {
            ItemStack mash = press?.MashSlot?.Itemstack;
            if (mash == null) return null;
            if (!IsPressedOut(mash)) return mash.Clone();

            return PreviewDry(mash, press.getJuiceableProps(mash), DryStackSize(press));
        }

        /// <summary>convertDryMash, computed on a copy.</summary>
        public static ItemStack PreviewDry(ItemStack mash, JuiceableProperties props, int dryStackSize)
        {
            if (mash == null) return null;

            ItemStack returned = props?.ReturnStack?.ResolvedItemstack;
            if (returned != null)
            {
                ItemStack dry = returned.Clone();
                dry.StackSize = (int)(dry.StackSize * Math.Round(LitresTransfered(mash), 2, MidpointRounding.AwayFromZero));
                return dry;
            }

            ItemStack result = mash.Clone();
            result.Attributes?.RemoveAttribute(LitresTransferedAttr);
            result.Attributes?.RemoveAttribute(LitresLeftAttr);
            result.Attributes?.RemoveAttribute("squeezeRel");
            if (result.Collectible?.Code?.Path != "rot") result.StackSize = dryStackSize;
            return result;
        }

        /// <summary>Takes the mash out, converting it first when pressed out. Null when nothing can be taken.</summary>
        public static ItemStack Take(BlockEntityFruitPress press)
        {
            if (!CanTake(press)) return null;

            if (IsPressedOut(press.MashSlot.Itemstack))
            {
                if (ConvertDryMashMethod == null) return null;
                ConvertDryMashMethod.Invoke(press, null);
            }

            ItemStack taken = press.MashSlot.Itemstack;
            press.MashSlot.Itemstack = null;
            MashChanged(press);
            return taken;
        }

        #endregion

        #region Screw

        public static bool IsPressing(BlockEntityFruitPress press)
            => press != null && press.CompressAnimActive;

        internal static void DriveSqueeze(BlockEntityFruitPress press)
        {
            RunningAnimation anim = press.GetBehavior<BEBehaviorAnimatable>()?.animUtil?.animator?.GetAnimationState(CompressAnim);
            if (anim != null) UpdateSqueezeRelMethod.Invoke(press, new object[] { anim });
        }

        // Press blocks already reported as having no drive behavior, by block code.
        private static readonly System.Collections.Generic.HashSet<string> missingDriveReported = new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// The drive comes from a JSON patch of the vanilla press. Where it did not land (another
        /// mod replaced the block type, or brings a press of its own) `do press` cannot work; said
        /// once per block code. Always false.
        /// </summary>
        private static bool ReportMissingDrive(BlockEntityFruitPress press)
        {
            string code = press.Block?.Code?.ToString() ?? "?";
            if (missingDriveReported.Add(code))
            {
                press.Api?.Logger?.Error("[SignalsLink] Fruit press {0} has no SignalsLinkFruitPressDrive behavior (patch of game:blocktypes/wood/fruitpress.json did not apply). Disabled: automatic pressing.", code);
            }
            return false;
        }

        /// <summary>Turns the screw, as the player's first click does (packet ScrewStart). False when it already is.</summary>
        public static bool Press(BlockEntityFruitPress press)
        {
            if (!CanDrive || press?.Api is not ICoreServerAPI sapi || press.CompressAnimActive) return false;
            var drive = press.GetBehavior<FruitPressDrive>();
            if (drive == null) return ReportMissingDrive(press);
            if (CompressAnimMetaField?.GetValue(press) is not AnimationMetaData meta) return false;
            BlockEntityAnimationUtil animUtil = press.GetBehavior<BEBehaviorAnimatable>()?.animUtil;
            if (animUtil == null) return false;

            meta.AnimationSpeed = 0.5f;
            animUtil.StartAnimation(meta);
            SqueezeSoundPlayedField?.SetValue(press, false);
            LastTransferHoursField?.SetValue(press, press.Api.World.Calendar.TotalHours);

            if (ListenerIdField != null && ServerTickMethod != null && ListenerIdField.GetValue(press) is long id && id == 0L)
            {
                var tick = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), press, ServerTickMethod);
                ListenerIdField.SetValue(press, press.RegisterGameTickListener(tick, 25));
            }

            sapi.Network.BroadcastBlockEntityPacket(press.Pos, 1001, new FruitPressAnimPacket
            {
                AnimationState = EnumFruitPressAnimState.ScrewStart,
                AnimationSpeed = 0.5f
            });
            drive.Start();
            press.MarkDirty();
            return true;
        }

        /// <summary>Lets the screw back up (packet Unscrew). False when there is nothing to release.</summary>
        public static bool Release(BlockEntityFruitPress press)
        {
            if (ListenerIdField == null || press == null || !press.CanUnscrew || press.Api is not ICoreServerAPI sapi) return false;
            if (CompressAnimMetaField?.GetValue(press) is not AnimationMetaData meta) return false;
            BlockEntityAnimationUtil animUtil = press.GetBehavior<BEBehaviorAnimatable>()?.animUtil;
            if (animUtil == null) return false;

            press.GetBehavior<FruitPressDrive>()?.Stop();
            meta.AnimationSpeed = 1.5f;
            animUtil.StopAnimation(CompressAnim);
            sapi.Network.BroadcastBlockEntityPacket(press.Pos, 1001, new FruitPressAnimPacket
            {
                AnimationState = EnumFruitPressAnimState.Unscrew,
                AnimationSpeed = 1.5f
            });
            animUtil.animator.GetAnimationState(CompressAnim)?.Stop();

            if (press.MashSlot.Empty && ListenerIdField?.GetValue(press) is long id && id != 0L)
            {
                press.UnregisterGameTickListener(id);
                ListenerIdField.SetValue(press, 0L);
            }
            return true;
        }

        #endregion
    }
}
