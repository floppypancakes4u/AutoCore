using System;
using System.IO;

namespace AutoCore.Game.Structures
{
    public class TFID
    {
        public long Coid { get; set; }
        public bool Global { get; set; }

        public TFID()
        {
            Coid = -1L;
            Global = false;
        }

        public TFID(long coid, bool global)
        {
            Coid = coid;
            Global = global;
        }

        public static TFID Read(BinaryReader br)
        {
            return new TFID
            {
                Global = br.ReadByte() != 0,
                Coid = br.ReadUInt32()
            };
        }

        public override bool Equals(object obj)
        {
            return obj is TFID tFID &&
                   Coid == tFID.Coid &&
                   Global == tFID.Global;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Coid, Global);
        }

        public static bool operator ==(TFID a, TFID b)
        {
            return a is not null && b is not null && a.Coid == b.Coid && a.Global == b.Global;
        }

        public static bool operator !=(TFID a, TFID b)
        {
            return a is null || b is null || a.Coid != b.Coid || a.Global != b.Global;
        }

        public static bool operator <(TFID a, TFID b)
        {
            return a is not null && b is not null && a.Coid < b.Coid;
        }

        public static bool operator >(TFID a, TFID b)
        {
            return a is not null && b is not null && a.Coid > b.Coid;
        }
    }
}
