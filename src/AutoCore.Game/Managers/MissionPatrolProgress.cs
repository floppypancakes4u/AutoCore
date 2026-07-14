namespace AutoCore.Game.Managers;

using AutoCore.Game.Mission.Requirements;

/// <summary>
/// Pure multi-waypoint patrol progress for AutoPatrol (0x20B3).
/// Sequential: <c>ObjectiveProgress</c> is pads accepted (0..needed).
/// Non-sequential: packed <c>(completedLaps &lt;&lt; 20) | bitmask</c> of visited pads this lap.
/// </summary>
public static class MissionPatrolProgress
{
    public const int MaxTrackableTargets = 20;
    public const int NonSequentialLapShift = 20;
    public const int NonSequentialMaskBits = (1 << NonSequentialLapShift) - 1;

    public readonly struct HitResult
    {
        public bool Accepted { get; init; }
        public int NewProgress { get; init; }
        public bool IsComplete { get; init; }
        public int DisplayProgress { get; init; }
        public int Needed { get; init; }
    }

    /// <summary>Listed pad COIDs with a positive GenericTarget entry.</summary>
    public static int CountListedTargets(ObjectiveRequirementPatrol patrol)
    {
        if (patrol == null)
            return 0;

        var count = Math.Max(patrol.TargetCount, 0);
        if (count == 0)
        {
            for (var i = 0; i < patrol.GenericTargets.Length; i++)
            {
                if (patrol.GenericTargets[i] > 0)
                    count++;
            }

            return count;
        }

        var listed = 0;
        for (var i = 0; i < count && i < patrol.GenericTargets.Length; i++)
        {
            if (patrol.GenericTargets[i] > 0)
                listed++;
        }

        return listed;
    }

    /// <summary>Pads required before the patrol objective may complete.</summary>
    public static int NeededCount(ObjectiveRequirementPatrol patrol)
    {
        var targets = CountListedTargets(patrol);
        if (targets <= 0)
            return 1;

        var laps = patrol.Laps > 0 ? patrol.Laps : 1;
        return targets * laps;
    }

    /// <summary>
    /// UI / ObjectiveState ratio numerator (0..needed) from encoded progress.
    /// </summary>
    public static int DisplayProgress(ObjectiveRequirementPatrol patrol, int encodedProgress)
    {
        if (patrol == null)
            return 0;

        var needed = NeededCount(patrol);
        if (needed <= 0)
            return 0;

        if (patrol.Sequential)
            return Math.Clamp(encodedProgress, 0, needed);

        var targets = CountListedTargets(patrol);
        if (targets <= 0)
            return Math.Clamp(encodedProgress, 0, needed);

        var maskWidth = Math.Min(targets, MaxTrackableTargets);
        var mask = encodedProgress & ((1 << maskWidth) - 1);
        var lapsDone = encodedProgress >> NonSequentialLapShift;
        var pop = PopCount(mask);
        return Math.Clamp(lapsDone * targets + pop, 0, needed);
    }

    /// <summary>
    /// Apply one AutoPatrol target hit. Returns Accepted=false when ignored (wrong order,
    /// already visited this lap, unknown COID, or already complete).
    /// </summary>
    public static HitResult TryApplyHit(
        ObjectiveRequirementPatrol patrol,
        int currentProgress,
        long targetCoid)
    {
        if (patrol == null || targetCoid <= 0)
        {
            return new HitResult
            {
                Accepted = false,
                NewProgress = currentProgress,
                IsComplete = false,
                DisplayProgress = 0,
                Needed = 1,
            };
        }

        var needed = NeededCount(patrol);
        var targets = CountListedTargets(patrol);
        if (targets <= 0)
        {
            // No listed pads — treat a listed-check failure as no-op (caller filters first).
            return new HitResult
            {
                Accepted = false,
                NewProgress = currentProgress,
                IsComplete = false,
                DisplayProgress = 0,
                Needed = needed,
            };
        }

        if (patrol.Sequential)
            return ApplySequential(patrol, currentProgress, targetCoid, targets, needed);

        return ApplyNonSequential(patrol, currentProgress, targetCoid, targets, needed);
    }

