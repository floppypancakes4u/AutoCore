from pathlib import Path
import subprocess

data = subprocess.check_output(
    ["git", "show", "HEAD:src/AutoCore.Game/Managers/NpcInteractHandler.cs"]
)
text_head = data.decode("utf-8") if data[:2] != b"\xff\xfe" else data.decode("utf-16")
start_h = text_head.find("    public static void HandleAutoPatrol")
end_h = text_head.find("    private static bool PatrolListsTarget")
assert start_h > 0 and end_h > start_h, (start_h, end_h)
head = text_head[start_h:end_h]

def rep(old: str, new: str, label: str) -> None:
    global head
    if old not in head:
        raise SystemExit(f"missing chunk for {label}")
    head = head.replace(old, new, 1)

rep(
    """        var targetCoid = packet.Target?.Coid ?? -1;
        if (targetCoid <= 0)
            return;

        // Client may already be on the patrol UI after local CompleteObjective while the server
        // is still on a prior deliver (dialog desync). Catch up before matching.
        ReconcileClientAheadPatrolTarget(conn, character, targetCoid);""",
    """        var targetCoid = packet.Target?.Coid ?? -1;
        if (targetCoid <= 0)
            return;

        MissionFlowDiag.Log(
            "AutoPatrol IN coid={0} char={1} cont={2} pos=({3:F1},{4:F1},{5:F1}) {6}",
            targetCoid,
            character.ObjectId.Coid,
            character.Map.ContinentId,
            playerPos.X, playerPos.Y, playerPos.Z,
            MissionFlowDiag.QuestSummary(character));

        // Client may already be on the patrol UI after local CompleteObjective while the server
        // is still on a prior deliver (dialog desync). Catch up before matching.
        var seqBeforeReconcile = character.CurrentQuests
            .Select(q => (q.MissionId, q.ActiveObjectiveSequence)).ToList();
        ReconcileClientAheadPatrolTarget(conn, character, targetCoid);
        var seqAfterReconcile = character.CurrentQuests
            .Select(q => (q.MissionId, q.ActiveObjectiveSequence)).ToList();
        if (!seqBeforeReconcile.SequenceEqual(seqAfterReconcile))
        {
            MissionFlowDiag.Log(
                "AutoPatrol RECONCILE changed before={0} after={1} target={2}",
                string.Join(',', seqBeforeReconcile.Select(t => $"m{t.MissionId}:s{t.ActiveObjectiveSequence}")),
                string.Join(',', seqAfterReconcile.Select(t => $"m{t.MissionId}:s{t.ActiveObjectiveSequence}")),
                targetCoid);
        }""",
    "entry+reconcile",
)

rep(
    """                    Logger.WriteLog(LogType.Debug,
                        "AutoPatrol: target={0} out of range distXZ={1:F1} radius={2:F1} mission={3}",
                        targetCoid,
                        MathF.Sqrt(distXZSq),
                        radius,
                        quest.MissionId);
                    continue;""",
    """                    MissionFlowDiag.Log(
                        "AutoPatrol REJECT range target={0} distXZ={1:F1} radius={2:F1} mission={3} seq={4}",
                        targetCoid,
                        MathF.Sqrt(distXZSq),
                        radius,
                        quest.MissionId,
                        quest.ActiveObjectiveSequence);
                    Logger.WriteLog(LogType.Debug,
                        "AutoPatrol: target={0} out of range distXZ={1:F1} radius={2:F1} mission={3}",
                        targetCoid,
                        MathF.Sqrt(distXZSq),
                        radius,
                        quest.MissionId);
                    continue;""",
    "range reject",
)

rep(
    """                Logger.WriteLog(LogType.Debug,
                    "AutoPatrol: target={0} listed, no map position on continent={1} mission={2} — trusting client range gate",
                    targetCoid,
                    character.Map.ContinentId,
                    quest.MissionId);""",
    """                MissionFlowDiag.Log(
                    "AutoPatrol TRUST no-map-pos target={0} cont={1} mission={2} seq={3}",
                    targetCoid,
                    character.Map.ContinentId,
                    quest.MissionId,
                    quest.ActiveObjectiveSequence);
                Logger.WriteLog(LogType.Debug,
                    "AutoPatrol: target={0} listed, no map position on continent={1} mission={2} — trusting client range gate",
                    targetCoid,
                    character.Map.ContinentId,
                    quest.MissionId);""",
    "trust no pos",
)

