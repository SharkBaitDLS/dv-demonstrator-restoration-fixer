using System;
using System.Collections.Generic;
using System.Linq;
using DRF.Model;
using DRF.Saves;
using DRF.World;
using DV.JObjectExtstensions;
using DV.PointSet;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DRF.Restore;

// Puts cars back into the world from the save they were read out of, with everything the game itself would
// have restored: paint, cargo, brakes, damage, drilled holes and the rest of the per-car state.
//
// Where a car ends up depends on whether the railway is still the one it was parked on. On the same
// railway it goes back exactly where it was. After a track update the old track references mean nothing,
// so it is set down on the nearest track with room for it, near where it used to stand. Cars that were 
// coupled together go back as the consist they were.
internal static class CarInjector
{
    // Matches the game's own logic in Coupler.GetFirstCouplerInRange that runs on save load to reassemble consists
    private const float ReachInset = 0.25f;
    private const float ReachRange = 1.5f;
    private const float ReachClose = 1f;
    private const float ReachAhead = 0.8f;

    // How far along a track a train is slid, each step, looking for room near where it stood.
    private const double SpanStep = 1.0;

    private const double TrackEndClearance = 2.5;
    private const double JunctionClearance = 15.0;

    private delegate CarSpawner.SpawnData UninitializedSpawnData(List<TrainCarLivery> liveries,
        List<bool> orientationReversed, RailTrack track, bool flipTrainConsist);

    private delegate void PopulateSpawnData(ref CarSpawner.SpawnData data, double startSpan,
        double minDistFromEndOfTrack);

    private static readonly Lazy<Action<JObject>?> RestoreConnections = new(() =>
        AccessTools.Method(typeof(CarsSaveManager), "RestoreCarConnections") is { } method
            ? AccessTools.MethodDelegate<Action<JObject>>(method)
            : null);

    private static readonly Lazy<Action<CarSpawner, TrainCar>?> FireCarSpawned = new(() =>
        AccessTools.Method(typeof(CarSpawner), "FireCarSpawned") is { } method
            ? AccessTools.MethodDelegate<Action<CarSpawner, TrainCar>>(method)
            : null);

    private static readonly Lazy<UninitializedSpawnData?> Uninitialized = new(() =>
        AccessTools.Method(typeof(CarSpawner), "GetUninitializedSpawnData") is { } method
            ? AccessTools.MethodDelegate<UninitializedSpawnData>(method)
            : null);

    private static readonly Lazy<PopulateSpawnData?> Populate = new(() =>
        AccessTools.Method(typeof(CarSpawner), "PopulateSpawnData") is { } method
            ? AccessTools.MethodDelegate<PopulateSpawnData>(method)
            : null);

    private static readonly Lazy<Func<Junction.Branch, bool>?> IsJunctionOutBranch = new(() =>
        AccessTools.Method(typeof(CarSpawner), "IsConnectedToJunctionOutBranch") is { } method
            ? AccessTools.MethodDelegate<Func<Junction.Branch, bool>>(method)
            : null);

    // Every car of one restore, across every demonstrator in it. In the event two different demonstrators
    // were coupled together in the prior save data, they should be restored as the same consist.
    internal sealed class Batch
    {
        internal Batch(string? sourceTracksHash, IEnumerable<CarRecord> records)
        {
            SourceTracksHash = sourceTracksHash;
            Records = [.. records.GroupBy(r => r.Guid, StringComparer.Ordinal).Select(g => g.First())];
        }

        internal string? SourceTracksHash { get; }

        internal List<CarRecord> Records { get; }

        // Those of them standing in the world, whether the restore put them there or they never left.
        internal Dictionary<string, TrainCar> Present { get; } = new Dictionary<string, TrainCar>(StringComparer.Ordinal);

        // The cars the restore put down, in order, and whether each had to be moved off its old spot.
        internal List<(CarRecord Record, bool Relocated)> Placed { get; } = [];

        // The ends of moved cars that were set down coupled to the car they stood coupled to, as (GUID,
        // front end). Only these ends of a moved car keep their couplings.
        internal HashSet<(string Guid, bool Front)> JoinedEnds { get; } = [];
    }

    // A car to be put back, with what is needed to place it.
    private sealed class Car(CarRecord record, GameObject prefab, TrainCarLivery livery)
    {
        internal CarRecord Record { get; } = record;
        internal GameObject Prefab { get; } = prefab;
        internal TrainCarLivery Livery { get; } = livery;

        internal Quaternion Rotation { get; } = CarInjector.Rotation(record);
        internal Vector3 Forward => Rotation * Vector3.forward;

