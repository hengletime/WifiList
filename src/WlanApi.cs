using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WifiList
{
    /// <summary>
    /// The pieces of the native WLAN API this app needs.
    ///
    /// netsh cannot address a profile whose name starts with '#': its command
    /// parser treats the rest of the argument as a comment, so it looks for a
    /// profile called "" and — the nasty part — still exits 0. Delete, export
    /// and set-profileparameter therefore all "succeed" silently without doing
    /// anything. These calls hand the name to the service as a plain string and
    /// return a real error code.
    /// </summary>
    internal static class WlanApi
    {
        private const uint ClientVersion = 2;      // Windows Vista or later
        private const int ErrorSuccess = 0;
        private const int ErrorNotFound = 1168;    // ERROR_NOT_FOUND

        // WLAN_INTERFACE_INFO_LIST: dwNumberOfItems, dwIndex, then the array of
        // WLAN_INTERFACE_INFO = GUID + WCHAR[256] + enum state -> 532 bytes.
        private const int InterfaceListHeader = 8;
        private const int InterfaceInfoSize = 532;

        // WLAN_PROFILE_INFO_LIST: same header, then WLAN_PROFILE_INFO =
        // WCHAR[256] + DWORD flags -> 516 bytes.
        private const int ProfileListHeader = 8;
        private const int ProfileInfoSize = 516;

        private const int ConnectionModeProfile = 0;
        private const int BssTypeInfrastructure = 1;

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved,
            out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved,
            out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanGetProfileList(IntPtr hClientHandle, ref Guid pInterfaceGuid,
            IntPtr pReserved, out IntPtr ppProfileList);

        [DllImport("wlanapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint WlanDeleteProfile(IntPtr hClientHandle, ref Guid pInterfaceGuid,
            string strProfileName, IntPtr pReserved);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanConnect(IntPtr hClientHandle, ref Guid pInterfaceGuid,
            ref ConnectionParameters pConnectionParameters, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr pMemory);

        [StructLayout(LayoutKind.Sequential)]
        private struct Dot11Ssid
        {
            public uint Length;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] Bytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ConnectionParameters
        {
            public int Mode;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Profile;
            public IntPtr Ssid;
            public IntPtr BssidList;
            public int BssType;
            public int Flags;
        }

        // ------------------------------------------------------------------
        // plumbing
        // ------------------------------------------------------------------

        private static bool Open(out IntPtr handle)
        {
            handle = IntPtr.Zero;
            try
            {
                uint negotiated;
                return WlanOpenHandle(ClientVersion, IntPtr.Zero, out negotiated, out handle) == ErrorSuccess;
            }
            catch
            {
                return false;
            }
        }

        private static void Close(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try { WlanCloseHandle(handle, IntPtr.Zero); }
            catch { }
        }

        /// <summary>GUIDs of every wireless interface, Wi-Fi Direct included.</summary>
        private static List<Guid> Interfaces(IntPtr handle)
        {
            List<Guid> list = new List<Guid>();
            IntPtr buf;
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out buf) != ErrorSuccess || buf == IntPtr.Zero)
            {
                return list;
            }
            try
            {
                int count = Marshal.ReadInt32(buf, 0);
                long start = buf.ToInt64() + InterfaceListHeader;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = new IntPtr(start + i * InterfaceInfoSize);
                    list.Add((Guid)Marshal.PtrToStructure(item, typeof(Guid)));
                }
            }
            catch { }
            finally
            {
                try { WlanFreeMemory(buf); }
                catch { }
            }
            return list;
        }

        private static List<string> ProfilesOn(IntPtr handle, Guid iface)
        {
            List<string> names = new List<string>();
            Guid guid = iface;
            IntPtr buf;
            if (WlanGetProfileList(handle, ref guid, IntPtr.Zero, out buf) != ErrorSuccess || buf == IntPtr.Zero)
            {
                return names;
            }
            try
            {
                int count = Marshal.ReadInt32(buf, 0);
                long start = buf.ToInt64() + ProfileListHeader;
                for (int i = 0; i < count; i++)
                {
                    string name = Marshal.PtrToStringUni(new IntPtr(start + i * ProfileInfoSize));
                    if (name != null) names.Add(name);
                }
            }
            catch { }
            finally
            {
                try { WlanFreeMemory(buf); }
                catch { }
            }
            return names;
        }

        // ------------------------------------------------------------------
        // public surface
        // ------------------------------------------------------------------

        /// <summary>
        /// Every saved profile name, across all interfaces. Returns false when
        /// the WLAN service cannot be reached at all, so callers can tell
        /// "no profiles" from "no answer".
        /// </summary>
        public static bool TryProfileNames(out List<string> names)
        {
            names = new List<string>();
            IntPtr handle;
            if (!Open(out handle)) return false;
            try
            {
                List<Guid> ifaces = Interfaces(handle);
                if (ifaces.Count == 0) return false;
                for (int i = 0; i < ifaces.Count; i++)
                {
                    List<string> on = ProfilesOn(handle, ifaces[i]);
                    for (int j = 0; j < on.Count; j++)
                    {
                        if (!names.Contains(on[j])) names.Add(on[j]);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                Close(handle);
            }
        }

        /// <summary>Deletes a profile from every interface that carries it.</summary>
        public static NetshResult Delete(string ssid)
        {
            NetshResult result = new NetshResult();
            IntPtr handle;
            if (!Open(out handle))
            {
                result.Code = -1;
                result.Text = L.T("w.errNoWlan");
                return result;
            }
            try
            {
                List<Guid> ifaces = Interfaces(handle);
                if (ifaces.Count == 0)
                {
                    result.Code = -1;
                    result.Text = L.T("w.errNoWlan");
                    return result;
                }

                int firstError = 0;
                bool deleted = false;
                for (int i = 0; i < ifaces.Count; i++)
                {
                    Guid guid = ifaces[i];
                    uint rc = WlanDeleteProfile(handle, ref guid, ssid, IntPtr.Zero);
                    if (rc == ErrorSuccess) deleted = true;
                    else if ((int)rc != ErrorNotFound && firstError == 0) firstError = (int)rc;
                }

                if (deleted)
                {
                    result.Code = 0;
                    result.Text = L.F("w.apiDeleted", ssid);
                }
                else
                {
                    result.Code = firstError == 0 ? 2 : firstError;
                    result.Text = L.F("w.apiDeleteFail", ssid,
                        firstError == 0 ? "ERROR_NOT_FOUND" : "error " + firstError.ToString());
                }
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Text = ex.Message;
            }
            finally
            {
                Close(handle);
            }
            return result;
        }

        /// <summary>
        /// Associates with a saved profile. Used only for names netsh cannot
        /// express; the interface is picked by asking each one which profiles
        /// it holds, which also keeps the Wi-Fi Direct adapter out of the way.
        /// </summary>
        public static NetshResult Connect(string ssid)
        {
            NetshResult result = new NetshResult();
            byte[] raw = System.Text.Encoding.UTF8.GetBytes(ssid);
            if (raw.Length > 32)
            {
                result.Code = -1;
                result.Text = L.T("d.errSsidLong");
                return result;
            }

            IntPtr handle;
            if (!Open(out handle))
            {
                result.Code = -1;
                result.Text = L.T("w.errNoWlan");
                return result;
            }

            IntPtr ssidPtr = IntPtr.Zero;
            try
            {
                Dot11Ssid dot = new Dot11Ssid();
                dot.Bytes = new byte[32];
                dot.Length = (uint)raw.Length;
                Array.Copy(raw, dot.Bytes, raw.Length);
                ssidPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Dot11Ssid)));
                Marshal.StructureToPtr(dot, ssidPtr, false);

                List<Guid> ifaces = Interfaces(handle);
                int firstError = -1;
                for (int i = 0; i < ifaces.Count; i++)
                {
                    List<string> on = ProfilesOn(handle, ifaces[i]);
                    if (!on.Contains(ssid)) continue;

                    ConnectionParameters cp = new ConnectionParameters();
                    cp.Mode = ConnectionModeProfile;
                    cp.Profile = ssid;
                    cp.Ssid = ssidPtr;
                    cp.BssidList = IntPtr.Zero;
                    cp.BssType = BssTypeInfrastructure;
                    cp.Flags = 0;

                    Guid guid = ifaces[i];
                    uint rc = WlanConnect(handle, ref guid, ref cp, IntPtr.Zero);
                    if (rc == ErrorSuccess)
                    {
                        result.Code = 0;
                        result.Text = L.F("w.apiConnected", ssid);
                        return result;
                    }
                    if (firstError < 0) firstError = (int)rc;
                }

                result.Code = firstError < 0 ? 2 : firstError;
                result.Text = firstError < 0
                    ? L.F("w.apiConnectMissing", ssid)
                    : L.F("w.apiConnectFail", ssid, "error " + firstError.ToString());
            }
            catch (Exception ex)
            {
                result.Code = -1;
                result.Text = ex.Message;
            }
            finally
            {
                if (ssidPtr != IntPtr.Zero)
                {
                    try { Marshal.FreeHGlobal(ssidPtr); }
                    catch { }
                }
                Close(handle);
            }
            return result;
        }
    }
}
