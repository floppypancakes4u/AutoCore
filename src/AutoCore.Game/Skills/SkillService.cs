namespace AutoCore.Game.Skills;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Server-authoritative entry point for skill sources. The first implemented effect family is
/// direct repair; unsupported definitions fail without changing authoritative state.
/// </summary>
public static class SkillService
{
    private const int HealElementType = 10;

    public static bool TryCastReaction(ClonedObjectBase activator, int skillId, int skillLevel)
    {
        if (activator == null || skillId <= 0)
            return false;

        Logger.WriteLog(LogType.Debug,
            "Skill cast attempt: source=Reaction skillId={0} level={1} activator={2} coid={3} hp={4}/{5}",
            skillId,
            skillLevel,
            activator.GetType().Name,
            activator.ObjectId.Coid,
            activator.GetCurrentHP(),
            activator.GetMaximumHP());

        var skill = AssetManager.Instance.GetSkill(skillId);
        if (skill == null)
        {
            Logger.WriteLog(LogType.Error,
                "Skill cast rejected: source=Reaction skillId={0} reason=definition-not-found",
                skillId);
            return false;
        }

        var level = Math.Max(1, skillLevel);
        var heal = GetElementValue(skill, HealElementType, level, activator.GetMaximumHP());
        if (heal <= 0)
        {
            Logger.WriteLog(LogType.Error,
                "Skill cast rejected: source=Reaction skillId={0} name='{1}' reason=no-supported-effect elements=[{2}]",
                skill.Id,
                skill.Name ?? string.Empty,
                string.Join(',', skill.Elements?.Select(element => $"{element.ElementType}:{element.EquationType}:{element.ValueBase}") ?? []));
            return false;
        }

        var restored = activator.RestoreHealth(heal);
        SendEffect(activator, skill.Id, level, restored);
        Logger.WriteLog(LogType.Debug,
            "Skill cast applied: source=Reaction skillId={0} name='{1}' target={2} requestedHeal={3} restored={4} hp={5}/{6}",
            skill.Id,
            skill.Name ?? string.Empty,
            activator.ObjectId.Coid,
            heal,
            restored,
            activator.GetCurrentHP(),
            activator.GetMaximumHP());
        return true;
    }

    private static int GetElementValue(Skill skill, int elementType, int level, int maximumValue)
    {
        var element = skill.Elements?.FirstOrDefault(candidate => candidate.ElementType == elementType);
        if (element == null || element.Value.ValueBase <= 0)
            return 0;

        var value = element.Value.ValueBase + (element.Value.ValuePerLevel * Math.Max(0, level - 1));
        // Retail skill 857 (INC Repair station heal) uses equation 1 with 0.15, meaning
        // 15% of the affected pool rather than 0.15 absolute hit points.
        if (element.Value.EquationType == 1)
            value *= Math.Max(0, maximumValue);

        return Math.Max(0, (int)MathF.Round(value));
    }

    private static void SendEffect(ClonedObjectBase caster, int skillId, int skillLevel, int restored)
    {
        var character = caster.GetAsCharacter() ?? caster.GetSuperCharacter(false);
        var connection = character?.OwningConnection;
        if (connection == null)
            return;

        var packet = new SkillStatusEffectPacket
        {
            SkillId = skillId,
            SkillLevel = (short)Math.Clamp(skillLevel, short.MinValue, short.MaxValue),
            ApplyPower = Math.Max(1, restored),
            Caster = caster.ObjectId,
            PosX = caster.Position.X,
            PosY = caster.Position.Y,
            PosZ = caster.Position.Z,
        };
        packet.AddTarget(caster.ObjectId, (short)Math.Clamp(restored, short.MinValue, short.MaxValue));
        connection.SendGamePacket(packet, skipOpcode: true);
    }
}
