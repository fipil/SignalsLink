using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.repair
{
    /// <summary>
    /// Gives a device back the block entity it lost.
    ///
    /// Vintage Story drops a block entity whose class it cannot find, and the next save writes the
    /// chunk back without it. So a single load of a world while this mod is not loaded - a build
    /// that failed, a mod folder not yet copied - permanently strips every device in whatever
    /// chunks happened to be loaded. The BLOCK survives, because that is just an id in the chunk's
    /// block array; only the thing that made it act is gone. On screen nothing changed, which is
    /// what makes it so confusing: the machine stands there, wired, receiving its signal, doing
    /// nothing, and a freshly placed one beside it works.
    ///
    /// This walks the loaded chunks and builds a block entity wherever a block says it should have
    /// one and has none. It only ever ADDS: a device that still has its own is not touched, and no
    /// block is changed. What cannot come back is what lived inside the lost entity - the paper,
    /// and the pending transfers. Those have to be written again.
    /// </summary>
    public class DeviceRepair : ModSystem
    {
        /// <summary>How far around the caller to look, in blocks, when no radius is given.</summary>
        public const int DefaultRadius = 64;

        /// <summary>The most that may be asked for. A whole world's worth would stall the server.</summary>
        public const int MaxRadius = 512;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.ChatCommands.Create("slrepair")
                .WithDescription("Rebuilds block entities that devices lost when the world was loaded without SignalsLink.")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.OptionalInt("radius", DefaultRadius))
                .HandleWith(args => Run(api, args));
        }

        private static TextCommandResult Run(ICoreServerAPI api, TextCommandCallingArgs args)
        {
            int radius = (int)args[0];
            if (radius < 1) radius = DefaultRadius;
            if (radius > MaxRadius) radius = MaxRadius;

            BlockPos centre = args.Caller.Entity?.Pos?.AsBlockPos;
            if (centre == null) return TextCommandResult.Error("Stand somewhere first.");

            Report report = Repair(api.World, centre, radius, dryRun: false);

            return TextCommandResult.Success(Describe(report, radius));
        }

        /// <summary>What one pass found and did.</summary>
        public sealed class Report
        {
            public int Examined;
            public int Rebuilt;
            public readonly Dictionary<string, int> ByBlock = new Dictionary<string, int>();
        }

        /// <summary>
        /// Walks a cube of the world and rebuilds what is missing. Reads through the world
        /// accessor, so only ground that is actually loaded is looked at - which is the ground the
        /// damage is on anyway, since a chunk had to be loaded to lose anything.
        /// </summary>
        public static Report Repair(IWorldAccessor world, BlockPos centre, int radius, bool dryRun)
        {
            Report report = new Report();
            if (world == null || centre == null) return report;

            IBlockAccessor blocks = world.BlockAccessor;

            int lowest = blocks.MapSizeY > 0 ? 0 : 0;
            int highest = blocks.MapSizeY > 0 ? blocks.MapSizeY - 1 : 255;

            int minY = centre.Y - radius < lowest ? lowest : centre.Y - radius;
            int maxY = centre.Y + radius > highest ? highest : centre.Y + radius;

            BlockPos at = new BlockPos(centre.dimension);

            for (int x = centre.X - radius; x <= centre.X + radius; x++)
            {
                for (int z = centre.Z - radius; z <= centre.Z + radius; z++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        at.Set(x, y, z);

                        Block block = blocks.GetBlock(at);
                        if (!IsOurs(block) || block.EntityClass == null) continue;

                        report.Examined++;

                        if (blocks.GetBlockEntity(at) != null) continue;

                        if (!dryRun) blocks.SpawnBlockEntity(block.EntityClass, at);

                        report.Rebuilt++;

                        string code = block.Code.ToShortString();
                        report.ByBlock.TryGetValue(code, out int seen);
                        report.ByBlock[code] = seen + 1;
                    }
                }
            }

            return report;
        }

        /// <summary>Only this mod's own blocks; other mods' losses are not ours to guess at.</summary>
        private static bool IsOurs(Block block)
        {
            return block?.Code != null && block.Code.Domain == "signalslink";
        }

        private static string Describe(Report report, int radius)
        {
            if (report.Rebuilt == 0)
            {
                return "Nothing to repair within " + radius + " blocks ("
                    + report.Examined + " device(s) checked, all of them intact).";
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.Append("Rebuilt ").Append(report.Rebuilt).Append(" of ").Append(report.Examined)
              .Append(" device(s) within ").Append(radius).Append(" blocks:");

            foreach (KeyValuePair<string, int> one in report.ByBlock)
            {
                sb.Append("\n  ").Append(one.Value).Append("x ").Append(one.Key);
            }

            sb.Append("\nTheir papers were lost with the old entity and have to be written again.");

            return sb.ToString();
        }
    }
}
