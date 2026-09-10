using SignalsLink.src.signals.cargo;
using SignalsLink.YTT.src.probe;
using SignalsLink.YTT.src.train;
using Vintagestory.API.Common;

// A loose DLL in the mods folder has no modinfo.json beside it, so the game reads this instead.
// Without it the assembly is refused before anything in it runs - which is what the server was
// complaining about. It has to say the same as modinfo.json, which is what the zip carries.
[assembly: ModInfo("Signals Link YTT", "signalslinkytt",
    Description = "Lets the Signals Link freight dock load and unload the trains of Yang's Transport Tycoon.",
    Website = "",
    Version = "0.1.0",
    Authors = new[] { "fipil" }
)]

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

        public YttSurface Surface { get; private set; }

        public override void StartServerSide(Vintagestory.API.Server.ICoreServerAPI api)
        {
            base.StartServerSide(api);

            if (!api.ModLoader.IsModEnabled(YttModId))
            {
                // Not an error: the manifest asks for YTT, so this only happens when someone has
                // taken it out from under us.
                api.Logger.Notification("[SignalsLink.YTT] " + YttModId + " is not loaded, standing down.");
                return;
            }

            // Startup is the right moment to find out: it is the last one at which the answer can
            // still be "do nothing" rather than "lose somebody's cargo".
            Surface = YttSurface.Probe(api);
            Surface.Report(api, VersionOf(api));

            if (!Surface.CanCarry) return;

            CargoHolderRegistry registry = api.ModLoader.GetModSystem<CargoHolderRegistry>();

            if (registry == null)
            {
                api.Logger.Warning("[SignalsLink.YTT] Signals Link has no cargo holder registry;"
                    + " this bridge needs a newer version of it.");
                return;
            }

            YttPersistence persistence = new YttPersistence(api, Surface.StoragePointsField.DeclaringType.Assembly);
            registry.Register(new TrainCargoHolderFinder(Surface, persistence));

            Enabled = true;
            api.Logger.Notification("[SignalsLink.YTT] trains can be loaded and unloaded.");
        }

        /// <summary>The other mod's version, for the log line that matters when something breaks.</summary>
        private static string VersionOf(ICoreAPI api)
        {
            foreach (Mod mod in api.ModLoader.Mods)
            {
                if (mod.Info?.ModID == YttModId) return mod.Info.Version ?? "?";
            }

            return "?";
        }
    }
}
