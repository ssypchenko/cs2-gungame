using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API;

namespace GunGame
{
    internal static class MotdSdk
    {
        internal static readonly int LinuxOffsetPredict =
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? 1 : 0;
    }

    internal unsafe struct SetStringUserDataRequest_t
    {
        public void* m_pRawData;
        public int m_cbDataSize;
    }

    internal unsafe class INetworkStringTable : NativeObject
    {
        private readonly nint* _vtable;

        public const uint INVALID_STRING_INDEX = uint.MaxValue;

        public INetworkStringTable(nint pointer) : base(pointer)
        {
            _vtable = *(nint**)Handle;
        }

        public uint AddString(bool isServer, string value, ref SetStringUserDataRequest_t userData)
        {
            return ((delegate* unmanaged<nint, bool, string, nint, uint>)
                _vtable[7 + MotdSdk.LinuxOffsetPredict])(
                    Handle,
                    isServer,
                    value,
                    (nint)Unsafe.AsPointer(ref userData));
        }
    }

    internal unsafe class INetworkStringTableContainer : NativeObject
    {
        private readonly nint* _vtable;

        public INetworkStringTableContainer(nint pointer) : base(pointer)
        {
            if (Handle == nint.Zero)
            {
                throw new InvalidOperationException("Failed to create INetworkStringTableContainer.");
            }

            _vtable = *(nint**)Handle;
        }

        public INetworkStringTable? FindTable(string tableName)
        {
            nint table = ((delegate* unmanaged<nint, string, nint>)
                _vtable[14 + MotdSdk.LinuxOffsetPredict])(Handle, tableName);

            return table != nint.Zero ? new INetworkStringTable(table) : null;
        }
    }
}
