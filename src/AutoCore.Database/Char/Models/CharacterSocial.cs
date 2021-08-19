namespace AutoCore.Database.Char.Models
{
    public enum SocialType
    {
        Friend,
        Enemy
    }

    public class CharacterSocial
    {
        public long CharacterCoid { get; set; }
        public long TargetCoid { get; set; }
        public byte Type { get; set; }

        public SocialType SocialType
        {
            get
            {
                return (SocialType)Type;
            }
            set
            {
                Type = (byte)value;
            }
        }
    }
}
