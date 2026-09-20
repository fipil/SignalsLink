using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.YTT.src.dump
{
    /// <summary>
    /// `/slytt dump [radius]` - what YTT believes about its trains, in chat and in the server log.
    ///
    /// Registered whenever YTT is loaded, probe or no probe: a bridge that stood down is exactly
    /// when somebody wants to see why.
    /// </summary>
    public static class SlyttCommand
    {
        /// <summary>Chat is not made for reading pages; the log gets everything.</summary>
        private const int ChatLines = 120;

        public static void Register(ICoreServerAPI api)
        {
            api.ChatCommands.Create("slytt")
                .WithDescription("Signals Link YTT bridge. dump [radius]: YTT's convoy bookkeeping"
                    + " (authority, virtual convoys, loaded convoys, occupancy), plus every vehicle"
                    + " within radius blocks of you.")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("action"), api.ChatCommands.Parsers.OptionalInt("radius"))
                .HandleWith(args => Handle(api, args));
        }

        private static TextCommandResult Handle(ICoreServerAPI api, TextCommandCallingArgs args)
        {
            string action = args.Parsers[0].IsMissing ? "dump" : ((string)args[0]).ToLowerInvariant();

            if (action != "dump") return TextCommandResult.Error("Usage: /slytt dump [radius]");

            int radius = args.Parsers[1].IsMissing ? 0 : (int)args[1];
            Vec3d around = args.Caller?.Pos;

            if (radius > 0 && around == null)
            {
                return TextCommandResult.Error("A radius needs a position; run this in game.");
            }

            string dump;

            try
            {
                dump = YttStateDump.Write(api, around, radius);
            }
            catch (Exception e)
            {
                api.Logger.Error("[SignalsLink.YTT] dump failed: " + e);
                return TextCommandResult.Error("Dump failed: " + e.Message + " (details in the server log)");
            }

            api.Logger.Notification("[SignalsLink.YTT] dump\n" + dump);

            string[] lines = dump.Split('\n');
            if (lines.Length <= ChatLines) return TextCommandResult.Success(dump);

            return TextCommandResult.Success(string.Join("\n", lines, 0, ChatLines)
                + "\n... " + (lines.Length - ChatLines) + " more lines; the whole dump is in server-main.log");
        }
    }
}
