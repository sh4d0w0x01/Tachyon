using System;

namespace MftSearch.Shared
{
    public static class MftRecordParser
    {
        public static unsafe void ParseRecordFields(byte* recordPtr, ushort majorVersion, out ulong frn, out ulong parentFrn, out ushort fileNameLength, out ushort fileNameOffset)
        {
            frn = 0;
            parentFrn = 0;
            fileNameLength = 0;
            fileNameOffset = 0;

            if (majorVersion == 2)
            {
                frn = *(ulong*)(recordPtr + 8);
                parentFrn = *(ulong*)(recordPtr + 16);
                fileNameLength = *(ushort*)(recordPtr + 56);
                fileNameOffset = *(ushort*)(recordPtr + 58);
            }
            else if (majorVersion == 3)
            {
                frn = *(ulong*)(recordPtr + 8);
                parentFrn = *(ulong*)(recordPtr + 24);
                fileNameLength = *(ushort*)(recordPtr + 72);
                fileNameOffset = *(ushort*)(recordPtr + 74);
            }
        }
    }
}
