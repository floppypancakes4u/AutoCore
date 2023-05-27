using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public enum RequirementType
{
    Kill = 0,
    KillAggregate = 1,
    Collect = 2,
    Deliver = 3,
    Stunt = 4,
    Money = 5,
    Mission = 6,
    Km = 7,
    TimePlayed = 8,
    Patrol = 9,
    CharacterLevel = 10,
    CraftItem = 11,
    UseItem = 12,
    Escort = 13,
    CrazyTaxi = 14,
    Rampage = 15,
    Survivor = 16,
}

public abstract class ObjectiveRequirement
{
    public MissionObjective ObjectiveOwner { get; set; }
    public RequirementType RequirementType { get; set; }
    public byte FirstStateSlot { get; set; }

    protected ObjectiveRequirement(MissionObjective owner)
    {
        ObjectiveOwner = owner;
    }

    public virtual void UnSerialize(XElement elem)
    {
        FirstStateSlot = (byte)(int)elem.Attribute("slot");
    }

    public static ObjectiveRequirement Create(MissionObjective owner, XElement elem)
    {
        var type = (string)elem.Attribute("type");

        ObjectiveRequirement req;

        switch (type)
        {
            case "kill":
                req = new ObjectiveRequirementKill(owner);
                break;

            case "kill_aggregate":
                req = new ObjectiveRequirementKillAggregate(owner);
                break;

            case "collect":
                req = new ObjectiveRequirementCollect(owner);
                break;

            case "deliver":
                req = new ObjectiveRequirementDeliver(owner);
                break;

            case "money":
                req = new ObjectiveRequirementMoney(owner);
                break;

            case "stunt":
                req = new ObjectiveRequirementStunt(owner);
                break;

            case "mission":
                req = new ObjectiveRequirementMission(owner);
                break;

            case "km":
                req = new ObjectiveRequirementKm(owner);
                break;

            case "timeplayed":
                req = new ObjectiveRequirementTimePlayed(owner);
                break;

            case "patrol":
                req = new ObjectiveRequirementPatrol(owner);
                break;

            case "useitem":
                req = new ObjectiveRequirementUseItem(owner);
                break;

            case "characterlevel":
                req = new ObjectiveRequirementCharacterLevel(owner);
                break;

            case "escort":
                req = new ObjectiveRequirementEscort(owner);
                break;

            case "crazytaxi":
                req = new ObjectiveRequirementCrazyTaxi(owner);
                break;

            default:
                return null;
        }

        req.UnSerialize(elem);

        return req;
    }
}
