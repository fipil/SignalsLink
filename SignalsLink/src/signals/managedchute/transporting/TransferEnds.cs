using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// One end of a transfer that is not a plain container: a block offered through a view
    /// (its slots, or virtual slots standing in for state the block keeps elsewhere).
    /// </summary>
    public interface ITransferEnd
    {
        BlockPos Pos { get; }

        /// <summary>What the ordinary inventory routes and the paper conditions see.</summary>
        IInventory View { get; }

        /// <summary>Called after a move changed this end, so the block can persist and redraw.</summary>
        void Changed(ICoreAPI api);
    }

    /// <summary>
    /// Offers ends for blocks the core does not know, typically another mod's. Registered with
    /// <see cref="TransferEndRegistry"/>; nothing changes while none is registered.
    /// </summary>
    public interface ITransferEndProvider
    {
        /// <summary>
        /// The end at <paramref name="pos"/> as seen from a device at <paramref name="devicePos"/>
        /// (null when the caller only reads), or null when the block is not one of this provider's.
        /// </summary>
        ITransferEnd At(ICoreAPI api, BlockPos pos, BlockPos devicePos);
    }

    public class TransferEndRegistry : ModSystem
    {
        private readonly List<ITransferEndProvider> providers = new List<ITransferEndProvider>();
        private readonly HashSet<Type> failed = new HashSet<Type>();

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public IReadOnlyList<ITransferEndProvider> Providers => providers;

        public void Register(ITransferEndProvider provider)
        {
            if (provider == null || providers.Contains(provider)) return;
            providers.Add(provider);
        }

        public ITransferEnd At(ICoreAPI api, BlockPos pos, BlockPos devicePos)
        {
            if (pos == null) return null;

            foreach (ITransferEndProvider provider in providers)
            {
                try
                {
                    ITransferEnd end = provider.At(api, pos, devicePos);
                    if (end != null) return end;
                }
                catch (Exception e)
                {
                    // A broken provider must not take the device with it; said once per provider.
                    if (failed.Add(provider.GetType()))
                    {
                        api?.Logger?.Error("[SignalsLink] transfer end provider " + provider.GetType().Name + " failed and is ignored from now on: " + e);
                    }
                }
            }

            return null;
        }

        /// <summary>The registered end at pos, or null (also when no registry is loaded).</summary>
        public static ITransferEnd Find(ICoreAPI api, BlockPos pos, BlockPos devicePos)
            => api?.ModLoader?.GetModSystem<TransferEndRegistry>()?.At(api, pos, devicePos);
    }
}
