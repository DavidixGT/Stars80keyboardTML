using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Terraria;
using Terraria.ModLoader;

namespace stars80qmkmod
{
    public class Stars80ModSystem : ModSystem
    {
        private const ushort VendorId = 0x342D;
        private const ushort ProductId = 0xE401;
        private const ushort UsagePage = 0xFF60;
        private const ushort Usage = 0x61;

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000; // CRITICAL: Enables true async kernel I/O
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        public static readonly int[][] LayoutMap = new int[][]
        {
            new int[] {0,  1,  2,  3,  4,  5,  6,  7,  8,  9,  10, 11, 12, 13, 14, 15, 16},
            new int[] {33, 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17},
            new int[] {34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50},
            new int[] {64, 63, 62, 61, 60, 59, 58, 57, 56, 55, 54, 53, 52, 51, 52, 53, 54}, 
            new int[] {65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81},
            new int[] {85, 86, 87, 84, 83, 88, 89, 90, 91, 82, 92, 93, 94, 95, 96, 97, 98}
        };

        private static readonly List<int> FlattenedUniqueKeys = new List<int>();
        public static SafeFileHandle DeviceHandle = null;
        private static FileStream DeviceStream = null; // .NET wrapper for lightning-fast async I/O

        // Pre-allocated static buffers to avoid GC allocations during active gameplay fades
        private static readonly List<byte[]> PreallocatedPackets = new List<byte[]>();

        public override void OnModLoad()
        {
            if (Main.dedServ || !OperatingSystem.IsWindows()) return;
            
            FlattenedUniqueKeys.Clear();
            HashSet<int> seen = new HashSet<int>();
            for (int row = 0; row < LayoutMap.Length; row++)
            {
                for (int col = 0; col < LayoutMap[row].Length; col++)
                {
                    int key = LayoutMap[row][col];
                    if (seen.Add(key)) 
                    {
                        FlattenedUniqueKeys.Add(key);
                    }
                }
            }

            // Pre-calculate packets structure layout frames shape once on game boot
            PreallocatedPackets.Clear();
            int totalKeys = FlattenedUniqueKeys.Count;
            int index = 0;
            while (index < totalKeys)
            {
                byte[] packet = new byte[33];
                packet[1] = 0x02; // Batch update token command index descriptor
                int keysInThisPacket = Math.Min(6, totalKeys - index);
                packet[2] = (byte)keysInThisPacket;

                int byteOffset = 3;
                for (int i = 0; i < keysInThisPacket; i++)
                {
                    packet[byteOffset] = (byte)FlattenedUniqueKeys[index++];
                    byteOffset += 4; // Colors will safely override offsets 1,2,3 dynamically later
                }
                PreallocatedPackets.Add(packet);
            }

            DeviceHandle = FindAndOpenStars80();
            if (DeviceHandle != null && !DeviceHandle.IsInvalid)
            {
                // Create a managed stream that uses pure underlying Windows Overlapped I/O
                DeviceStream = new FileStream(DeviceHandle, FileAccess.ReadWrite, 33, true);
            }
        }

        public override void OnModUnload()
        {
            if (DeviceStream != null)
            {
                ClearAllKeysSync(); // Quick clean shutdown sequence injection
                DeviceStream.Dispose();
                DeviceStream = null;
            }
            if (DeviceHandle != null && !DeviceHandle.IsInvalid && !DeviceHandle.IsClosed)
            {
                DeviceHandle.Dispose();
                DeviceHandle = null;
            }
        }
        private static readonly byte[] SingleKeyPayload = new byte[33];
private static readonly object SingleKeyLock = new object();

public static async Task SetKeyColorAsync(int ledIndex, byte r, byte g, byte b)
{
    if (DeviceStream == null || !DeviceStream.CanWrite) return;

    // Use a lock to ensure multi-threaded calls do not overwrite the single buffer simultaneously
    lock (SingleKeyLock)
    {
        SingleKeyPayload[1] = 0x02;            // Command: Batch/Single Update
        SingleKeyPayload[2] = 1;               // Total keys inside packet (1)
        SingleKeyPayload[3] = (byte)ledIndex;  // Targeted Key Index
        SingleKeyPayload[4] = g;               // Corrected RGB mapping
        SingleKeyPayload[5] = r;               
        SingleKeyPayload[6] = b;               
    }

    try
    {
        // Sends the single packet instantly to the OS kernel without stalling the game thread
        await DeviceStream.WriteAsync(SingleKeyPayload, 0, SingleKeyPayload.Length).ConfigureAwait(false);
        await DeviceStream.FlushAsync().ConfigureAwait(false);
    }
    catch { /* Device disconnected gracefully or busy */ }
}

        // True non-blocking asynchronous hardware push method call
        public static async Task SetEntireKeyboardColorAsync(byte r, byte g, byte b)
        {
            if (DeviceStream == null || !DeviceStream.CanWrite) return;

            try
            {
                foreach (var packet in PreallocatedPackets)
                {
                    int keysInThisPacket = packet[2];
                    int byteOffset = 3;
                    for (int i = 0; i < keysInThisPacket; i++)
                    {
                        packet[byteOffset + 1] = g; // Update live target calculations smoothly
                        packet[byteOffset + 2] = r;
                        packet[byteOffset + 3] = b;
                        byteOffset += 4;
                    }

                    // Sends packets instantly to kernel without stalling any game engine worker loops
                    await DeviceStream.WriteAsync(packet, 0, packet.Length).ConfigureAwait(false);
                }
                await DeviceStream.FlushAsync().ConfigureAwait(false);
            }
            catch { /* Device disconnected gracefully or busy */ }
        }

