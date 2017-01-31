using System.Runtime.InteropServices;

namespace TwinRx.Tests
{
    public struct MyPlcStruct
    {
        public short myInt;

        [MarshalAs(UnmanagedType.I1)]
        public bool myBool;
    }
}