        // Where the middle of the car stood. A car's own origin isn't necessarily its middle.
        internal Vector3 Centre => Record.Position + Rotation * Bounds.center;
        internal Bounds Bounds { get; } = prefab.GetComponent<TrainCar>().Bounds;

        private readonly (Vector3 Point, Vector3 Facing)? _front = Reach(prefab, front: true);
        private readonly (Vector3 Point, Vector3 Facing)? _rear = Reach(prefab, front: false);

        // Where the game measured this end's coupler reach from as the car stood, and which way that coupler
        // faced, or nothing for a car without the coupler anchors every car the game can couple has.
        internal (Vector3 Point, Vector3 Facing)? Coupler(bool front) =>
            (front ? _front : _rear) is var (point, facing)
                ? (Record.Position + Rotation * point, Rotation * facing)
                : null;

        // Whether the save has this end's chain hooked onto something.
        internal bool Hooked(bool front) =>
            Record.Data.GetInt(front ? SaveKeys.CouplerStateFront : SaveKeys.CouplerStateRear) is { } state
            && Enum.GetName(typeof(ChainCouplerInteraction.State), state) is { } name
            && name.StartsWith(nameof(ChainCouplerInteraction.State.Attached), StringComparison.Ordinal);
    }

    internal static void Spawn(Batch batch, RestoreOutcome outcome)
    {
        var registry = SingletonBehaviour<TrainCarRegistry>.Instance;
        var toPlace = new List<CarRecord>();
        foreach (var record in batch.Records)
        {
            var existing = registry != null ? registry.GetTrainCarByCarGuid(record.Guid) : null;
            if (existing == null)
            {
                toPlace.Add(record);
                continue;
            }

            batch.Present[record.Guid] = existing;
            outcome.CarsAlreadyPresent++;
        }

        // Make sure their numbers are no longer reserved by the time the game registers them.
        foreach (var record in toPlace) ForgetEarlierUnique(record);

        var relocating = new List<Car>();
        foreach (var record in toPlace)
        {
            var prefab = TrainCar.GetCarPrefab((TrainCarType)record.Type);
            var livery = ((TrainCarType)record.Type).ToV2();
            if (prefab == null || livery == null)
            {
                outcome.Warn($"Car {record.Id} can't come back: nothing installed provides car type "
                    + $"{record.Type}.");
                outcome.CarsFailed++;
                continue;
            }

            if (record.Derailed || TryResolveTracks(record, batch.SourceTracksHash, out _, out _, out _, out _))
            {
                Guarded(record, outcome, () =>
                {
                    Placed(batch, record, SpawnInPlace(prefab, record, batch.SourceTracksHash), false, outcome);
                    outcome.Note($"Put {record.Id} back exactly where it stood.");
                });
                continue;
            }

            relocating.Add(new Car(record, prefab, livery));
        }

        foreach (var train in Trains(relocating)) Guarded(train[0].Record, outcome, () => PlaceTrain(batch, train, outcome));
    }

    internal static void Connect(Batch batch)
    {
        var restore = RestoreConnections.Value;
        if (restore == null) return;

        Physics.SyncTransforms();
        foreach (var (record, relocated) in batch.Placed)
        {
            var data = record.Data;
            if (relocated)
            {
                data = (JObject)data.DeepClone();
                if (!batch.JoinedEnds.Contains((record.Guid, true)))
                    Loosen(data, SaveKeys.CouplerStateFront, SaveKeys.AirHoseFront, SaveKeys.AirCockFront);
                if (!batch.JoinedEnds.Contains((record.Guid, false)))
                    Loosen(data, SaveKeys.CouplerStateRear, SaveKeys.AirHoseRear, SaveKeys.AirCockRear);
            }

            try
            {
                restore(data);
            }
            catch (Exception ex)
            {
                Main.Logger.LogException($"Failed to couple car {record.Id} back up:", ex);
            }
        }

        foreach (var (record, _) in batch.Placed)
        {
            if (!batch.Present.TryGetValue(record.Guid, out var car) || car == null) continue;
            CloseIfOrphaned(car, car.frontCoupler, record.Data.GetBool(SaveKeys.AirHoseFront));
            CloseIfOrphaned(car, car.rearCoupler, record.Data.GetBool(SaveKeys.AirHoseRear));
        }
    }

