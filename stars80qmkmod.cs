using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace stars80qmkmod
{
    public class Stars80ModSystem : ModSystem
    {
        // HID Device Configuration
        private const ushort VendorId = 0x342D;
        private const ushort ProductId = 0xE401;
        private const ushort UsagePage = 0xFF60;
        private const ushort Usage = 0x61;
        private const int ReportLength = 32;

        // Win32 File Constants
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // 6 Rows x 17 Columns Physical Matrix Map
        private static readonly int[][] LayoutMap = new int[][]
        {
            new int[] {0,  1,  2,  3,  4,  5,  6,  7,  8,  9,  10, 11, 12, 13, 14, 15, 16},
            new int[] {33, 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17},
            new int[] {34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50},
            new int[] {64, 63, 62, 61, 60, 59, 58, 57, 56, 55, 54, 53, 52, 51, 49, 49, 50},
            new int[] {65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 76, 77, 77, 77, 77},
            new int[] {85, 85, 85, 84, 83, 83, 83, 83, 83, 82, 81, 80, 80, 79, 78, 78, 78}
        };

        private Microsoft.Win32.SafeHandles.SafeFileHandle _deviceHandle = null;
        private int _lastLedIdx = -1;
        private int _updateTimer = 0;

        public override void OnModLoad()
        {
            // Only run on Windows client machines
            if (Main.dedServ || !OperatingSystem.IsWindows()) return;

            _deviceHandle = FindAndOpenStars80();
            if (_deviceHandle != null && !_deviceHandle.IsInvalid)
            {
                Mod.Logger.Info("Stars80 Keyboard connected via native Win32 API.");
            }
            else
            {
                Mod.Logger.Warn("Stars80 Keyboard could not be found.");
            }
        }

public override void PostUpdateEverything()
{
    if (_deviceHandle == null || _deviceHandle.IsInvalid || Main.dedServ || !OperatingSystem.IsWindows()) 
        return;

    // OPTIONAL: If the Terraria game window is not active, turn off the last active light and skip tracking
    if (!Main.instance.IsActive)
    {
        if (_lastLedIdx != -1)
        {
            byte[] clearPayload = new byte[32];
            clearPayload[0] = 0x02;
            clearPayload[1] = 1;
            clearPayload[2] = (byte)_lastLedIdx;
            SendRawReport(clearPayload);
            _lastLedIdx = -1;
        }
        return;
    }
    // Throttle processing to every 2 game ticks (~30Hz) to prevent stuttering
    _updateTimer++;
    if (_updateTimer % 2 != 0) return;

    // 1. Get GLOBAL desktop cursor coordinates instead of local game view coordinates
    POINT point;
    if (!GetCursorPos(out point)) return;

    // Use your absolute desktop resolution targets (matching the Python script)
    const int targetWidth = 1920;
    const int targetHeight = 1080;

    int mx = Math.Clamp(point.X, 0, targetWidth - 1);
    int my = Math.Clamp(point.Y, 0, targetHeight - 1);

    // 2. Map coordinates precisely to Grid Row (6) and Col (17) bounds
    int col = (int)(((double)mx / targetWidth) * 17);
    int row = (int)(((double)my / targetHeight) * 6);

    col = Math.Clamp(col, 0, 16);
    row = Math.Clamp(row, 0, 5);

    int ledIdx = LayoutMap[row][col];

    // 3. Generate reactive colors based on vertical depth
    int r = (int)((1.0 - ((double)my / targetHeight)) * 255);
    int g = 255;
    int b = (int)(((double)my / targetHeight) * 255);

    // 4. Send USB reports on coordinate index changes
    if (ledIdx != _lastLedIdx)
    {
        if (_lastLedIdx != -1)
        {
            byte[] clearPayload = new byte[32];
            clearPayload[0] = 0x02; 
            clearPayload[1] = 1;    
            clearPayload[2] = (byte)_lastLedIdx;
            SendRawReport(clearPayload);
        }

        byte[] payload = new byte[32];
        payload[0] = 0x02; 
        payload[1] = 1;    
        payload[2] = (byte)ledIdx;
        payload[3] = (byte)r;
        payload[4] = (byte)g;
        payload[5] = (byte)b;
        SendRawReport(payload);

        _lastLedIdx = ledIdx;
    }
}

public override void OnModUnload()
{
    if (_deviceHandle != null && !_deviceHandle.IsInvalid && !_deviceHandle.IsClosed)
    {
        // 1. Loop through all 6 rows and 17 columns to find and turn off every active index
        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < 17; col++)
            {
                int targetIdx = LayoutMap[row][col];
                
                byte[] clearPayload = new byte[32];
                clearPayload[0] = 0x02;        // Command: Batch Update
                clearPayload[1] = 1;           // Total keys in packet
                clearPayload[2] = (byte)targetIdx; // Targeting this key index
                clearPayload[3] = 0;           // Reset R
                clearPayload[4] = 0;           // Reset G
                clearPayload[5] = 0;           // Reset B
                
                SendRawReport(clearPayload);
            }
        }

        // 2. Safely release the native file handle structure
        _deviceHandle.Dispose();
    }
}


        private void SendRawReport(byte[] data)
{
    // Ensure the handle is fully active before writing
    if (_deviceHandle == null || _deviceHandle.IsInvalid || _deviceHandle.IsClosed)
        return;

    byte[] writeBuffer = new byte[ReportLength + 1];
    Array.Copy(data, 0, writeBuffer, 1, Math.Min(data.Length, ReportLength));

    // Send directly to the Windows OS kernel without using FileStream wrappers
    uint bytesWritten;
    WriteFile(_deviceHandle, writeBuffer, (uint)writeBuffer.Length, out bytesWritten, IntPtr.Zero);
}

        private static Microsoft.Win32.SafeHandles.SafeFileHandle FindAndOpenStars80()
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x10); // DIGCF_DEVICEINTERFACE | DIGCF_PRESENT
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
                    Marshal.WriteInt32(detailBuffer, (IntPtr.Size == 8) ? 8 : 5); // cbSize configuration setup for structural pointer architecture

                    if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, detailSize, ref detailSize, IntPtr.Zero))
                    {
                        IntPtr pathPtr = new IntPtr(detailBuffer.ToInt64() + 4);
                        string devicePath = Marshal.PtrToStringUni(pathPtr);

                        Microsoft.Win32.SafeHandles.SafeFileHandle handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
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

                                        // Ensure we match the Raw HID functional endpoints layout usage settings target profile rules
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
		[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
private static extern bool WriteFile(
    Microsoft.Win32.SafeHandles.SafeFileHandle hFile, 
    byte[] lpBuffer, 
    uint nNumberOfBytesToWrite, 
    out uint lpNumberOfBytesWritten, 
    IntPtr lpOverlapped
);

[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
private static extern bool GetCursorPos(out POINT lpPoint);
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
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
private struct POINT 
{ 
    public int X; 
    public int Y; 
}
#endregion
	}
}