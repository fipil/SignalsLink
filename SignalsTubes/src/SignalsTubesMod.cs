using SignalsTubes.src.programtube;
using SignalsTubes.src.socket;
using SignalsTubes.src.imprint;
using SignalsTubes.src.copier;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

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
        public override void StartClientSide(ICoreClientAPI api) => ImprinterNetwork.StartClient(api);
        public override void StartServerSide(ICoreServerAPI api) => ImprinterNetwork.StartServer(api);

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterItemClass("ProgramTube", typeof(ItemProgramTube));
            api.RegisterBlockClass("TubeSocket", typeof(BlockTubeSocket));
            api.RegisterBlockEntityClass("TubeSocket", typeof(BETubeSocket));
            api.RegisterBlockClass("Imprinter", typeof(BlockImprinter));
            api.RegisterBlockEntityClass("Imprinter", typeof(BEImprinter));
            api.RegisterItemClass("ImprinterPlug", typeof(ItemImprinterTool));
            api.RegisterItemClass("ImprinterProbe", typeof(ItemImprinterTool));
            api.RegisterBlockClass("TubeCopier", typeof(BlockTubeCopier));
            api.RegisterBlockEntityClass("TubeCopier", typeof(BETubeCopier));
        }
    }
}