rep(
    """            LogPatrolIncomplete(patrol, quest, objective, targetCoid);
            AdvanceOrCompleteObjective(conn, character, quest, mission, objective, source: "AutoPatrol");
            return;""",
    """            LogPatrolIncomplete(patrol, quest, objective, targetCoid);
            MissionFlowDiag.Log(
                "AutoPatrol ADVANCE mission={0} seq={1} obj={2} target={3} listedTargets={4}",
                quest.MissionId,
                quest.ActiveObjectiveSequence,
                objective.ObjectiveId,
                targetCoid,
                CountPatrolTargets(patrol));
            AdvanceOrCompleteObjective(conn, character, quest, mission, objective, source: "AutoPatrol");
            MissionFlowDiag.Log(
                "AutoPatrol AFTER advance mission={0} {1}",
                quest.MissionId,
                MissionFlowDiag.QuestSummary(character));
            return;""",
    "advance",
)

rep(
    """            Logger.WriteLog(LogType.Debug,
                "AutoPatrol: stale client patrol target={0} mission={1} finishedObj={2} activeSeq={3} — force-complete + resync",
                targetCoid,
                quest.MissionId,
                pastPatrolObj.ObjectiveId,
                quest.ActiveObjectiveSequence);""",
    """            MissionFlowDiag.Log(
                "AutoPatrol STALE-RESYNC target={0} mission={1} finishedObj={2} activeSeq={3}",
                targetCoid,
                quest.MissionId,
                pastPatrolObj.ObjectiveId,
                quest.ActiveObjectiveSequence);
            Logger.WriteLog(LogType.Debug,
                "AutoPatrol: stale client patrol target={0} mission={1} finishedObj={2} activeSeq={3} — force-complete + resync",
                targetCoid,
                quest.MissionId,
                pastPatrolObj.ObjectiveId,
                quest.ActiveObjectiveSequence);""",
    "stale",
)

rep(
    """            Logger.WriteLog(LogType.Debug,
                "AutoPatrol: no matching active patrol for target={0} charCoid={1} quests=[{2}]",
                targetCoid,
                character.ObjectId.Coid,
                string.Join(',', character.CurrentQuests.Select(q =>
                    $"{q.MissionId}:seq{q.ActiveObjectiveSequence}")));""",
    """            MissionFlowDiag.Log(
                "AutoPatrol NO-MATCH target={0} char={1} {2}",
                targetCoid,
                character.ObjectId.Coid,
                MissionFlowDiag.QuestSummary(character));
            Logger.WriteLog(LogType.Debug,
                "AutoPatrol: no matching active patrol for target={0} charCoid={1} quests=[{2}]",
                targetCoid,
                character.ObjectId.Coid,
                string.Join(',', character.CurrentQuests.Select(q =>
                    $"{q.MissionId}:seq{q.ActiveObjectiveSequence}")));""",
    "no match",
)

rep(
    """                Logger.WriteLog(LogType.Debug,
                    "AutoPatrol: client-ahead reconcile mission={0} seq {1} -> toward {2} (target={3})",
                    quest.MissionId,
                    quest.ActiveObjectiveSequence,
                    match.Sequence,
                    targetCoid);""",
    """                MissionFlowDiag.Log(
                    "AutoPatrol RECONCILE-ADVANCE mission={0} seq {1} -> toward {2} target={3}",
                    quest.MissionId,
                    quest.ActiveObjectiveSequence,
                    match.Sequence,
                    targetCoid);
                Logger.WriteLog(LogType.Debug,
                    "AutoPatrol: client-ahead reconcile mission={0} seq {1} -> toward {2} (target={3})",
                    quest.MissionId,
                    quest.ActiveObjectiveSequence,
                    match.Sequence,
                    targetCoid);""",
    "reconcile advance",
)

rep(
    """                    Logger.WriteLog(LogType.Debug,
                        "AutoPatrol: patrol target={0} mission={1} seq={2} — sibling deliver; ensuring pad NPC cbid={3} once",
                        targetCoid,
                        quest.MissionId,
                        quest.ActiveObjectiveSequence,
                        deliver.NPCTargetCBID);""",
    """                    MissionFlowDiag.Log(
                        "AutoPatrol SIBLING-DELIVER target={0} mission={1} seq={2} deliverCbid={3}",
                        targetCoid,
                        quest.MissionId,
                        quest.ActiveObjectiveSequence,
                        deliver.NPCTargetCBID);
                    Logger.WriteLog(LogType.Debug,
                        "AutoPatrol: patrol target={0} mission={1} seq={2} — sibling deliver; ensuring pad NPC cbid={3} once",
                        targetCoid,
                        quest.MissionId,
                        quest.ActiveObjectiveSequence,
                        deliver.NPCTargetCBID);""",
    "sibling",
)

path = Path("src/AutoCore.Game/Managers/NpcInteractHandler.cs")
text = path.read_text(encoding="utf-8")
start = text.find("    public static void HandleAutoPatrol")
end = text.find("    private static bool PatrolListsTarget")
assert start > 0 and end > start, (start, end)
path.write_text(text[:start] + head + text[end:], encoding="utf-8", newline="\n")
print("OK restored AutoPatrol + diag, bytes", len(head))