    // If this car came from a state where it was coupled, but its consist no longer exists,
    // don't leave the air hoses open and leaking.
    private static void CloseIfOrphaned(TrainCar car, Coupler coupler, bool? wasConnected)
    {
        if (wasConnected != true || coupler.hoseAndCock.IsHoseConnected || !coupler.IsCockOpen) return;

        coupler.IsCockOpen = false;
        Main.Logger.Log($"Closed the {(coupler.isFrontCoupler ? "front" : "rear")} air cock of {car.ID}: its "
            + "hose was connected to a car that isn't there any more.");
    }

    private static void Loosen(JObject data, string coupler, string hose, string cock)
    {
        data.SetInt(coupler, (int)ChainCouplerInteraction.State.Parked);
        data.SetBool(hose, false);
        data.SetBool(cock, false);
    }

    private static void ForgetEarlierUnique(CarRecord record)
    {
        var livery = ((TrainCarType)record.Type).ToV2();
        if (Unique(record) && CarLifecycle.ForgetDeletedUnique(livery) is string released)
        {
            Main.Logger.Log($"Dropped the stashed state of an earlier {livery!.id} [{released}] and its number "
                + $"reservation, so {record.Id} comes back as the only one of its kind.");
        }
    }

    private static void Guarded(CarRecord record, RestoreOutcome outcome, Action place)
    {
        try
        {
            place();
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to put car {record.Id} back:", ex);
            outcome.CarsFailed++;
        }
    }

    private static void Placed(Batch batch, CarRecord record, TrainCar car, bool relocated, RestoreOutcome outcome)
    {
        Physics.SyncTransforms();

        RestoreState(car, record);
        batch.Present[record.Guid] = car;
        batch.Placed.Add((record, relocated));
        outcome.CarsReturned++;
    }

    private static TrainCar SpawnInPlace(GameObject prefab, CarRecord record, string? sourceTracksHash)
    {
        var spawner = SingletonBehaviour<CarSpawner>.Instance;
        var position = record.Position + WorldMover.currentMove;

        // A derailed car was never on a track to begin with, so its own position is reasonably as good
        // after a track update as it was before.
        if (record.Derailed)
        {
            return spawner.SpawnLoadedCar(prefab, record.Id, record.Guid, PlayerSpawned(record),
                Unique(record), position, Rotation(record),
                bogie1Derailed: true, null, 0.0, bogie2Derailed: true, null, 0.0);
        }

        TryResolveTracks(record, sourceTracksHash, out var track1, out var span1, out var track2, out var span2);
        return spawner.SpawnLoadedCar(prefab, record.Id, record.Guid, PlayerSpawned(record),
            Unique(record), position, Rotation(record),
            bogie1Derailed: false, track1, span1, bogie2Derailed: false, track2, span2);
    }

    // The cars being moved, gathered into the trains they stood coupled in. Each train is in order from one
    // end to the other.
    private static List<List<Car>> Trains(List<Car> cars)
    {
        var neighbours = cars.ToDictionary(c => c, _ => new List<Car>());
        for (var i = 0; i < cars.Count; i++)
            for (var j = i + 1; j < cars.Count; j++)
            {
                if (!Coupled(cars[i], cars[j])) continue;
                neighbours[cars[i]].Add(cars[j]);
                neighbours[cars[j]].Add(cars[i]);
            }

        var trains = new List<List<Car>>();
        var seen = new HashSet<Car>();
        foreach (var car in cars)
        {
            if (seen.Contains(car)) continue;

            var train = new List<Car>();
            var queue = new Queue<Car>();
            queue.Enqueue(car);
            seen.Add(car);
            while (queue.Count > 0)
            {
                var next = queue.Dequeue();
                train.Add(next);
                foreach (var other in neighbours[next].Where(seen.Add)) queue.Enqueue(other);
            }

            trains.Add(InOrder(train));
            if (train.Count > 1)
                Main.Logger.Log($"{string.Join(", ", trains[trains.Count - 1].Select(c => c.Record.Id))} stood coupled "
                    + "together, so they go back as one train.");
        }
        return trains;
    }

    private static bool Coupled(Car a, Car b) =>
        new[] { true, false }.Any(endA => new[] { true, false }.Any(endB =>
            (a.Hooked(endA) && InReach(a, endA, b, endB)) || (b.Hooked(endB) && InReach(b, endB, a, endA))));

    private static bool InReach(Car from, bool fromFront, Car to, bool toFront)
    {
        if (from.Coupler(fromFront) is not var (fromPoint, facing) || to.Coupler(toFront) is not var (toPoint, _))
            return false;

        var offset = toPoint - fromPoint;
        return offset.sqrMagnitude < ReachRange * ReachRange
            && (offset.sqrMagnitude < ReachClose * ReachClose
                || Vector3.Dot(offset.normalized, facing) > ReachAhead);
    }

