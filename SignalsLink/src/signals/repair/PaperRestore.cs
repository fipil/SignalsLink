using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.repair
{
    /// <summary>
    /// Writes papers back onto devices from a file, after <see cref="DeviceRepair"/> has given
    /// them their block entities again.
    ///
    /// The papers themselves live inside the block entity, so they go with it and no repair can
    /// bring them back - but they can be read out of an older save and put back here. Deliberately
    /// through the game rather than by editing the save file: Vintage Story does the writing, so a
    /// mistake costs a wrong paper on one device rather than a broken chunk.
    ///
    /// It refuses to overwrite a device that already has a paper unless told to. A device that
    /// survived may well be carrying a NEWER paper than the file, and quietly replacing it with an
    /// older one would be the worst outcome of all - it looks like it worked.
    /// </summary>
    public class PaperRestore : ModSystem
    {
        /// <summary>Default name, looked for in the game's data folder.</summary>
        public const string DefaultFile = "signalslink-papers.json";

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.ChatCommands.Create("slpapers")
                .WithDescription("Writes paper conditions back onto devices from a file.")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(
                    api.ChatCommands.Parsers.WordRange("mode", "check", "apply", "force"),
                    api.ChatCommands.Parsers.OptionalWord("file"))
                .HandleWith(args => Run(api, args));
        }

        /// <summary>One device's paper, as the file carries it.</summary>
        public class Entry
        {
            public int x;
            public int y;
            public int z;
            public string block;
            public string conditions;
        }

        public class Document
        {
            public string source;
            public List<Entry> devices = new List<Entry>();
        }

        private static TextCommandResult Run(ICoreServerAPI api, TextCommandCallingArgs args)
        {
            string mode = (string)args[0];
            string name = args.Parsers[1].IsMissing ? DefaultFile : (string)args[1];

            string path = Path.IsPathRooted(name) ? name : Path.Combine(api.DataBasePath, name);

            if (!File.Exists(path)) return TextCommandResult.Error("No such file: " + path);

            Document doc;

            try
            {
                doc = JsonConvert.DeserializeObject<Document>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                return TextCommandResult.Error("Could not read " + path + ": " + e.Message);
            }

            if (doc?.devices == null || doc.devices.Count == 0)
            {
                return TextCommandResult.Error("There are no devices in " + path + ".");
            }

            return TextCommandResult.Success(Apply(api.World, doc, mode));
        }

        private static string Apply(IWorldAccessor world, Document doc, string mode)
        {
            bool write = mode != "check";
            bool overwrite = mode == "force";

            int written = 0, already = 0, wrongBlock = 0, noEntity = 0, notLoaded = 0;
            List<string> notes = new List<string>();

            foreach (Entry entry in doc.devices)
            {
                BlockPos pos = new BlockPos(entry.x, entry.y, entry.z, 0);

                Block block = world.BlockAccessor.GetBlock(pos);

                // An unloaded chunk reads as air through the accessor, which must not be mistaken
                // for "the device is gone". Told apart by asking the chunk itself.
                if (world.BlockAccessor.GetChunkAtBlockPos(pos) == null)
                {
                    notLoaded++;
                    continue;
                }

                string code = block?.Code?.ToString();

                if (code != entry.block)
                {
                    wrongBlock++;
                    Note(notes, entry, "expected " + entry.block + ", found " + (code ?? "nothing"));
                    continue;
                }

                if (world.BlockAccessor.GetBlockEntity(pos) is not IPaperConditionsHost host)
                {
                    noEntity++;
                    Note(notes, entry, "has no block entity yet - run /slrepair first");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(host.ConditionsText) && !overwrite)
                {
                    already++;
                    continue;
                }

                if (write) host.ConditionsText = entry.conditions;

                written++;
            }

            StringBuilder sb = new StringBuilder();

            sb.Append(write ? "Wrote " : "Would write ").Append(written)
              .Append(" of ").Append(doc.devices.Count).Append(" paper(s).");

            if (already > 0) sb.Append("\n  ").Append(already).Append(" left alone - they already have a paper (use 'force' to replace).");
            if (noEntity > 0) sb.Append("\n  ").Append(noEntity).Append(" have no block entity - run /slrepair there first.");
            if (wrongBlock > 0) sb.Append("\n  ").Append(wrongBlock).Append(" sit under a different block than the file expects.");
            if (notLoaded > 0) sb.Append("\n  ").Append(notLoaded).Append(" are in chunks that are not loaded - go there and run it again.");

            foreach (string note in notes) sb.Append("\n  ").Append(note);

            return sb.ToString();
        }

        /// <summary>Only the first few oddities are named; the counts above say how many there were.</summary>
        private static void Note(List<string> notes, Entry entry, string what)
        {
            if (notes.Count >= 10) return;

            notes.Add(entry.x + ", " + entry.y + ", " + entry.z + ": " + what);
        }
    }
}