    private static HitResult ApplySequential(
        ObjectiveRequirementPatrol patrol,
        int currentProgress,
        long targetCoid,
        int targets,
        int needed)
    {
        var progress = Math.Max(0, currentProgress);
        if (progress >= needed)
        {
            return new HitResult
            {
                Accepted = false,
                NewProgress = progress,
                IsComplete = true,
                DisplayProgress = needed,
                Needed = needed,
            };
        }

        var hitIndex = IndexOfTarget(patrol, targetCoid, targets);
        if (hitIndex < 0)
        {
            return new HitResult
            {
                Accepted = false,
                NewProgress = progress,
                IsComplete = false,
                DisplayProgress = progress,
                Needed = needed,
            };
        }

        // Exact sequential only. Client GetTarget/Eval use absolute pad counts
        // (CVOGObjectiveRequirement_Patrol_GetTarget/Eval @ 0x0060e370 / 0x0060e0f0);
        // progress is not advanced client-side without ObjectiveState. Catch-up to a later
        // pad (old behavior) could complete the whole route on one wrong COID and jump the
        // UI to the deliver NPC (LOA Jimmy Chrome).
        var expectedIndex = progress % targets;
        if (hitIndex != expectedIndex)
        {
            return new HitResult
            {
                Accepted = false,
                NewProgress = progress,
                IsComplete = false,
                DisplayProgress = progress,
                Needed = needed,
            };
        }

        var next = Math.Min(progress + 1, needed);
        return new HitResult
        {
            Accepted = true,
            NewProgress = next,
            IsComplete = next >= needed,
            DisplayProgress = next,
            Needed = needed,
        };
    }

    private static HitResult ApplyNonSequential(
        ObjectiveRequirementPatrol patrol,
        int currentProgress,
        long targetCoid,
        int targets,
        int needed)
    {
        var maskWidth = Math.Min(targets, MaxTrackableTargets);
        var mask = currentProgress & ((1 << maskWidth) - 1);
        var lapsDone = currentProgress >> NonSequentialLapShift;
        var lapsRequired = patrol.Laps > 0 ? patrol.Laps : 1;

        if (lapsDone >= lapsRequired)
        {
            return new HitResult
            {
                Accepted = false,
                NewProgress = currentProgress,
                IsComplete = true,
                DisplayProgress = needed,
                Needed = needed,
            };
        }

        var index = IndexOfTarget(patrol, targetCoid, targets);
        if (index < 0 || index >= maskWidth)
        {
            return new HitResult
            {
                Accepted = false,
                NewProgress = currentProgress,
                IsComplete = false,
                DisplayProgress = DisplayProgress(patrol, currentProgress),
                Needed = needed,
            };
        }

        var bit = 1 << index;
        if ((mask & bit) != 0)
        {
            return new HitResult
            {
                Accepted = false,
                NewProgress = currentProgress,
                IsComplete = false,
                DisplayProgress = DisplayProgress(patrol, currentProgress),
                Needed = needed,
            };
        }

        mask |= bit;
        var pop = PopCount(mask);
        if (pop >= targets)
        {
            lapsDone++;
            mask = 0;
        }

        var encoded = (lapsDone << NonSequentialLapShift) | mask;
        var complete = lapsDone >= lapsRequired;
        return new HitResult
        {
            Accepted = true,
            NewProgress = encoded,
            IsComplete = complete,
            DisplayProgress = complete ? needed : DisplayProgress(patrol, encoded),
            Needed = needed,
        };
    }

    private static int IndexOfTarget(ObjectiveRequirementPatrol patrol, long targetCoid, int targets)
    {
        var count = Math.Max(patrol.TargetCount, 0);
        if (count == 0)
            count = patrol.GenericTargets.Length;

        var limit = Math.Min(Math.Min(count, targets), patrol.GenericTargets.Length);
        for (var i = 0; i < limit; i++)
        {
            if (patrol.GenericTargets[i] == targetCoid)
                return i;
        }

        return -1;
    }

    private static int PopCount(int value)
    {
        var n = 0;
        var v = value;
        while (v != 0)
        {
            n += v & 1;
            v >>= 1;
        }

        return n;
    }
}