    private static (Vector3 Point, Vector3 Facing)? Reach(GameObject prefab, bool front)
    {
        var car = prefab.GetComponent<TrainCar>();
        var anchor = front ? car.FrontCouplerAnchor : car.RearCouplerAnchor;
        if (anchor == null) return null;

        var facing = anchor.localRotation * Vector3.forward;
        return (anchor.localPosition - facing * ReachInset, facing);
    }

    private static List<Car> InOrder(List<Car> train)
    {
        if (train.Count < 2) return train;

        var first = train.OrderByDescending(c => (c.Centre - train[0].Centre).sqrMagnitude).First();
        var last = train.OrderByDescending(c => (c.Centre - first.Centre).sqrMagnitude).First();
        var axis = last.Centre - first.Centre;
        return [.. train.OrderBy(c => Vector3.Dot(c.Centre - first.Centre, axis))];
    }

    // A train is set down whole on the nearest track that has room for it, as close as it can get to where
    // it stood. If no track near enough has room for all of it, each car is tried on its own instead.
    private static void PlaceTrain(Batch batch, List<Car> train, RestoreOutcome outcome)
    {
        if (TryLayOut(train, out var layout, out var order))
        {
            SpawnTrain(batch, layout, order, outcome);
            outcome.Note(train.Count == 1
                ? $"Put {train[0].Record.Id} back near where it stood, on the nearest track with room for it."
                : $"Put {string.Join(", ", order.Select(c => c.Record.Id))} back near where they stood, as the "
                    + "train they were, on the nearest track with room for all of them.");
            return;
        }

        if (train.Count > 1)
        {
            outcome.Warn($"No track near where {string.Join(", ", train.Select(c => c.Record.Id))} stood has "
                + "room for them together, so each goes back on its own and they will need coupling up again.");
            foreach (var car in train) Guarded(car.Record, outcome, () => PlaceTrain(batch, [car], outcome));
            return;
        }

        outcome.Warn($"Car {train[0].Record.Id} can't come back: no room for it near where it was.");
        outcome.CarsFailed++;
    }

    // The game's own train layout, tried on each track near where the train stood, closest first, and along
    // each from the spot nearest where it stood outwards, until the whole train fits.
    private static bool TryLayOut(List<Car> train, out CarSpawner.SpawnData layout, out List<Car> order)
    {
        layout = default;
        order = train;

        var uninitialized = Uninitialized.Value;
        var populate = Populate.Value;
        var junction = IsJunctionOutBranch.Value;
        if (uninitialized == null || populate == null || junction == null) return false;

        var range = Main.Settings.SearchRangeMeters;
        var centre = train.Aggregate(Vector3.zero, (sum, c) => sum + c.Centre) / train.Count
            + WorldMover.currentMove;

        var candidates = new List<(RailTrack Track, EquiPointSet.Point Point, float Distance)>();
        foreach (var track in SingletonBehaviour<RailTrackRegistryBase>.Instance.AllTracks)
        {
            if (RailTrack.GetPointWithinRangeWithYOffset(track, centre, range) is not { } point) continue;
            candidates.Add((track, point, Vector3.Distance((Vector3)point.position + WorldMover.currentMove,
                centre)));
        }

        foreach (var (track, point, distance) in candidates.OrderBy(c => c.Distance))
        {
            var along = point.forward;
            order = Vector3.Dot(train[train.Count - 1].Centre - train[0].Centre, along) > 0f
                ? Enumerable.Reverse(train).ToList()
                : train;

            layout = uninitialized(order.Select(c => c.Livery).ToList(),
                order.Select(c => Vector3.Dot(c.Forward, along) < 0f).ToList(), track, false);
            if (layout.result != CarSpawner.SpawnDataResult.Uninitialized) continue;

            var span = track.GetKinkedPointSet().span;
            var startClearance = junction(track.inBranch) ? JunctionClearance : TrackEndClearance;
            var endClearance = junction(track.outBranch) ? JunctionClearance : TrackEndClearance;
            var lastStart = span - endClearance - layout.trainLength;
            var preferred = point.span - layout.trainLength / 2.0;
            var reach = range - distance;

            for (var offset = 0.0; offset <= reach; offset += SpanStep)
            {
                foreach (var start in offset == 0.0 ? new[] { preferred } : new[] { preferred + offset, preferred - offset })
                {
                    if (start < startClearance || start > lastStart) continue;

                    populate(ref layout, start, endClearance);
                    if (layout.result == CarSpawner.SpawnDataResult.OK) return true;
                }

                if (preferred + offset > lastStart && preferred - offset < startClearance) break;
            }
        }
        return false;
    }

