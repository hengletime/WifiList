using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

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

        // WLAN_AVAILABLE_NETWORK_LIST: header, then WLAN_AVAILABLE_NETWORK =
        // WCHAR strProfileName[256] (512) + DOT11_SSID (36) + bssType (4) +
        // uNumberOfBssids (4) + bNetworkConnectable (4) +
        // wlanNotConnectableReason (4) + uNumberOfPhyTypes (4) +
        // dot11PhyTypes[8] (32) + bMorePhyTypes (4) + wlanSignalQuality (4) +
        // bSecurityEnabled (4) + auth (4) + cipher (4) + dwFlags (4) +
        // dwReserved (4) -> 628 bytes. Offsets are spelled out because the
        // quality field sits far enough in that guessing hurts.
        private const int AvailableListHeader = 8;
        private const int AvailableNetworkSize = 628;
        private const int AvailableSsidLengthAt = 512;
        private const int AvailableSsidAt = 516;
        private const int AvailableSignalAt = 604;

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

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanGetAvailableNetworkList(IntPtr hClientHandle,
            ref Guid pInterfaceGuid, uint dwFlags, IntPtr pReserved,
            out IntPtr ppAvailableNetworkList);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanScan(IntPtr hClientHandle, ref Guid pInterfaceGuid,
            IntPtr pDot11Ssid, IntPtr pIeData, IntPtr pReserved);

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
        /// <summary>
        /// Signal quality (0-100) for every SSID the radio can currently hear.
        ///
        /// The value comes from the WLAN service rather than from parsing
        /// "netsh wlan show networks", because that output is localised (the
        /// field is 信号 on this machine, Signal elsewhere) and its percent
        /// figures are printed per BSSID, which then needs block parsing. The
        /// API answers with one number per network in every locale.
        ///
        /// Networks with several access points keep their strongest value.
        /// Hidden SSIDs (zero length) are skipped: they cannot be matched to a
        /// saved profile by name. Returns false when nothing usable came back,
        /// so the caller can fall back to "no signal known".
        /// </summary>
        public static bool TryAvailableSignals(out Dictionary<string, int> signals)
        {
            signals = new Dictionary<string, int>(StringComparer.Ordinal);
            IntPtr handle;
            if (!Open(out handle)) return false;

            try
            {
                List<Guid> ifaces = Interfaces(handle);
                if (ifaces.Count == 0) return false;

                bool any = false;
                for (int i = 0; i < ifaces.Count; i++)
                {
                    Guid guid = ifaces[i];
                    IntPtr buf;
                    uint rc = WlanGetAvailableNetworkList(handle, ref guid, 0, IntPtr.Zero, out buf);
                    if (rc != ErrorSuccess || buf == IntPtr.Zero) continue;

                    try
                    {
                        int count = Marshal.ReadInt32(buf, 0);
                        if (count <= 0 || count > 512) continue;   // guard a bad header

                        long start = buf.ToInt64() + AvailableListHeader;
                        for (int j = 0; j < count; j++)
                        {
                            long p = start + (long)j * AvailableNetworkSize;

                            int len = Marshal.ReadInt32(new IntPtr(p + AvailableSsidLengthAt));
                            if (len <= 0 || len > 32) continue;    // hidden network

                            byte[] raw = new byte[len];
                            Marshal.Copy(new IntPtr(p + AvailableSsidAt), raw, 0, len);
                            string ssid = DecodeSsid(raw);
                            if (ssid.Length == 0) continue;

                            int quality = Marshal.ReadInt32(new IntPtr(p + AvailableSignalAt));
                            if (quality < 0) quality = 0;
                            if (quality > 100) quality = 100;

                            int seen;
                            if (signals.TryGetValue(ssid, out seen))
                            {
                                if (quality > seen) signals[ssid] = quality;
                            }
                            else
                            {
                                signals[ssid] = quality;
                            }
                            any = true;
                        }
                    }
                    catch { }
                    finally
                    {
                        try { WlanFreeMemory(buf); }
                        catch { }
                    }
                }
                return any;
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

        /// <summary>
        /// Asks the radio to rescan in the background. The call returns at once;
        /// results show up in the next WlanGetAvailableNetworkList. Used before a
        /// refresh so the signal column is not showing a stale neighbourhood.
        /// </summary>
        public static void Scan()
        {
            IntPtr handle;
            if (!Open(out handle)) return;
            try
            {
                List<Guid> ifaces = Interfaces(handle);
                for (int i = 0; i < ifaces.Count; i++)
                {
                    Guid guid = ifaces[i];
                    try { WlanScan(handle, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); }
                    catch { }
                }
            }
            catch { }
            finally
            {
                Close(handle);
            }
        }

        /// <summary>
        /// SSID bytes are whatever the access point sent. Current gear uses
        /// UTF-8, older Chinese hotspots send GBK; a strict UTF-8 attempt tells
        /// them apart because GBK bytes are almost never valid UTF-8.
        /// </summary>
        private static string DecodeSsid(byte[] raw)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(raw);
            }
            catch
            {
                try { return Encoding.Default.GetString(raw); }
                catch { return ""; }
            }
        }
    }
}
