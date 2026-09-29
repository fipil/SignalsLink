using SignalsTubes.src.programtube;
using Vintagestory.API.Common;

[assembly: ModInfo("Signals Tubes", "signalstubes",
    Description = "Vacuum-tube logic for Signals.",
    Website = "",
    Version = "0.1.0",
    Authors = new[] { "fipil" }
)]

namespace SignalsTubes.src
{
    public class SignalsTubesMod : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterItemClass("ProgramTube", typeof(ItemProgramTube));
        }
    }
}
