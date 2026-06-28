using System;
using System.Runtime.InteropServices;
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
        private const int ReportLength = 32;

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        public static readonly int[][] LayoutMap = new int[][]
        {
            new int[] {0,  1,  2,  3,  4,  5,  6,  7,  8,  9,  10, 11, 12, 13, 14, 15, 16},
            new int[] {33, 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17},
            new int[] {34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50},
            new int[] {64, 63, 62, 61, 60, 59, 58, 57, 56, 55, 54, 53, 52, 51, 49, 49, 50},
            new int[] {65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 76, 77, 77, 77, 77},
            new int[] {85, 85, 85, 84, 83, 83, 83, 83, 83, 82, 81, 80, 80, 79, 78, 78, 78}
        };

        public static SafeFileHandle DeviceHandle = null;

        public override void OnModLoad()
        {
            if (Main.dedServ || !OperatingSystem.IsWindows()) return;
            DeviceHandle = FindAndOpenStars80();
        }

        public override void OnModUnload()
        {
            if (DeviceHandle != null && !DeviceHandle.IsInvalid && !DeviceHandle.IsClosed)
            {
                ClearAllKeys();
                System.Threading.Thread.Sleep(5); 
                DeviceHandle.Dispose();
            }
        }

public static void SetKeyColor(int ledIndex, byte r, byte g, byte b)
{
    if (DeviceHandle == null || DeviceHandle.IsInvalid || DeviceHandle.IsClosed) return;

    // FIX: Expand array length to 33 bytes. Index 0 remains 0x00 (The Windows HID Report ID)
    byte[] payload = new byte[33]; 
    payload[1] = 0x02;        // Command shifted to Index 1: Batch Update
    payload[2] = 1;           // Shifted to Index 2: Total keys inside packet
    payload[3] = (byte)ledIndex; // Shifted to Index 3: Targeted Key Index
    payload[4] = g;           // Shifted to Index 4: Set R
    payload[5] = r;           // Shifted to Index 5: Set G
    payload[6] = b;           // Shifted to Index 6: Set B

    uint bytesWritten;
    WriteFile(DeviceHandle, payload, (uint)payload.Length, out bytesWritten, IntPtr.Zero);
}


public static void ClearAllKeys()
{
    // Simply fire the batch loop with zeros to instantly clear the whole board
    SetEntireKeyboardColor(0, 0, 0);
}

public static void SetEntireKeyboardColor(byte r, byte g, byte b)
{
    if (DeviceHandle == null || DeviceHandle.IsInvalid || DeviceHandle.IsClosed) return;

    // Flatten our 2D Layout Map into a clean list of all unique keys
    System.Collections.Generic.List<int> allKeys = new System.Collections.Generic.List<int>();
    for (int row = 0; row < 6; row++)
    {
        for (int col = 0; col < 17; col++)
        {
            allKeys.Add(LayoutMap[row][col]);
        }
    }

    // Process keys in batches of 6 keys per USB report packet
    int totalKeys = allKeys.Count;
    int index = 0;

    while (index < totalKeys)
    {
        // 33-byte buffer. Index 0 is 0x00 (Windows HID Report ID)
        byte[] payload = new byte[33];
        payload[1] = 0x02; // Command: Batch Update

        // Calculate how many keys are left for this specific packet (max 6)
        int keysInThisPacket = Math.Min(6, totalKeys - index);
        payload[2] = (byte)keysInThisPacket;

        int byteOffset = 3;
        for (int i = 0; i < keysInThisPacket; i++)
        {
            payload[byteOffset] = (byte)allKeys[index++]; // Key Index location
            payload[byteOffset + 1] = g;                  // R
            payload[byteOffset + 2] = r;                  // G
            payload[byteOffset + 3] = b;                  // B
            byteOffset += 4; // Move to next key segment inside the packet
        }

        uint bytesWritten;
        WriteFile(DeviceHandle, payload, (uint)payload.Length, out bytesWritten, IntPtr.Zero);
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

                        SafeFileHandle handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                        if (!handle.IsInvalid)
                        {
                            HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES();
                            attributes.Size = Marshal.SizeOf(attributes);

                            if (HidD_GetAttributes(handle, ref attributes))
                            {
                                if (attributes.VendorID == VendorId && attributes.ProductID == ProductId)
                                {
                                    IntPtr preparsedData;
                                    if (HidD_GetPreparsedData(handle, out preparsedData))
                                    {
                                        HIDP_CAPS caps;
                                        HidP_GetCaps(preparsedData, out caps);
                                        HidD_FreePreparsedData(preparsedData);

                                        if (caps.UsagePage == UsagePage && caps.Usage == Usage)
                                        {
                                            SetupDiDestroyDeviceInfoList(deviceInfoSet);
                                            return handle;
                                        }
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
