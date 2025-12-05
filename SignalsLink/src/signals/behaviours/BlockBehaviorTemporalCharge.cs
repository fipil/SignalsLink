using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.behaviours
{
    public class BlockBehaviorTemporalCharge : BlockBehavior
    {
        private const float TICKS_PER_DAY = 24000f;
        private const float REFERENCE_DAYS = 100f;

        public float BaseConsumptionFactor { get; private set; } = 1.0f;
        public float ReferenceVolume { get; private set; } = 100f;
        public float GearTotalCharge { get; private set; }
        public int MaxChargeMultiplier { get; private set; } = 5;
        public string ChargeItemCode { get; private set; } = "gear-temporal";

        public BlockBehaviorTemporalCharge(Block block) : base(block)
        {
            GearTotalCharge = REFERENCE_DAYS * TICKS_PER_DAY;
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);

            BaseConsumptionFactor = properties["baseConsumptionFactor"].AsFloat(1.0f);
            ReferenceVolume = properties["referenceVolume"].AsFloat(100f);
            MaxChargeMultiplier = properties["maxChargeMultiplier"].AsInt(5);
            ChargeItemCode = properties["chargeItemCode"].AsString("gear-temporal");

            float referenceDays = properties["referenceDays"].AsFloat(REFERENCE_DAYS);
            GearTotalCharge = referenceDays * TICKS_PER_DAY;
        }

        public float GetMaxCharge()
        {
            return GearTotalCharge * MaxChargeMultiplier;
        }

        public float CalculateConsumptionPerTick(float operationalVolume)
        {
            float volumeRatio = operationalVolume / ReferenceVolume;
            return volumeRatio * BaseConsumptionFactor / REFERENCE_DAYS / TICKS_PER_DAY;
        }

        public bool TryCharge(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.Side != EnumAppSide.Server)
                return false;

            ItemSlot hotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

            if (hotbarSlot?.Itemstack == null || !hotbarSlot.Itemstack.Collectible.Code.Path.Contains(ChargeItemCode))
                return false;

            if (!(world.BlockAccessor.GetBlockEntity(blockSel.Position) is ITemporalChargeHolder holder))
                return false;

            float currentCharge = holder.GetCurrentCharge();
            float maxCharge = GetMaxCharge();

            // Přetypování na IServerPlayer
            IServerPlayer serverPlayer = byPlayer as IServerPlayer;

            if (currentCharge >= maxCharge)
            {
                if (serverPlayer != null)
                {
                    (world.Api as ICoreServerAPI)?.SendIngameError(serverPlayer, "fullycharged",
                        "This device is already fully charged");
                }
                return true;
            }

            float newCharge = Math.Min(currentCharge + GearTotalCharge, maxCharge);
            holder.SetCurrentCharge(newCharge);

            hotbarSlot.TakeOut(1);
            hotbarSlot.MarkDirty();

            world.PlaySoundAt(new AssetLocation("sounds/effect/teleport"),
                blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z, byPlayer);

            if (serverPlayer != null)
            {
                float daysAdded = GearTotalCharge / TICKS_PER_DAY / BaseConsumptionFactor;
                float totalDays = newCharge / TICKS_PER_DAY / BaseConsumptionFactor;

                (world.Api as ICoreServerAPI)?.SendIngameDiscovery(serverPlayer, "charged",
                    $"Charged! Added {daysAdded:F0} days. Total: {totalDays:F0} days (at reference volume)");
            }

            return true;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            if (inSlot.Itemstack?.Attributes != null)
            {
                float charge = inSlot.Itemstack.Attributes.GetFloat("storedCharge", 0f);
                float maxCharge = GetMaxCharge();

                if (charge > 0)
                {
                    float daysRemaining = (charge / TICKS_PER_DAY) / BaseConsumptionFactor;
                    float maxDays = (maxCharge / TICKS_PER_DAY) / BaseConsumptionFactor;
                    float chargePercent = (charge / maxCharge) * 100f;

                    dsc.AppendLine($"Charge: {daysRemaining:F1}/{maxDays:F0} days ({chargePercent:F0}%)");
                    dsc.AppendLine($"  (at reference volume)");
                }
                else
                {
                    dsc.AppendLine("Charge: Empty");
                }
            }
            else
            {
                dsc.AppendLine("Charge: Empty");
            }

            dsc.AppendLine($"Max capacity: {MaxChargeMultiplier} temporal gears");
        }

        public void AppendPlacedBlockInfo(IWorldAccessor world, BlockPos pos, StringBuilder dsc)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is ITemporalChargeHolder holder)
            {
                float currentCharge = holder.GetCurrentCharge();
                float maxCharge = GetMaxCharge();

                if (currentCharge > 0)
                {
                    float daysRemaining = currentCharge / (24000f * BaseConsumptionFactor);
                    float maxDays = maxCharge / (24000f * BaseConsumptionFactor);
                    float chargePercent = (currentCharge / maxCharge) * 100f;

                    dsc.AppendLine($"Charge: {daysRemaining:F1}/{maxDays:F0} days ({chargePercent:F0}%)");

                    // Volitelně: info o spotřebě podle aktuálního volume
                    float operationalVolume = holder.GetOperationalVolume();
                    float actualConsumptionPerTick = CalculateConsumptionPerTick(operationalVolume);
                    float actualDaysRemaining = currentCharge / (actualConsumptionPerTick * 24000f);

                    if (Math.Abs(operationalVolume - ReferenceVolume) > 0.1f)
                    {
                        dsc.AppendLine($"At current volume ({operationalVolume:F0} blocks): ~{actualDaysRemaining:F1} days");
                    }
                }
                else
                {
                    dsc.AppendLine("Charge: Empty - needs temporal gear");
                }
            }
        }

    }
}
