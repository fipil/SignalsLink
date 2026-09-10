using SignalsLink.src.signals.cargo;
using Vintagestory.API.Common;

namespace SignalsLink.YTT.src
{
    /// <summary>
    /// The bridge between Signals Link and Yang's Transport Tycoon.
    ///
    /// It exists as a mod of its own for one reason: <b>everything volatile lives here.</b> The
    /// dock, the paper, the sections and the storage yard are all ordinary Signals Link and have
    /// nothing to do with trains. What rides on the insides of another mod — the reflection, the
    /// reach into the steam engine, the version probe — is all in this assembly, so when YTT
    /// changes, this is the only thing that has to be released again.
    ///
    /// <b>There is no reference to yangtransport.dll</b>, here or in the project file, and there
    /// must never be one. Everything goes through reflection over types the class registry hands
    /// out, behind a probe that checks the surface is still what this was written against. A
    /// missing method then degrades loudly instead of taking the server down.
    /// </summary>
    public class SignalsLinkYttMod : ModSystem
    {
        /// <summary>The mod this bridges to. Asked for by name; never linked against.</summary>
        public const string YttModId = "yangtransport";

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        /// <summary>
        /// True once the bridge has found everything it needs. Until then it does nothing at all —
        /// half a bridge is worse than none.
        /// </summary>
        public bool Enabled { get; private set; }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            if (!api.ModLoader.IsModEnabled(YttModId))
            {
                // Not an error: the manifest asks for YTT, so this only happens when someone has
                // taken it out from under us.
                api.Logger.Notification("[SignalsLink.YTT] " + YttModId + " is not loaded, standing down.");
                return;
            }

            Enabled = true;
        }

        /// <summary>
        /// Registers what this bridge adds to Signals Link. Nothing yet — the train holder, the
        /// reflection layer and the probe come next.
        /// </summary>
        private void Register(ICoreAPI api)
        {
            CargoHolderRegistry registry = api.ModLoader.GetModSystem<CargoHolderRegistry>();
            if (registry == null) return;
        }
    }
}
