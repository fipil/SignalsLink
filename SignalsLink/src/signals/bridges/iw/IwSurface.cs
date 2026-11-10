using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.bridges.iw
{
    /// <summary>
    /// What this bridge needs to find in Immersive Woodworking's chopper, looked up by name so
    /// SignalsLink never references that mod. Logs need only public members; fitting a head
    /// writes a private field, so it is a separate capability that may fall away on its own.
    /// A fingerprint of the members found is logged, and compared with the versions this was
    /// written against (same pattern as the YTT bridge).
    /// </summary>
    public sealed class IwSurface
    {
        public const string ModId = "immersivewoodworking";
        public const string MasterTypeName = "ImmersiveWoodworking.BlockEntityChopper";
        public const string PartTypeName = "ImmersiveWoodworking.BEMultiblockChopper";

        private const BindingFlags Anywhere =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly HashSet<string> KnownGood = new HashSet<string>
        {
            // Immersive Woodworking 1.3.6 and 1.3.14: the same chopper surface.
            "30091c0ebced",
        };

        public bool CanFeedLogs { get; private set; }
        public bool CanFitHead { get; private set; }
        public string Surface { get; private set; } = "";
        public string Complaint { get; private set; } = "";

        public Type MasterType { get; private set; }
        public Type PartType { get; private set; }

        public PropertyInfo Principal { get; private set; }
        public MethodInfo AutoPush { get; private set; }
        public PropertyInfo Inventory { get; private set; }
        public PropertyInfo HeadStack { get; private set; }
        public PropertyInfo HasArm { get; private set; }
        public FieldInfo HeadField { get; private set; }
        public MethodInfo UpdateRenderer { get; private set; }

        public static IwSurface Probe(ICoreAPI api)
        {
            Type master = null, part = null;

            try
            {
                foreach (ModSystem system in api.ModLoader.Systems)
                {
                    Assembly assembly = system?.GetType().Assembly;
                    master = assembly?.GetType(MasterTypeName);
                    if (master == null) continue;
                    part = assembly.GetType(PartTypeName);
                    break;
                }
            }
            catch (Exception e)
            {
                return new IwSurface { Complaint = "looking for the chopper failed: " + e.Message };
            }

            return Probe(master, part);
        }

        public static IwSurface Probe(Type master, Type part)
        {
            IwSurface surface = new IwSurface();

            try
            {
                surface.Look(master, part);
            }
            catch (Exception e)
            {
                surface.CanFeedLogs = false;
                surface.CanFitHead = false;
                surface.Complaint = "the probe itself failed: " + e.Message;
            }

            return surface;
        }

        private void Look(Type master, Type part)
        {
            if (master == null) { Complaint = MasterTypeName + " is not there"; return; }
            if (part == null) { Complaint = PartTypeName + " is not there"; return; }

            MasterType = master;
            PartType = part;
            List<string> members = new List<string>();

            Principal = part.GetProperty("Principal", Anywhere);
            if (Principal == null || !Principal.CanRead || Principal.PropertyType != typeof(BlockPos))
            {
                Complaint = "BEMultiblockChopper.Principal is missing or is no longer a BlockPos";
                return;
            }
            members.Add(Describe("BEMultiblockChopper.Principal", Principal.PropertyType));

            AutoPush = master.GetMethod("GetAutoPushIntoSlot", Anywhere, null,
                new[] { typeof(BlockFacing), typeof(ItemSlot), typeof(BlockPos) }, null);
            if (AutoPush == null || AutoPush.ReturnType != typeof(ItemSlot))
            {
                Complaint = "BlockEntityChopper.GetAutoPushIntoSlot(BlockFacing, ItemSlot, BlockPos) is gone";
                return;
            }
            members.Add("BlockEntityChopper.GetAutoPushIntoSlot:ItemSlot(BlockFacing,ItemSlot,BlockPos)");

            Inventory = master.GetProperty("Inventory", Anywhere);
            if (Inventory == null || !Inventory.CanRead || !typeof(InventoryBase).IsAssignableFrom(Inventory.PropertyType))
            {
                Complaint = "BlockEntityChopper.Inventory is missing or is no longer an inventory";
                return;
            }
            members.Add(Describe("BlockEntityChopper.Inventory", Inventory.PropertyType));

            CanFeedLogs = true;

            LookAtHead(master, members);

            Surface = Fingerprint(members);
        }

        /// <summary>Probed separately: no head is no reason to refuse logs.</summary>
        private void LookAtHead(Type master, List<string> members)
        {
            HeadStack = master.GetProperty("HeadStack", Anywhere);
            HasArm = master.GetProperty("HasArm", Anywhere);
            HeadField = master.GetField("headStack", Anywhere);
            UpdateRenderer = master.GetMethod("UpdateRenderer", Anywhere, null, Type.EmptyTypes, null);

            if (HeadStack == null || !HeadStack.CanRead || HeadStack.PropertyType != typeof(ItemStack)) { HeadStack = null; return; }
            if (HasArm == null || !HasArm.CanRead || HasArm.PropertyType != typeof(bool)) { HasArm = null; return; }
            if (HeadField == null || HeadField.FieldType != typeof(ItemStack) || HeadField.IsInitOnly) { HeadField = null; return; }

            members.Add(Describe("BlockEntityChopper.HeadStack", HeadStack.PropertyType));
            members.Add(Describe("BlockEntityChopper.HasArm", HasArm.PropertyType));
            members.Add(Describe("BlockEntityChopper.headStack", HeadField.FieldType));
            if (UpdateRenderer != null) members.Add(Describe("BlockEntityChopper.UpdateRenderer", UpdateRenderer.ReturnType));

            CanFitHead = true;
        }

        private static string Describe(string name, Type type) => name + ":" + type.Name;

        public static string Fingerprint(params string[] members) => Fingerprint((IEnumerable<string>)members);

        public static string Fingerprint(IEnumerable<string> members)
        {
            List<string> sorted = new List<string>(members);
            sorted.Sort(StringComparer.Ordinal);

            using SHA1 sha = SHA1.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", sorted)));

            return Convert.ToHexString(hash).Substring(0, 12).ToLowerInvariant();
        }

        /// <summary>Says in the log what was found, and how surprising it was.</summary>
        public void Report(ICoreAPI api, string version)
        {
            if (!CanFeedLogs)
            {
                api.Logger.Warning("[SignalsLink.IW] standing down: " + Complaint
                    + ". Chutes and dampers will not feed the chopper until this is sorted out.");
                return;
            }

            if (KnownGood.Contains(Surface))
            {
                api.Logger.Notification("[SignalsLink.IW] chopper surface " + Surface + " as expected.");
            }
            else
            {
                api.Logger.Warning("[SignalsLink.IW] Immersive Woodworking " + version
                    + " has a chopper surface this was not written against (" + Surface + "). It looks usable and"
                    + " will be used, but if the chopper behaves oddly with a chute or damper, this line is the first thing to quote.");
            }

            if (!CanFitHead)
            {
                api.Logger.Notification("[SignalsLink.IW] the chopper's head cannot be reached on this version;"
                    + " logs still go in, a new head does not.");
            }
        }
    }
}
