    public static void HandleAutoPatrol(TNLConnection conn, AutoPatrolPacket packet)
    {
        if (conn == null || packet == null)
            return;

        var character = conn.CurrentCharacter;
        if (character?.Map == null)
            return;

        // Town: character on foot; field/highway: vehicle chassis (same as UseObject / triggers).
        var playerPos = GetPlayerInteractPosition(character);
        var activator = TriggerManager.ResolvePlayerTriggerActivator(character) ?? (ClonedObjectBase)character;

        var targetCoid = packet.Target?.Coid ?? -1;
        if (targetCoid <= 0)
            return;

        // Client may already be on the patrol UI after local CompleteObjective while the server
        // is still on a prior deliver (dialog desync). Catch up before matching.
        ReconcileClientAheadPatrolTarget(conn, character, targetCoid);

        var anyQuest = false;
        foreach (var quest in character.CurrentQuests.ToList())
        {
            if (character.CompletedMissionIds.Contains(quest.MissionId))
                continue;

            anyQuest = true;
            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission == null
                || !mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective))
            {
                continue;
            }

            var patrol = objective.Requirements.OfType<ObjectiveRequirementPatrol>().FirstOrDefault();
            if (patrol == null || !patrol.AutoComplete)
                continue;

            if (!PatrolListsTarget(patrol, targetCoid))
                continue;

            // Prefer server-side range when the pad exists as a live entity, graphics template,
            // or VisualWaypoint. Many mission GenericTargetCOIDs (e.g. Track This 10310–10324)
            // are client-only ghosts never present in continent .fam Templates — client only
            // emits AutoPatrol when already inside AutoCompleteDistance, so trust that gate.
            if (TryGetWorldPosition(character.Map, targetCoid, out var targetPos))
            {
                var radius = patrol.AutoCompleteDistance > 0f ? patrol.AutoCompleteDistance : 25f;
                var distXZSq = DistXZSq(playerPos, targetPos);
                if (distXZSq > radius * radius)
                {
                    Logger.WriteLog(LogType.Debug,
                        "AutoPatrol: target={0} out of range distXZ={1:F1} radius={2:F1} mission={3}",
                        targetCoid,
                        MathF.Sqrt(distXZSq),
                        radius,
                        quest.MissionId);
                    continue;
                }
            }
            else
            {
                Logger.WriteLog(LogType.Debug,
                    "AutoPatrol: target={0} listed, no map position on continent={1} mission={2} — trusting client range gate",
                    targetCoid,
                    character.Map.ContinentId,
                    quest.MissionId);
            }

            // Patrol + deliver on same objective (Final Exam class): reaching the pad waypoint
            // must not finish the mission — NPC deliver still required. Client sends AutoPatrol
            // every tick while in volume; EnsureDeliverTurnInNpc is idempotent after first setup.
            if (ObjectiveHasBlockingSiblingRequirements(objective, RequirementType.Patrol))
            {
                foreach (var deliver in objective.Requirements.OfType<ObjectiveRequirementDeliver>())
                {
                    if (!deliver.NPCTargetCompletes || deliver.NPCTargetCBID <= 0)
                        continue;

                    // Skip all work (and logging) once pad NPC is live and client was notified.
                    if (character.Map != null
                        && character.MapPresence.IsDeliverTurnInReady(deliver.NPCTargetCBID)
                        && character.Map.MapHasPresentEntityWithCbidForTests(
                            character, deliver.NPCTargetCBID))
                    {
                        continue;
                    }

                    Logger.WriteLog(LogType.Debug,
                        "AutoPatrol: patrol target={0} mission={1} seq={2} — sibling deliver; ensuring pad NPC cbid={3} once",
                        targetCoid,
                        quest.MissionId,
                        quest.ActiveObjectiveSequence,
                        deliver.NPCTargetCBID);

                    character.Map?.EnsureDeliverTurnInNpc(activator, deliver.NPCTargetCBID);
                }

                return;
            }

            LogPatrolIncomplete(patrol, quest, objective, targetCoid);
            AdvanceOrCompleteObjective(conn, character, quest, mission, objective, source: "AutoPatrol");
            return;
        }

        // Server already past the patrol that owns this pad (e.g. Track This seq2 deliver while
        // client still AutoPatrols 10310). Force-complete the finished patrol on the client and
        // resync the active objective once so waypoints clear and turn-in UI can show.
        if (TryResyncClientPastPatrol(conn, character, targetCoid))
            return;

        if (anyQuest)
        {
            Logger.WriteLog(LogType.Debug,
                "AutoPatrol: no matching active patrol for target={0} charCoid={1} quests=[{2}]",
                targetCoid,
                character.ObjectId.Coid,
                string.Join(',', character.CurrentQuests.Select(q =>
                    $"{q.MissionId}:seq{q.ActiveObjectiveSequence}")));
        }
    }

    /// <summary>
    /// Client still AutoPatrols a pad from a finished objective sequence. Send 0x2070 for that
    /// patrol objective + journal/active ObjectiveState so the client leaves the waypoint UI.
    /// One-shot per mission per continent (client spam).
    /// </summary>
    private static bool TryResyncClientPastPatrol(
        TNLConnection conn,
        Character character,
        long targetCoid)
    {
        if (conn == null || character?.Map == null || targetCoid <= 0)
            return false;

        character.MapPresence.EnsureContinent(character.Map.ContinentId);

        foreach (var quest in character.CurrentQuests)
        {
            if (character.CompletedMissionIds.Contains(quest.MissionId))
                continue;
            if (character.MapPresence.HasStalePatrolResync(quest.MissionId))
                continue;

            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission?.Objectives == null)
                continue;

            // Only resync when the server has a real later active objective (not a bad/empty seq).
            if (!mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var activeObj)
                || activeObj == null)
            {
                continue;
            }

            MissionObjective pastPatrolObj = null;
            foreach (var obj in mission.Objectives.Values)
            {
                if (obj.Sequence >= quest.ActiveObjectiveSequence)
                    continue;

                var patrol = obj.Requirements?.OfType<ObjectiveRequirementPatrol>().FirstOrDefault();
                if (patrol == null || !patrol.AutoComplete)
                    continue;
                if (!PatrolListsTarget(patrol, targetCoid))
                    continue;

                pastPatrolObj = obj;
                break;
            }

            if (pastPatrolObj == null)
                continue;

            character.MapPresence.MarkStalePatrolResync(quest.MissionId);

            Logger.WriteLog(LogType.Debug,
                "AutoPatrol: stale client patrol target={0} mission={1} finishedObj={2} activeSeq={3} — force-complete + resync",
                targetCoid,
                quest.MissionId,
                pastPatrolObj.ObjectiveId,
                quest.ActiveObjectiveSequence);

            conn.SendGamePacket(new CompleteDynamicObjectivePacket
            {
                MissionId = quest.MissionId,
                ObjectiveId = pastPatrolObj.ObjectiveId,
            });

            ResyncActiveMissionToClient(conn, character, quest);
            TriggerManager.Instance.OnMissionStateChanged(
                character.CurrentVehicle ?? (ClonedObjectBase)character);
            character.Map?.ReplayMissionWorldSetup(
                character.CurrentVehicle ?? (ClonedObjectBase)character);
            return true;
        }

        return false;
    }

    /// <summary>
    /// When the client AutoPatrols a waypoint that belongs to a later objective (typical after
    /// local CompleteObjective on a prior deliver while server stayed on that deliver), advance
    /// intermediate sequences until the matching patrol is active.
    /// </summary>
    private static void ReconcileClientAheadPatrolTarget(
        TNLConnection conn,
        Character character,
        long targetCoid)
    {
        if (character == null || targetCoid <= 0)
            return;

        foreach (var quest in character.CurrentQuests.ToList())
        {
            if (character.CompletedMissionIds.Contains(quest.MissionId))
                continue;

            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission?.Objectives == null || mission.Objectives.Count == 0)
                continue;

            // Earliest objective at/after active whose AutoComplete patrol lists this target.
            MissionObjective match = null;
            foreach (var obj in mission.Objectives.Values.OrderBy(o => o.Sequence))
            {
                if (obj.Sequence < quest.ActiveObjectiveSequence)
                    continue;

                var patrol = obj.Requirements?.OfType<ObjectiveRequirementPatrol>().FirstOrDefault();
                if (patrol == null || !patrol.AutoComplete)
                    continue;
                if (!PatrolListsTarget(patrol, targetCoid))
                    continue;

                match = obj;
                break;
            }

            if (match == null || match.Sequence <= quest.ActiveObjectiveSequence)
                continue;

            // Advance intermediate objectives (e.g. deliver-1) so patrol becomes active.
            var guard = 0;
            while (character.CurrentQuests.Contains(quest)
                && quest.ActiveObjectiveSequence < match.Sequence
                && guard++ < 16)
            {
                if (!mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var current)
                    || current == null)
                {
                    break;
                }

                Logger.WriteLog(LogType.Debug,
                    "AutoPatrol: client-ahead reconcile mission={0} seq {1} -> toward {2} (target={3})",
                    quest.MissionId,
                    quest.ActiveObjectiveSequence,
                    match.Sequence,
                    targetCoid);

                AdvanceOrCompleteObjective(
                    conn,
                    character,
                    quest,
                    mission,
                    current,
                    source: "AutoPatrolReconcile");
            }
        }
    }

    /// <summary>
    /// True when the objective has other requirements that still need a separate server event
    /// before the objective may advance/complete (e.g. deliver turn-in alongside patrol).
    /// </summary>
    internal static bool ObjectiveHasBlockingSiblingRequirements(
        MissionObjective objective,
        RequirementType satisfiedType)
        => MissionWorldPhaseRules.HasBlockingDeliverSibling(objective, satisfiedType);

    private static int CountPatrolTargets(ObjectiveRequirementPatrol patrol)
    {
        var count = Math.Max(patrol.TargetCount, 0);
        if (count == 0)
        {
            for (var i = 0; i < patrol.GenericTargets.Length; i++)
            {
                if (patrol.GenericTargets[i] > 0)
                    count++;
            }
        }

        return count;
    }

    private static void LogPatrolIncomplete(
        ObjectiveRequirementPatrol patrol,
        CharacterQuest quest,
        MissionObjective objective,
        long targetCoid)
    {
        var listed = CountPatrolTargets(patrol);
        var context =
            $"mission={quest.MissionId} seq={quest.ActiveObjectiveSequence} objective={objective.ObjectiveId} target={targetCoid} listedTargets={listed}";

        if (listed > 1)
        {
            IncompleteHandlerLog.Warn(
                "AutoPatrol",
                context,
                "Multi-waypoint patrol: any listed target currently completes the entire objective (no per-point progress)",
                "Track visited GenericTargets (Sequential order if Sequential=true); only AdvanceOrComplete when all points (or current lap) are done; update ObjectiveState slots/bitmask per FirstStateSlot.");
        }

        if (patrol.Laps > 1)
        {
            IncompleteHandlerLog.Warn(
                "AutoPatrol",
                context,
                $"Laps={patrol.Laps} ignored — single pass completes the objective",
                "Maintain lap counter on CharacterQuest progress; require Laps full circuits before complete.");
        }

        if (patrol.Sequential && listed > 1)
        {
            IncompleteHandlerLog.Warn(
                "AutoPatrol",
                context,
                "Sequential=true ignored — targets may be hit in any order",
                "Enforce GenericTargets order; reject or ignore out-of-order AutoPatrol until previous COID cleared.");
        }

        if (patrol.AutoFail)
        {
            IncompleteHandlerLog.Warn(
                "AutoPatrol",
                context,
                $"AutoFail=true (distance={patrol.AutoFailDistance}) not monitored",
                "On movement/tick, fail mission or objective when player leaves AutoFailDistance from active route.");
        }

        if (patrol.ContinentId > 0)
        {
            IncompleteHandlerLog.Warn(
                "AutoPatrol",
                context,
                $"ContinentCBID={patrol.ContinentId} not validated against current map",
                "Reject AutoPatrol progress when character map continent does not match requirement ContinentId.");
        }
    }

