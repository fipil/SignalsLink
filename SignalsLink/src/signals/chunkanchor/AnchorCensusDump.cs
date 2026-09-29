using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>Counts things by name and writes them out, most first.</summary>
    public sealed class CensusTally
    {
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        public int Total { get; private set; }

        public void Add(string name, int howMany = 1)
        {
            name = string.IsNullOrEmpty(name) ? "?" : name;

            counts.TryGetValue(name, out int before);
            counts[name] = before + howMany;
            Total += howMany;
        }

        /// <summary>Most first; equal counts by name, so two dumps of the same place read the same.</summary>
        public IEnumerable<KeyValuePair<string, int>> Ranked()
        {
            return counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal);
        }

        public void WriteTo(StringBuilder text, string title)
        {
            text.AppendLine(title + " (" + Total + ")");

            foreach (KeyValuePair<string, int> pair in Ranked())
            {
                text.AppendLine("  " + pair.Value.ToString().PadLeft(6) + "  " + pair.Key);
            }

            text.AppendLine();
        }
    }

    /// <summary>
    /// <c>/slanchor dump</c>: what the anchor being looked at counted, written to a file.
    ///
    /// The anchor quotes one number, and a number cannot be argued with. This says what is behind
    /// it - by the same walk over the same chunks the census makes, so the totals have to agree.
    /// </summary>
    public static class AnchorCensusDump
    {
        public const string FileName = "signalslink-anchor-dump.txt";

        public static void Register(ICoreServerAPI api, ChunkAnchors anchors)
        {
            api.ChatCommands.Create("slanchor")
                .WithDescription("Signals Link chunk anchor. dump: look at an anchor, and what it counted is written to "
                    + FileName + " in the Logs folder.")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("action"))
                .HandleWith(args => Handle(api, anchors, args));
        }

        private static TextCommandResult Handle(ICoreServerAPI api, ChunkAnchors anchors, TextCommandCallingArgs args)
        {
            string action = args.Parsers[0].IsMissing ? "dump" : (string)args[0];
            if (action != "dump") return TextCommandResult.Error("Usage: /slanchor dump");

            BlockPos looking = args.Caller.Player?.CurrentBlockSelection?.Position;

            if (looking == null || api.World.BlockAccessor.GetBlockEntity(looking) is not BEChunkAnchor anchor)
            {
                return TextCommandResult.Error("Look at a chunk anchor first.");
            }

            try
            {
                string text = Describe(api, anchors, anchor);
                string path = Path.Combine(GamePaths.Logs, FileName);

                File.WriteAllText(path, text, new UTF8Encoding(false));

                return TextCommandResult.Success("Written to " + path);
            }
            catch (Exception e)
            {
                api.Logger.Error("[SignalsLink] anchor dump failed: " + e);
                return TextCommandResult.Error("Dump failed: " + e.Message + " (details in the server log)");
            }
        }

        private static string Describe(ICoreServerAPI api, ChunkAnchors anchors, BEChunkAnchor anchor)
        {
            StringBuilder text = new StringBuilder();

            List<long> columns = new List<long>(anchor.Columns);
            List<long> halo = new List<long>(anchors?.HaloOf(anchor.Pos) ?? Array.Empty<long>());

            text.AppendLine("Signals Link chunk anchor dump, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("anchor \"" + anchor.AnchorName + "\" at " + anchor.Pos);
            text.AppendLine("columns set: " + columns.Count + ", extra columns kept for a train: " + halo.Count);
            text.AppendLine("the anchor itself says: " + anchor.ActiveBlocks + " active blocks, " + anchor.Creatures
                + " creatures, census " + (anchor.CensusReady ? "ready" : "NOT ready"));
            text.AppendLine();

            CensusTally byClass = new CensusTally();
            CensusTally byBlock = new CensusTally();
            CensusTally byFamily = new CensusTally();
            CensusTally idleBlocks = new CensusTally();
            CensusTally creatures = new CensusTally();
            CensusTally otherEntities = new CensusTally();
            StringBuilder perColumn = new StringBuilder();

            int chunksHigh = (api.WorldManager.MapSizeY + 31) / 32;

            foreach (long key in columns.Concat(halo))
            {
                (int x, int z) = AnchorArea.Of(key);
                int blocksHere = 0, creaturesHere = 0;
                StringBuilder levels = new StringBuilder();

                for (int y = 0; y < chunksHigh; y++)
                {
                    IServerChunk chunk = api.WorldManager.GetChunk(x, y, z);

                    if (chunk == null)
                    {
                        levels.Append(" y" + y + "=unloaded");
                        continue;
                    }

                    int onThisLevel = 0;

                    foreach (BlockEntity entity in chunk.BlockEntities?.Values ?? Enumerable.Empty<BlockEntity>())
                    {
                        string code = entity?.Block?.Code?.ToString();

                        // The same test the census makes.
                        if (!AnchorCensus.IsActive(entity))
                        {
                            idleBlocks.Add(Family(entity?.Block?.Code));
                            continue;
                        }

                        byClass.Add(entity?.GetType().Name);
                        byBlock.Add(code);
                        byFamily.Add(Family(entity?.Block?.Code));
                        onThisLevel++;
                    }

                    if (onThisLevel > 0) levels.Append(" y" + y + "=" + onThisLevel);
                    blocksHere += onThisLevel;

                    Entity[] entities = chunk.Entities;

                    for (int i = 0; entities != null && i < chunk.EntitiesCount && i < entities.Length; i++)
                    {
                        Entity entity = entities[i];
                        if (entity == null) continue;

                        // The same test the census makes.
                        if (AnchorCensus.IsLivestock(entity))
                        {
                            creatures.Add(entity.Code?.ToString());
                            creaturesHere++;
                        }
                        else
                        {
                            otherEntities.Add(entity.Code?.ToString());
                        }
                    }
                }

                perColumn.AppendLine("  column " + x + "," + z + " (blocks " + x * 32 + ".." + (x * 32 + 31) + ", " + z * 32 + ".." + (z * 32 + 31) + ")"
                    + (halo.Contains(key) ? " [train]" : "") + ": " + blocksHere + " active blocks, " + creaturesHere
                    + " creatures |" + levels);
            }

            SignalsLinkConfig config = SignalsLinkConfigLoader.Current;
            int held = columns.Count + halo.Count;
            float units = AnchorCensus.AnchorUnits(byClass.Total, creatures.Total, held, config.AnchorCreatureWeight, config.AnchorColumnWeight);

            text.AppendLine("counted now: " + byClass.Total + " active blocks, " + creatures.Total + " creatures, " + held + " columns");
            text.AppendLine("units = " + byClass.Total + " x " + AnchorCensus.ActiveBlockWeight + " + " + creatures.Total + " x "
                + config.AnchorCreatureWeight + " + " + held + " x " + config.AnchorColumnWeight + " = " + units);
            text.AppendLine("reference load " + config.AnchorReferenceLoad + ", exponent " + config.AnchorPriceExponent
                + ", days per gear as the anchor quotes it: " + anchor.DaysPerGear().ToString("0.#"));
            text.AppendLine();

            text.AppendLine("PER COLUMN (y = vertical chunk of 32 blocks, counted from the bottom of the world)");
            text.Append(perColumn);
            text.AppendLine();

            byFamily.WriteTo(text, "ACTIVE BLOCKS BY FAMILY (block code up to the first dash)");
            byClass.WriteTo(text, "ACTIVE BLOCKS BY BLOCK ENTITY CLASS");
            byBlock.WriteTo(text, "ACTIVE BLOCKS BY BLOCK CODE");
            idleBlocks.WriteTo(text, "BLOCK ENTITIES NOT COUNTED (they do not tick, or are chiselled blocks or piles)");
            creatures.WriteTo(text, "ANIMALS (counted: born in captivity, or a tamed elk)");
            otherEntities.WriteTo(text, "OTHER ENTITIES (not counted: wild animals, monsters, players, vehicles)");

            return text.ToString();
        }

        /// <summary>"game:log-placed-oak-ud" is one of many "game:log"; four hundred lines of variants hide that.</summary>
        public static string Family(AssetLocation code)
        {
            if (code == null) return "?";

            string path = code.Path ?? "";
            int dash = path.IndexOf('-');

            return code.Domain + ":" + (dash < 0 ? path : path.Substring(0, dash));
        }
    }
}
