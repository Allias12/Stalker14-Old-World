    using Robust.Shared.Prototypes;

    namespace Content.Server._Stalker.SpawnOnApproach;

    [RegisterComponent, AutoGenerateComponentPause]
    public sealed partial class SpawnOnApproachComponent : Component
    {
        [ViewVariables(VVAccess.ReadOnly)] public bool Enabled;

        /// <summary>
        /// Determines whether to spawn entities on componentInit
        /// </summary>
        [DataField] public bool InstantSpawn;

        [DataField("prototypes")] public List<EntProtoId> EntProtoIds = new(); // ST:OW

        [DataField("restricted")] public List<EntProtoId> RestrictedProtos = new(); // ST:OW

        [DataField] public int MinAmount;

        [DataField] public int MaxAmount;

        [DataField] public float MaxOffset;

        [DataField] public float MinOffset;

        [DataField] public float Chance;

        // ST:OW begin
        /// <summary>
        /// Percent added to spawn chance per failed spawn
        /// </summary>
        [DataField] public float ChanceIncreaseOnFailure;
        
        /// <summary>
        /// Diagnostics for staff and debugging in View Variables
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly)] public int ConsecutiveFailures;
        
        [ViewVariables(VVAccess.ReadOnly)] public SpawnOnApproachResult LastResult;

        [ViewVariables(VVAccess.ReadOnly)] public int LastRequestedAmount;

        [ViewVariables(VVAccess.ReadOnly)] public int LastSpawnedAmount;

        [ViewVariables(VVAccess.ReadOnly)] public float LastEffectiveChance;
        // ST:OW end

        // ST14-EN Addition
        [DataField] public bool SpawnNearPlayers = false;

        /// <summary>
        /// If system should avoid spawning entities inside each other
        /// Useful when you need to spawn some static objects, like bushes
        /// </summary>
        [DataField] public bool SpawnInside = true;

        /// <summary>
        /// Cooldown in seconds
        /// </summary>
        [DataField] public float Cooldown;

        // ST:OW begin
        /// <summary>
        /// Cooldown in seconds after a failed chance roll
        /// If it is unset then spawner will default to base CD
        /// </summary>
        [DataField] public float? FailureCooldown;
        // ST:OW end

        /// <summary>
        /// System field to track cooldown
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
        public TimeSpan? CoolDownTime;

        [DataField] public TimeSpan? MinStartAction;

        /// <summary>
        /// Set timeout on each dice roll
        /// It's needed for crates triggers. Or they will try to spawn each time when somebody goes by
        /// Making chance of spawn basically useless
        /// </summary>
        [DataField("timeoutOnRoll")] public bool ShouldTimeoutOnRoll;

        // ST:OW begin
        /// <summary>
        /// Maximum number of random spawn attempts
        /// </summary>
        [DataField] public int MaxSpawnAttempts = 30;

        /// <summary>
        /// Minimum distance from a player that a mutant is eligible to spawn
        /// </summary>
        [DataField] public float MinPlayerDistance;
    }

    public enum SpawnOnApproachResult : byte
        {
            None,
            ChanceFailed,
            NoValidPosition,
            ZeroAmount,
            PartialSpawn,
            Spawned,
        }
        // ST:OW end