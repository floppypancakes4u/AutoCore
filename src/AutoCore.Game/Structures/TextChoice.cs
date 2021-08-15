using System.Collections.Generic;

namespace AutoCore.Game.Structures
{
    public struct TextChoice
    {
        public string Text;
        public List<TextParam> TextParams;
        public ulong TriggerCOID;
    }
}