    // Each car set down where the layout put it, the way the game's own train spawning does, except with
    // the car's existing number and identity rather than a new one.
    private static void SpawnTrain(Batch batch, CarSpawner.SpawnData layout, List<Car> order, RestoreOutcome outcome)
    {
        var spawner = SingletonBehaviour<CarSpawner>.Instance;
        var fire = FireCarSpawned.Value ?? throw new MissingMethodException(nameof(CarSpawner), "FireCarSpawned");

        for (var i = 0; i < order.Count; i++)
        {
            var record = order[i].Record;
            var slot = layout.carData[i];

            var car = UnityEngine.Object.Instantiate(slot.prefab, slot.position, Quaternion.LookRotation(slot.forward))
                .GetComponentInChildren<TrainCar>();
            car.playerSpawnedCar = PlayerSpawned(record);
            car.uniqueCar = Unique(record);
            car.InitializeExistingLogicCar(record.Id, record.Guid);
            car.SetTrack(layout.track, slot.position, slot.forward);
            car.TryAddFastTravelDestination();
            fire(spawner, car);
            Placed(batch, record, car, true, outcome);

            if (i + 1 < order.Count)
            {
                batch.JoinedEnds.Add((record.Guid, slot.orientationReversed));
                batch.JoinedEnds.Add((order[i + 1].Record.Guid, !layout.carData[i + 1].orientationReversed));
            }
        }
    }

    private static bool TryResolveTracks(CarRecord record, string? sourceTracksHash, out RailTrack? track1,
        out double span1, out RailTrack? track2, out double span2)
    {
        track1 = track2 = null;
        span1 = span2 = 0.0;

        var registry = SingletonBehaviour<RailTrackRegistryBase>.Instance;
        if (registry == null || sourceTracksHash != registry.TracksHash) return false;

        var index1 = record.Data.GetInt(SaveKeys.Bogie1TrackIndex);
        var index2 = record.Data.GetInt(SaveKeys.Bogie2TrackIndex);
        var position1 = record.Data.GetDouble(SaveKeys.Bogie1Span);
        var position2 = record.Data.GetDouble(SaveKeys.Bogie2Span);
        if (!index1.HasValue || !index2.HasValue || !position1.HasValue || !position2.HasValue) return false;

        var tracks = registry.OrderedRailtracks;
        if (index1.Value < 0 || index1.Value >= tracks.Length) return false;
        if (index2.Value < 0 || index2.Value >= tracks.Length) return false;

        track1 = tracks[index1.Value];
        track2 = tracks[index2.Value];
        span1 = position1.Value;
        span2 = position2.Value;
        return true;
    }

    private static void RestoreState(TrainCar car, CarRecord record)
    {
        var data = record.Data;
        var cargo = data.GetInt(SaveKeys.LoadedCargo) ?? (int)CargoType.None;

        CarsSaveManager.RestoreCarState(car,
            Enum.IsDefined(typeof(CargoType), cargo) ? (CargoType)cargo : CargoType.None,
            (byte?)data.GetInt(SaveKeys.LoadedCargoModel),
            data.GetBool(SaveKeys.Exploded) ?? false,
            record.PaintExterior,
            record.PaintInterior,
            data.GetFloat(SaveKeys.Handbrake) ?? -1f,
            data.GetFloat(SaveKeys.BrakePipe) ?? -1f,
            data.GetFloat(SaveKeys.AuxReservoir) ?? -1f,
            data.GetFloat(SaveKeys.MainReservoir) ?? -1f,
            data.GetFloat(SaveKeys.ControlReservoir) ?? -1f,
            data.GetFloat(SaveKeys.BrakeCylinder) ?? -1f,
            car.visitChecker == null ? -1f : data.GetFloat(SaveKeys.VisitChecker) ?? -1f,
            data.GetJObject(SaveKeys.CarState),
            data.GetJObject(SaveKeys.SimCarState),
            data.GetJObject(SaveKeys.ModCarState));
    }

    private static bool PlayerSpawned(CarRecord record) =>
        record.Data.GetBool(SaveKeys.PlayerSpawned) ?? false;

    private static bool Unique(CarRecord record) => record.Data.GetBool(SaveKeys.Unique) ?? false;

    private static Quaternion Rotation(CarRecord record) =>
        Quaternion.Euler(record.Data.GetVector3(SaveKeys.Rotation) ?? Vector3.zero);
}
