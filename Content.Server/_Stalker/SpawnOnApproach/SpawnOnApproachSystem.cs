using System.Numerics;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Trigger;
using Robust.Shared.Map;
using Robust.Shared.Player; // ST14-EN: Addition
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server._Stalker_OW.Spawning.Regions; // ST:OW
using Robust.Shared.Maths; // ST:OW
using Content.Shared.Mobs; // ST:OW
using Content.Shared.Doors.Components; // ST:OW

namespace Content.Server._Stalker.SpawnOnApproach;

public sealed class SpawnOnApproachSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly EntityLookupSystem _lookupSystem = default!;

    // ST:OW begin
    private static readonly Vector2i[] CardinalDirections =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };
    // ST:OW end
    
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpawnOnApproachComponent, TriggerEvent>(OnTrigger);
        SubscribeLocalEvent<SpawnOnApproachComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<SpawnOnApproachComponent> entity, ref ComponentInit args)
    {
        if (_timing.CurTime < entity.Comp.MinStartAction)
            return;
        // Check components with instant spawn
        if (!entity.Comp.InstantSpawn)
            return;

        SpawnWithOffset(entity);
    }

    private void OnTrigger(Entity<SpawnOnApproachComponent> entity, ref TriggerEvent args)
    {
        if (!entity.Comp.Enabled)
            return;

        if (_timing.CurTime < entity.Comp.MinStartAction)
            return;

        SpawnWithOffset(entity);
    }

    private void SpawnWithOffset(Entity<SpawnOnApproachComponent> entity)
    {
        var comp = entity.Comp;
        comp.LastRequestedAmount = 0;
        comp.LastSpawnedAmount = 0;

        var chanceIncrease = Math.Max(0f, comp.ChanceIncreaseOnFailure);
        var effectiveChance = Math.Clamp(
            comp.Chance + comp.ConsecutiveFailures * chanceIncrease,
            0f,
            1f);
        comp.LastEffectiveChance = effectiveChance;

        if (!_random.Prob(effectiveChance))
        {
            if (chanceIncrease > 0f && comp.ConsecutiveFailures < int.MaxValue)
                comp.ConsecutiveFailures++;

            comp.LastResult = SpawnOnApproachResult.ChanceFailed;

            if (comp.ShouldTimeoutOnRoll)
                StartCooldown(comp, comp.FailureCooldown ?? comp.Cooldown);

            return;
        }

        var amount = _random.Next(comp.MinAmount, comp.MaxAmount + 1);
        comp.LastRequestedAmount = amount;

        if (amount == 0)
        {
            comp.ConsecutiveFailures = 0;
            comp.LastResult = SpawnOnApproachResult.ZeroAmount;
            StartCooldown(comp, comp.Cooldown);
            return;
        }

        var xform = Transform(entity);
        var (reachableTiles, fallbackCoords) =
            BuildSpawnSearchArea(xform.Coordinates, comp.MaxOffset);

        for (var i = 0; i < amount && reachableTiles.Count > 0; i++)
        {
            if (!TryFindSpawnPosition(
                    entity.Owner,
                    comp,
                    xform.Coordinates,
                    reachableTiles,
                    fallbackCoords,
                    out var spawnCoords))
            {
                continue;
            }

            var proto = _random.Pick(comp.EntProtoIds);
            Spawn(proto, spawnCoords);
            comp.LastSpawnedAmount++;
        }

        if (comp.LastSpawnedAmount == 0)
        {
            // If spawner cannot find a valid spawn despite a success roll,
            // Then use failureCooldown for less CD
            comp.LastResult = SpawnOnApproachResult.NoValidPosition;
            StartCooldown(comp, comp.FailureCooldown ?? comp.Cooldown);
            return;
        }

        // Partial spawns (i.e. 2/4) counts as a success
        comp.ConsecutiveFailures = 0;
        comp.LastResult = comp.LastSpawnedAmount < amount
            ? SpawnOnApproachResult.PartialSpawn
            : SpawnOnApproachResult.Spawned;
        StartCooldown(comp, comp.Cooldown);
    }

    private void StartCooldown(SpawnOnApproachComponent comp, float seconds)
    {
        comp.CoolDownTime = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        comp.Enabled = false;
    }
    // ST:OW end

    private EntityCoordinates RandomizeCoords(SpawnOnApproachComponent comp, EntityCoordinates initial)
    {
        // ST14-EN: commented out
        // var offset = _random.NextFloat(comp.MinOffset, comp.MaxOffset);
        // var xOffset = _random.NextFloat(-offset, offset);
        // var yOffset = _random.NextFloat(-offset, offset);
        // return initial.Offset(new Vector2(xOffset, yOffset));

        // ST14-EN fix: Correct formula for uniform distance in [MinOffset, MaxOffset]
        var lightningDistance = comp.MinOffset + _random.NextFloat() * (comp.MaxOffset - comp.MinOffset);
        return initial.Offset(_random.NextAngle().ToVec() * lightningDistance);
    }

    // ST:OW begin
    private bool TryFindRandomSpawn(EntityUid spawner, 
        SpawnOnApproachComponent comp, 
        EntityCoordinates origin, 
        HashSet<Vector2i> reachableTiles, 
        out EntityCoordinates result)
    {
        for (var attempt = 0;
             attempt < comp.MaxSpawnAttempts;
             attempt++)
        {
            var candidate =
                RandomizeCoords(comp, origin);

            var tile =
                _turf.GetTileRef(candidate);

            if (tile == null ||
                !reachableTiles.Contains(
                    tile.Value.GridIndices))
            {
                continue;
            }

            if (!IsValidSpawnPosition(
                    spawner,
                    candidate,
                    comp))
            {
                continue;
            }

            result = candidate;
            return true;
        }

        result = default;
        return false;
    }
    // ST:OW end

    // ST14-EN: Addition
    private bool CheckPlayerNearby(in EntityCoordinates coords, SpawnOnApproachComponent comp)
    {
        if (comp.SpawnNearPlayers)
            return false;
        // ST:OW begin
        var minPlayerDistance = comp.MinPlayerDistance > 0f
            ? comp.MinPlayerDistance
            : comp.MinOffset * 0.75f;
        if (minPlayerDistance <= 0f)
            return false;
        var actorQuery = GetEntityQuery<ActorComponent>();
        foreach (var uid in _lookupSystem.GetEntitiesInRange(
                     coords,
                     minPlayerDistance,
                     flags: LookupFlags.Approximate | LookupFlags.Dynamic))
        // ST:OW end    
        {
            if (actorQuery.HasComponent(uid))
                return true;
        }

        return false;
    }
    
    // ST:OW begin
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var query =
            EntityQueryEnumerator<SpawnOnApproachComponent>();

        while (query.MoveNext(out _, out var spawner))
        {
            if (spawner.Enabled)
                continue;

            if (spawner.CoolDownTime > now)
                continue;

            spawner.CoolDownTime = null;
            spawner.Enabled = true;
        }
    }
    
    // Use regular system to find spawn spot
    // If that fails then look for nearby floor tiles
    private bool TryFindSpawnPosition(
        EntityUid spawner,
        SpawnOnApproachComponent comp,
        EntityCoordinates origin,
        HashSet<Vector2i> reachableTiles,
        List<EntityCoordinates> fallbackCoords,
        out EntityCoordinates result)
    {
        if (TryFindRandomSpawn(
                spawner,
                comp,
                origin,
                reachableTiles,
                out result))
        {
            return true;
        }

        return TryFindConnectedFallback(
            spawner,
            comp,
            fallbackCoords,
            out result);
    }
    
    // Uses a breadth-first search flood-fill system :D
    // Calculates the valid area around the spawner and returns a set of reachable grids & fallback coordinates
    private (HashSet<Vector2i> ReachableTiles, List<EntityCoordinates> FallbackCoords) BuildSpawnSearchArea(
        EntityCoordinates origin, 
        float maxOffset)
    {
        var reachableTiles = new HashSet<Vector2i>();
        var fallbackCoords = new List<EntityCoordinates>();

        var visited = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();

        var start = Vector2i.Zero;

        visited.Add(start);
        queue.Enqueue(start);

        var fallbackMaxSq = maxOffset * maxOffset;
        var searchOffset = maxOffset + 1f;
        var searchMaxSq = searchOffset * searchOffset;

        while (queue.Count > 0)
        {
            var offset = queue.Dequeue();
            var distanceSq = offset.X * offset.X + offset.Y * offset.Y;

            if (distanceSq > searchMaxSq)
                continue;

            var coords = origin.Offset(new Vector2(offset.X, offset.Y));

            if (!TryGetTraversableSpawnTile(coords, out var gridIndices))
                continue;

            reachableTiles.Add(gridIndices);

            if (distanceSq <= fallbackMaxSq)
                fallbackCoords.Add(coords);

            foreach (var direction in CardinalDirections)
            {
                var next = offset + direction;

                if (!visited.Add(next))
                    continue;

                var nextDistanceSq = next.X * next.X + next.Y * next.Y;

                if (nextDistanceSq > searchMaxSq)
                    continue;

                queue.Enqueue(next);
            }
        }

        return (reachableTiles, fallbackCoords);
    }
    
    // Selects a random position from the previously calculated area
    private bool TryFindConnectedFallback(
        EntityUid spawner,
        SpawnOnApproachComponent comp,
        List<EntityCoordinates> fallbackCoords,
        out EntityCoordinates result)
    {
        for (var i = 0; i < fallbackCoords.Count; i++)
        {
            var selected = _random.Next(i, fallbackCoords.Count);

            var candidate = fallbackCoords[selected];
            fallbackCoords[selected] = fallbackCoords[i];
            fallbackCoords[i] = candidate;

            if (!IsValidSpawnPosition(spawner, candidate, comp))
                continue;

            result = candidate;
            return true;
        }

        result = default;
        return false;
    }
    
    // Determine if the tile is "traversable"
    // AKA not a wall, space, etc.
    private bool TryGetTraversableSpawnTile(
        EntityCoordinates coords,
        out Vector2i gridIndices)
    {
        gridIndices = default;

        var tile = _turf.GetTileRef(coords);

        if (tile == null || tile.Value.Tile.IsEmpty)
            return false;

        var boundaryQuery = GetEntityQuery<STSpawnBoundaryComponent>();
        var doorQuery = GetEntityQuery<DoorComponent>();

        foreach (var uid in _lookupSystem.GetLocalEntitiesIntersecting(tile.Value, 0f))
        {
            if (boundaryQuery.HasComponent(uid) || doorQuery.HasComponent(uid))
                return false;
        }

        if (_turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask))
            return false;

        gridIndices = tile.Value.GridIndices;
        return true;
    }
    
    
    // Checks if an area is a legal spawn spot
    // Considers tiles, collisions, and other entities
    private bool IsValidSpawnPosition(
        EntityUid spawner,
        EntityCoordinates coords,
        SpawnOnApproachComponent comp)
    {
        var tile = _turf.GetTileRef(coords);

        if (tile == null || tile.Value.Tile.IsEmpty)
            return false;

        if (_turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask))
            return false;

        var checkRestricted = comp.RestrictedProtos.Count > 0;
        var checkMobs = !comp.SpawnInside;

        if (checkRestricted || checkMobs)
        {
            var metaQuery = GetEntityQuery<MetaDataComponent>();
            var mobQuery = GetEntityQuery<MobStateComponent>();

            foreach (var uid in _lookupSystem.GetLocalEntitiesIntersecting(tile.Value, 0f))
            {
                if (checkRestricted &&
                    metaQuery.TryGetComponent(uid, out var meta) &&
                    meta.EntityPrototype != null &&
                    comp.RestrictedProtos.Contains(meta.EntityPrototype.ID))
                {
                    return false;
                }

                if (checkMobs &&
                    uid != spawner &&
                    mobQuery.TryGetComponent(uid, out var mobState) &&
                    mobState.CurrentState != MobState.Dead)
                {
                    return false;
                }
            }
        }

        if (CheckPlayerNearby(coords, comp))
            return false;

        return true;
    }
    // ST:OW end
}
