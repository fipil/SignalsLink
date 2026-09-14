using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace YttRepro
{
    /// <summary>
    /// `/yttrepro build | split | check | untether | clean` - reproduces a convoy that YTT leaves
    /// stuck forever once part of it has been unloaded while its head stays loaded.
    ///
    /// Stand-alone on purpose: game + YTT, nothing else, so that the report can be re-run by
    /// anyone with the mod. YTT is reached by reflection only (YttPeek).
    /// </summary>
    public class YttReproMod : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            if (!api.ModLoader.IsModEnabled("yangtransport"))
            {
                api.Logger.Notification("[yttrepro] yangtransport is not loaded, standing down.");
                return;
            }

            ReproRig rig = new ReproRig(api);

            api.ChatCommands.Create("yttrepro")
                .WithDescription("YTT stuck-convoy reproduction: build (track + coupled train across a chunk column boundary),"
                    + " split (unload the wagons like a chunk unload), check (STUCK / RECOVERED / COMPLETE),"
                    + " untether (what the linkage pole would do), clean.")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("action"))
                .HandleWith(args =>
                {
                    IServerPlayer player = args.Caller.Player as IServerPlayer;
                    string action = args.Parsers[0].IsMissing ? "check" : ((string)args[0]).ToLowerInvariant();

                    try
                    {
                        return action switch
                        {
                            "build" => TextCommandResult.Success(rig.Build(player)),
                            "split" => TextCommandResult.Success(rig.Split()),
                            "check" => TextCommandResult.Success(rig.Check()),
                            "untether" => TextCommandResult.Success(rig.Untether(player)),
                            "clean" => TextCommandResult.Success(rig.Clean()),
                            _ => TextCommandResult.Error("Usage: /yttrepro build | split | check | untether | clean"),
                        };
                    }
                    catch (Exception e)
                    {
                        api.Logger.Error("[yttrepro] " + action + " failed: " + e);
                        return TextCommandResult.Error(action + " failed: " + e.GetBaseException().Message + " (details in the server log)");
                    }
                });
        }
    }
}
