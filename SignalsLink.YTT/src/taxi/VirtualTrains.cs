using System;
using System.Collections;
using System.Collections.Generic;
using SignalsLink.YTT.src.probe;
using Vintagestory.API.Common;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>
    /// The trains the other mod drives off-screen, read as candidates for summoning. Reads only,
    /// through the members the surface probe checked; one failure stands it down for the session
    /// and the ticket summons loaded trains only.
    /// </summary>
    public class VirtualTrains
    {
        private readonly YttSurface surface;
        private readonly ILogger logger;
        private bool failed;

        public VirtualTrains(YttSurface surface, ILogger logger)
        {
            this.surface = surface;
            this.logger = logger;
        }

        public bool Available => !failed && surface?.CanWatchApproach == true && surface.CanReadVirtualTrains;

        /// <summary>Every virtual convoy as a candidate, in the given dimension. Never cached.</summary>
        public List<TrainCandidate> Candidates(int dimension)
        {
            List<TrainCandidate> trains = new List<TrainCandidate>();
            if (!Available) return trains;

            try
            {
                if (surface.VirtualConvoysField.GetValue(surface.OffscreenSystem) is not IDictionary convoys) return trains;

                foreach (object convoy in convoys.Values)
                {
                    TrainCandidate train = Read(convoy, dimension);
                    if (train != null) trains.Add(train);
                }
            }
            catch (Exception e)
            {
                failed = true;
                trains.Clear();
                logger?.Error("[SignalsLink.YTT] could not read the off-screen trains: " + e
                    + " The ticket summons loaded trains only for the rest of this session.");
            }

            return trains;
        }

        private TrainCandidate Read(object convoy, int dimension)
        {
            if (surface.ConvoyOrphanedField.GetValue(convoy) is true) return null;
            if (surface.ConvoyVehiclesField.GetValue(convoy) is not IList vehicles || vehicles.Count == 0) return null;
            if (surface.ConvoyPosesField.GetValue(convoy) is not IList poses || poses.Count == 0) return null;

            bool passenger = false, conductor = false, engine = false;
            double fuel = double.MaxValue, water = double.MaxValue, speed = 0;
            byte gauge = 0;

            foreach (object vehicle in vehicles)
            {
                string code = surface.SnapshotCodeField.GetValue(vehicle) as string ?? "";
                if (SummonRules.IsPassengerCar(code)) passenger = true;
                if (surface.SnapshotConductorField.GetValue(vehicle) is true) conductor = true;
                gauge = (byte)surface.SnapshotGaugeField.GetValue(vehicle);

                if (surface.SnapshotEngineField.GetValue(vehicle) is true)
                {
                    engine = true;
                    fuel = Math.Min(fuel, (double)surface.SnapshotFuelSecondsField.GetValue(vehicle));
                    water = Math.Min(water, (double)surface.SnapshotWaterSecondsField.GetValue(vehicle));
                    speed = Math.Max(speed, (double)surface.SnapshotSpeedField.GetValue(vehicle));
                }
            }

            List<(double, double)> positions = new List<(double, double)>(poses.Count);
            foreach (object pose in poses)
            {
                positions.Add(((double)surface.PoseXField.GetValue(pose), (double)surface.PoseZField.GetValue(pose)));
            }

            int lead = (int)surface.ConvoyLeadIndexField.GetValue(convoy);
            if (lead < 0 || lead >= positions.Count) lead = 0;

            return new TrainCandidate
            {
                Head = (long)surface.ConvoyHeadKeyField.GetValue(convoy),
                Virtual = true,
                X = positions[lead].Item1,
                Z = positions[lead].Item2,
                // The simulation keeps no dimension of its own; its trains are where the station is.
                Dimension = dimension,
                Gauge = gauge,
                HasPassengerCar = passenger,
                HasConductor = conductor,
                HasEngine = engine,
                HasPlayers = false,
                HasFuelAndWater = engine && fuel > 0 && water > 0,
                OnTaxiRide = OnTaxiRide(convoy),
                SpeedBps = speed,
                Columns = new List<(int, int)>(SummonRules.ColumnsAround(positions)),
            };
        }

        /// <summary>A taxi timetable is one stop held for a day; nobody writes that by hand. The way back holds a second less and counts as free.</summary>
        private bool OnTaxiRide(object convoy)
        {
            object automation = surface.ConvoyAutomationField.GetValue(convoy);
            if (automation == null) return false;
            if (surface.AutomationRouteField.GetValue(automation) is not Array route || route.Length != 1) return false;

            object stop = route.GetValue(0);
            return stop != null && (int)surface.RouteHoldSecondsField.GetValue(stop) == Timetable.HoldSeconds;
        }
    }
}