        private static void ClearAllKeysSync()
        {
            if (DeviceHandle == null || DeviceHandle.IsInvalid || DeviceHandle.IsClosed) return;
            foreach (var packet in PreallocatedPackets)
            {
                int keysInThisPacket = packet[2];
                int byteOffset = 3;
                for (int i = 0; i < keysInThisPacket; i++)
                {
                    packet[byteOffset + 1] = 0;
                    packet[byteOffset + 2] = 0;
                    packet[byteOffset + 3] = 0;
                    byteOffset += 4;
                }
                WriteFile(DeviceHandle, packet, (uint)packet.Length, out _, IntPtr.Zero);
            }
        }

        private static SafeFileHandle FindAndOpenStars80()
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x10);
            if (deviceInfoSet == INVALID_HANDLE_VALUE) return null;

            SP_DEVICE_INTERFACE_DATA interfaceData = new SP_DEVICE_INTERFACE_DATA();
            interfaceData.cbSize = Marshal.SizeOf(interfaceData);

            int index = 0;
            while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index++, ref interfaceData))
            {
                uint detailSize = 0;
                SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, ref detailSize, IntPtr.Zero);

                IntPtr detailBuffer = Marshal.AllocHGlobal((int)detailSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, (IntPtr.Size == 8) ? 8 : 5);

                    if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, detailSize, ref detailSize, IntPtr.Zero))
                    {
                        IntPtr pathPtr = new IntPtr(detailBuffer.ToInt64() + 4);
                        string devicePath = Marshal.PtrToStringUni(pathPtr);

                        // Modded to pass FILE_FLAG_OVERLAPPED to prevent driver thread locking issues
                        SafeFileHandle handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                        if (!handle.IsInvalid)
                        {
                            HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };

                            if (HidD_GetAttributes(handle, ref attributes) && attributes.VendorID == VendorId && attributes.ProductID == ProductId)
                            {
                                if (HidD_GetPreparsedData(handle, out IntPtr preparsedData))
                                {
                                    HIDP_CAPS caps;
                                    int status = HidP_GetCaps(preparsedData, out caps);
                                    HidD_FreePreparsedData(preparsedData);

                                    if (status == 0x00110000 && caps.UsagePage == UsagePage && caps.Usage == Usage)
                                    {
                                        SetupDiDestroyDeviceInfoList(deviceInfoSet);
                                        return handle;
                                    }
                                }
                            }
                            handle.Dispose(); 
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(detailBuffer); }
            }

            SetupDiDestroyDeviceInfoList(deviceInfoSet);
            return null;
        }


        #region Native Win32 API Imports

[System.Runtime.InteropServices.DllImport("hid.dll", SetLastError = true)]
private static extern void HidD_GetHidGuid(out Guid hidGuid);

[System.Runtime.InteropServices.DllImport("setupapi.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);

[System.Runtime.InteropServices.DllImport("setupapi.dll", SetLastError = true)]
private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

[System.Runtime.InteropServices.DllImport("setupapi.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, ref uint requiredSize, IntPtr deviceInfoData);

[System.Runtime.InteropServices.DllImport("setupapi.dll", SetLastError = true)]
private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

[System.Runtime.InteropServices.DllImport("hid.dll", SetLastError = true)]
private static extern bool HidD_GetAttributes(Microsoft.Win32.SafeHandles.SafeFileHandle hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

[System.Runtime.InteropServices.DllImport("hid.dll", SetLastError = true)]
private static extern bool HidD_GetPreparsedData(Microsoft.Win32.SafeHandles.SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

[System.Runtime.InteropServices.DllImport("hid.dll", SetLastError = true)]
private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

[System.Runtime.InteropServices.DllImport("hid.dll", SetLastError = true)]
private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
private static extern bool WriteFile(Microsoft.Win32.SafeHandles.SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
private struct SP_DEVICE_INTERFACE_DATA 
{ 
    public int cbSize; 
    public Guid interfaceClassGuid; 
    public int flags; 
    public IntPtr reserved; 
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
private struct HIDD_ATTRIBUTES 
{ 
    public int Size; 
    public ushort VendorID; 
    public ushort ProductID; 
    public ushort VersionNumber; 
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
private struct HIDP_CAPS 
{ 
    public ushort Usage; 
    public ushort UsagePage; 
    public ushort InputReportByteLength; 
    public ushort OutputReportByteLength; 
    public ushort FeatureReportByteLength; 
    
    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst = 17)] 
    public ushort[] Reserved; 
    
    public ushort NumberLinkCollectionNodes; 
    public ushort NumberInputButtonCaps; 
    public ushort NumberInputValueCaps; 
    public ushort NumberInputDataIndices; 
    public ushort NumberOutputButtonCaps; 
    public ushort NumberOutputValueCaps; 
    public ushort NumberOutputDataIndices; 
    public ushort NumberFeatureButtonCaps; 
    public ushort NumberFeatureValueCaps; 
    public ushort NumberFeatureDataIndices; 
}

#endregion
    }}
