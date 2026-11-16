using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace WifiList
{
    /// <summary>Interface languages. The numeric value is the column used in the string table.</summary>
    internal enum UiLang
    {
        Zh = 0,
        En = 1,
        Th = 2
    }

    /// <summary>
    /// Three column string table. Every visible string lives here so a language
    /// switch is a single dictionary lookup instead of a code change. Values are
    /// keyed by short dotted names ("grid.name"); a missing key falls back to the
    /// key itself, which makes omissions obvious during testing.
    /// </summary>
    internal static class L
    {
        public static UiLang Current = UiLang.Zh;

        private static readonly Dictionary<string, string[]> Map =
            new Dictionary<string, string[]>(StringComparer.Ordinal);

        private static void A(string key, string zh, string en, string th)
        {
            Map[key] = new string[] { zh, en, th };
        }

        /// <summary>Looks a key up in the active language, then falls back to Chinese.</summary>
        public static string T(string key)
        {
            string[] v;
            if (!Map.TryGetValue(key, out v)) return key;
            string s = v[(int)Current];
            if (s != null && s.Length > 0) return s;
            return v[0];
        }

        /// <summary>Formatted lookup: L.F("status.count", 12, 5).</summary>
        public static string F(string key, params object[] args)
        {
            try
            {
                return string.Format(CultureInfo.CurrentCulture, T(key), args);
            }
            catch
            {
                return T(key);
            }
        }

        // ---- persistence -------------------------------------------------

        private static string File_()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WifiList");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "lang.cfg");
            }
            catch
            {
                return null;
            }
        }

        public static void Load()
        {
            try
            {
                string f = File_();
                if (f == null || !System.IO.File.Exists(f)) return;
                string s = System.IO.File.ReadAllText(f).Trim().ToLowerInvariant();
                if (s == "en") Current = UiLang.En;
                else if (s == "th") Current = UiLang.Th;
                else Current = UiLang.Zh;
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                string f = File_();
                if (f == null) return;
                string s = Current == UiLang.En ? "en" : (Current == UiLang.Th ? "th" : "zh");
                System.IO.File.WriteAllText(f, s);
            }
            catch { }
        }

        /// <summary>Name of a language as it names itself.</summary>
        public static string NativeName(UiLang lang)
        {
            if (lang == UiLang.En) return "English";
            if (lang == UiLang.Th) return "ไทย";
            return "简体中文";
        }

        // ---- table -------------------------------------------------------

        static L()
        {
            Build();
        }

        private static void Build()
        {
            // ---- application shell ----
            A("app.title", "WiFi 密码清单", "WiFi Passwords", "รหัสผ่าน WiFi");
            A("app.sub", "v{0}  ·  {1}  ·  {2} 个已保存网络  ·  {3}",
                "v{0}  ·  {1}  ·  {2} saved networks  ·  {3}",
                "v{0}  ·  {1}  ·  บันทึกไว้ {2} เครือข่าย  ·  {3}");

            A("tip.refresh", "重新读取（F5）", "Reload (F5)", "โหลดใหม่ (F5)");
            A("tip.themeDark", "切换到深色主题", "Switch to dark theme", "เปลี่ยนเป็นธีมมืด");
            A("tip.themeLight", "切换到浅色主题", "Switch to light theme", "เปลี่ยนเป็นธีมสว่าง");
            A("tip.min", "最小化", "Minimize", "ย่อหน้าต่าง");
            A("tip.close", "关闭", "Close", "ปิด");
            A("tip.add", "添加一个 WiFi 配置", "Add a Wi-Fi profile", "เพิ่มโปรไฟล์ Wi-Fi");
            A("tip.delete", "删除所选网络的已保存配置", "Delete the saved profile of the selected row",
                "ลบโปรไฟล์ที่บันทึกไว้ของรายการที่เลือก");
            A("tip.clear", "清空搜索内容", "Clear the search box", "ล้างคำค้นหา");
            A("tip.lang", "切换语言 / Language / ภาษา", "Language", "เปลี่ยนภาษา");
            A("tip.about", "关于本程序", "About this app", "เกี่ยวกับโปรแกรมนี้");

            // ---- toolbar ----
            A("btn.add", "添加 WiFi", "Add Wi-Fi", "เพิ่ม Wi-Fi");
            A("btn.delete", "删除", "Delete", "ลบ");
            A("btn.showAll", "显示全部密码", "Show all", "แสดงทั้งหมด");
            A("btn.hideAll", "隐藏全部密码", "Hide all", "ซ่อนทั้งหมด");
            A("btn.export", "导出 HTML 报告", "Export HTML", "ส่งออกรายงาน HTML");
            A("chip.onlyKey", "只看有密码", "With password", "เฉพาะมีรหัสผ่าน");
            A("ph.search", "搜索 WiFi 名称…", "Search Wi-Fi name…", "ค้นหาชื่อ Wi-Fi…");
            A("hint.bar", "双击显示密码 · 右键更多 · F5 刷新",
                "Double-click to reveal · right-click for more · F5 to reload",
                "ดับเบิลคลิกเพื่อดูรหัสผ่าน · คลิกขวาเพื่อตัวเลือก · F5 โหลดใหม่");

            // ---- grid ----
            A("grid.name", "网络名称", "Network", "ชื่อเครือข่าย");
            A("grid.security", "安全类型", "Security", "ความปลอดภัย");
            A("grid.mode", "连接方式", "Connect", "โหมดเชื่อมต่อ");
            A("grid.key", "密码", "Password", "รหัสผ่าน");
            A("cell.noKey", "无密码", "No password", "ไม่มีรหัสผ่าน");
            A("cell.notSaved", "（未保存明文）", "(not stored in clear)", "(ไม่ได้เก็บแบบข้อความธรรมดา)");

            // ---- status line ----
            A("status.loading", "正在读取已保存的 WiFi 配置…", "Reading saved Wi-Fi profiles…",
                "กำลังอ่านโปรไฟล์ WiFi ที่บันทึกไว้…");
            A("status.count", "共 {0} 个网络 · 当前显示 {1} 个", "{0} networks · showing {1}",
                "ทั้งหมด {0} เครือข่าย · แสดง {1}");
            A("status.empty", "没有读取到任何已保存的 WiFi 配置。",
                "No saved Wi-Fi profile was found.", "ไม่พบโปรไฟล์ WiFi ที่บันทึกไว้");
            A("status.readFail", "读取失败", "Read failed", "อ่านข้อมูลไม่สำเร็จ");
            A("status.copied", "已复制到剪贴板", "Copied to the clipboard", "คัดลอกไปยังคลิปบอร์ดแล้ว");
            A("status.copyFail", "复制失败，剪贴板被其他程序占用",
                "Copy failed: the clipboard is locked by another app",
                "คัดลอกไม่สำเร็จ คลิปบอร์ดถูกโปรแกรมอื่นใช้งานอยู่");
            A("status.exported", "已导出：{0}", "Exported: {0}", "ส่งออกแล้ว: {0}");
            A("status.connected", "已连接到{0}", "Connected to {0}", "เชื่อมต่อกับ {0} แล้ว");
            A("status.connecting", "正在连接{0}…", "Connecting to {0}…", "กำลังเชื่อมต่อ {0}…");
            A("status.retrying", "正在重试{0}…", "Retrying {0}…", "กำลังลองใหม่ {0}…");
            A("status.retryDrop", "正在断开旧连接后重试…", "Dropping the old link and retrying…",
                "กำลังตัดการเชื่อมต่อเดิมแล้วลองใหม่…");
            A("status.waitConnect", "正在连接{0}… {1}s", "Connecting to {0}… {1}s", "กำลังเชื่อมต่อ {0}… {1}s");
            A("status.waitDisconnect", "正在断开… {0}s", "Disconnecting… {0}s", "กำลังตัดการเชื่อมต่อ… {0}s");
            A("status.disconnecting", "正在断开…", "Disconnecting…", "กำลังตัดการเชื่อมต่อ…");
            A("status.disconnected", "已断开无线连接", "Wireless connection closed", "ตัดการเชื่อมต่อไร้สายแล้ว");
            A("status.saving", "正在保存{0}…", "Saving {0}…", "กำลังบันทึก {0}…");
            A("status.saved", "已保存{0}的配置", "Saved the profile of {0}", "บันทึกโปรไฟล์ {0} แล้ว");
            A("status.renamed", "已重命名为{0}", "Renamed to {0}", "เปลี่ยนชื่อเป็น {0} แล้ว");
            A("status.savedLive", "（当前连接可能已受影响，可右键重新连接）",
                " (the current link may have dropped; right-click to reconnect)",
                " (การเชื่อมต่อปัจจุบันอาจหลุด คลิกขวาเพื่อเชื่อมต่อใหม่)");
            A("status.added", "已添加{0}", "{0} added", "เพิ่ม {0} แล้ว");
            A("status.addedOk", "，已连接", ", connected", " เชื่อมต่อแล้ว");
            A("status.addedNo", "，暂未连上（可右键再试一次）", ", not connected yet (right-click to retry)",
                " ยังไม่เชื่อมต่อ (คลิกขวาเพื่อลองใหม่)");
            A("status.modeSet", "{0}已设为{1}", "{0} is now set to {1}", "ตั้ง {0} เป็น{1} แล้ว");
            A("status.deleted", "已删除{0}的配置", "Deleted the profile of {0}", "ลบโปรไฟล์ {0} แล้ว");

            // ---- context menu ----
            A("menu.connect", "连接到此网络", "Connect", "เชื่อมต่อเครือข่ายนี้");
            A("menu.disconnect", "断开当前连接", "Disconnect", "ตัดการเชื่อมต่อ");
            A("menu.copy", "复制", "Copy", "คัดลอก");
            A("menu.copyName", "网络名称", "Network name", "ชื่อเครือข่าย");
            A("menu.copyKey", "密码", "Password", "รหัสผ่าน");
            A("menu.showKey", "显示密码", "Show password", "แสดงรหัสผ่าน");
            A("menu.hideKey", "隐藏密码", "Hide password", "ซ่อนรหัสผ่าน");
            A("menu.config", "配置", "Configure", "ตั้งค่า");
            A("menu.edit", "编辑此网络…", "Edit this network…", "แก้ไขเครือข่ายนี้…");
            A("menu.editNo", "编辑此网络…（企业级请用系统设置）",
                "Edit this network… (use Windows Settings for enterprise)",
                "แก้ไขเครือข่ายนี้… (เครือข่ายองค์กรให้ใช้การตั้งค่า Windows)");
            A("menu.auto", "设为自动连接", "Connect automatically", "เชื่อมต่ออัตโนมัติ");
            A("menu.manual", "设为手动连接", "Connect manually", "เชื่อมต่อด้วยตนเอง");
            A("menu.delete", "删除此网络的配置", "Delete this profile", "ลบโปรไฟล์นี้");
            A("menu.refresh", "刷新列表", "Reload list", "โหลดรายการใหม่");

            // ---- message boxes ----
            A("btn.ok", "知道了", "OK", "ตกลง");
            A("btn.confirm", "确定", "OK", "ตกลง");
            A("btn.cancel", "取消", "Cancel", "ยกเลิก");
            A("btn.overwrite", "覆盖", "Overwrite", "เขียนทับ");
            A("netsh.noDetail", "（netsh 没有返回详细信息）",
                "(netsh returned no detail)", "(netsh ไม่ได้ให้รายละเอียด)");

            A("dlg.connectFail", "连接未成功", "Connection failed", "เชื่อมต่อไม่สำเร็จ");
            A("dlg.connectReasons",
                "可能原因：\r\n· 该网络不在信号范围内，或信号太弱\r\n· 密码已被修改\r\n· 需要网页认证或 802.1X 登录",
                "Possible reasons:\r\n· the network is out of range or too weak\r\n· the password was changed\r\n· it needs a captive portal or 802.1X sign-in",
                "สาเหตุที่เป็นไปได้:\r\n· เครือข่ายอยู่นอกระยะหรือสัญญาณอ่อนเกินไป\r\n· รหัสผ่านถูกเปลี่ยน\r\n· ต้องเข้าสู่ระบบผ่านหน้าเว็บหรือ 802.1X");
            A("dlg.connectTail",
                "已自动重试一次。若仍失败，可在系统 Wi-Fi 面板里连一次，确认热点本身可用；也可以先点「刷新」确认列表是最新的。",
                "Retried once automatically. If it still fails, try connecting from the Windows Wi-Fi flyout to confirm the hotspot works, and press Refresh to make sure the list is current.",
                "ลองใหม่ให้อัตโนมัติแล้วหนึ่งครั้ง หากยังไม่สำเร็จ ลองเชื่อมต่อจากแผง Wi-Fi ของ Windows เพื่อยืนยันว่าฮอตสปอตใช้งานได้ แล้วกดรีเฟรชเพื่อให้รายการเป็นปัจจุบัน");
            A("dlg.retCode", "系统返回码：{0}",
                "System return code: {0}", "รหัสตอบกลับจากระบบ: {0}");
            A("dlg.disconnectFail", "断开失败", "Disconnect failed", "ตัดการเชื่อมต่อไม่สำเร็จ");
            A("dlg.disconnectTip", "可以先在系统托盘的网络菜单里手动断开。",
                "You can disconnect manually from the network menu in the system tray.",
                "ลองตัดการเชื่อมต่อเองได้จากเมนูเครือข่ายในถาดระบบ");
            A("dlg.modeFail", "设置未生效", "Setting not applied", "ตั้งค่าไม่สำเร็จ");
            A("dlg.modeFailTip", "若提示权限不足，请以管理员身份运行后重试。",
                "If it mentions insufficient rights, run the app as administrator and try again.",
                "หากแจ้งว่าไม่มีสิทธิ์ ให้รันโปรแกรมในฐานะผู้ดูแลระบบแล้วลองใหม่");
            A("dlg.saveFail", "保存失败", "Save failed", "บันทึกไม่สำเร็จ");
            A("dlg.saveReasons",
                "常见原因：\r\n· 密码不符合所选安全类型的要求\r\n· 配置文件正被系统占用\r\n· 需要以管理员身份运行本程序",
                "Common causes:\r\n· the password does not match the chosen security type\r\n· the profile is locked by the system\r\n· the app needs to run as administrator",
                "สาเหตุที่พบบ่อย:\r\n· รหัสผ่านไม่ตรงกับประเภทความปลอดภัยที่เลือก\r\n· โปรไฟล์ถูกระบบล็อกอยู่\r\n· ต้องรันโปรแกรมในฐานะผู้ดูแลระบบ");
            A("dlg.rolledBack", "已自动还原为修改前的配置。",
                "The profile was restored to its previous state.",
                "คืนค่าโปรไฟล์กลับเป็นค่าเดิมแล้ว");
            A("dlg.rollbackFail", "原配置的回滚也失败了，请用「添加 WiFi」重新录入。",
                "Restoring the old profile failed too; add it again with Add Wi-Fi.",
                "การคืนค่าโปรไฟล์เดิมก็ล้มเหลว ให้เพิ่มใหม่ด้วย «เพิ่ม Wi-Fi»");
            A("dlg.addFail", "添加失败", "Add failed", "เพิ่มไม่สำเร็จ");
            A("dlg.addReasons",
                "常见原因：\r\n· 名称或密码不符合该安全类型的要求\r\n· 无线网卡被禁用\r\n· 需要以管理员身份运行本程序",
                "Common causes:\r\n· the name or password does not match the security type\r\n· the wireless adapter is disabled\r\n· the app needs to run as administrator",
                "สาเหตุที่พบบ่อย:\r\n· ชื่อหรือรหัสผ่านไม่ตรงกับประเภทความปลอดภัย\r\n· อะแดปเตอร์ไร้สายถูกปิดใช้งาน\r\n· ต้องรันโปรแกรมในฐานะผู้ดูแลระบบ");
            A("dlg.deleteFail", "删除失败", "Delete failed", "ลบไม่สำเร็จ");
            A("dlg.deleteFailTip", "若提示权限不足，请右键 WifiList.exe 以管理员身份运行后重试。",
                "If it mentions insufficient rights, right-click WifiList.exe and run as administrator.",
                "หากแจ้งว่าไม่มีสิทธิ์ ให้คลิกขวา WifiList.exe แล้วรันในฐานะผู้ดูแลระบบ");
            A("dlg.deleteTitle", "删除 WiFi", "Delete Wi-Fi", "ลบ Wi-Fi");
            A("dlg.deleteAsk",
                "确定要从这台电脑删除{0}的已保存配置吗？\r\n\r\n删除后，再次连接需要重新输入密码。",
                "Delete the saved profile of {0} from this PC?\r\n\r\nReconnecting later will require the password again.",
                "ต้องการลบโปรไฟล์ที่บันทึกไว้ของ {0} ออกจากเครื่องนี้หรือไม่?\r\n\r\nการเชื่อมต่อครั้งหน้าจะต้องกรอกรหัสผ่านใหม่");
            A("dlg.deleteAskLive", "\r\n\r\n注意：这是当前正在连接的网络，删除后连接会立即断开。",
                "\r\n\r\nNote: this is the network you are connected to; it will disconnect immediately.",
                "\r\n\r\nหมายเหตุ: นี่คือเครือข่ายที่กำลังเชื่อมต่ออยู่ การเชื่อมต่อจะขาดทันที");
            A("dlg.deletePick", "请先在列表中选择要删除的网络。",
                "Select the network you want to delete first.", "เลือกเครือข่ายที่ต้องการลบก่อน");
            A("dlg.dupTitle", "名称重复", "Duplicate name", "ชื่อซ้ำ");
            A("dlg.dupAsk", "列表里已经有一个叫{0}的配置。\r\n\r\n继续保存会覆盖它。",
                "A profile named {0} already exists.\r\n\r\nSaving will overwrite it.",
                "มีโปรไฟล์ชื่อ {0} อยู่แล้ว\r\n\r\nหากบันทึกต่อจะเขียนทับโปรไฟล์เดิม");
            A("dlg.exportFail", "导出失败", "Export failed", "ส่งออกไม่สำเร็จ");

            // ---- statistics cards ----
            A("p.saved", "已保存网络", "Saved", "บันทึกไว้");
            A("p.current", "当前连接", "Connected", "เชื่อมต่ออยู่");
            A("p.withKey", "含密码", "With key", "มีรหัสผ่าน");
            A("p.open", "开放网络", "Open", "เครือข่ายเปิด");
            A("p.autoCount", "自动连接 {0} 个", "{0} auto", "อัตโนมัติ {0}");
            A("p.dblClick", "双击一行即可查看", "Double-click a row", "ดับเบิลคลิกที่แถวเพื่อดู");
            A("p.openHint", "无密码保护，连接即用", "No password needed", "ไม่ต้องใช้รหัสผ่าน");
            A("p.allKeyHint", "全部网络均有密码", "All have passwords", "มีรหัสผ่านทั้งหมด");
            A("p.notConnected", "未连接", "Not connected", "ไม่ได้เชื่อมต่อ");
            A("p.noAssoc", "无线网卡未关联热点", "Adapter not associated", "อะแดปเตอร์ยังไม่เชื่อมต่อ");
            A("p.signalRssi", "信号 {0}%  ·  {1} dBm", "Signal {0}%  ·  {1} dBm", "สัญญาณ {0}%  ·  {1} dBm");
            A("p.signal", "信号 {0}%", "Signal {0}%", "สัญญาณ {0}%");

            // ---- current connection card ----
            A("c.signal", "信号", "Signal", "สัญญาณ");
            A("c.label", "当前连接", "Connected to", "เชื่อมต่ออยู่กับ");
            A("c.connected", "已连接", "Connected", "เชื่อมต่อแล้ว");
            A("c.none", "未连接无线网络", "No wireless connection", "ไม่มีการเชื่อมต่อไร้สาย");
            A("c.bssidUnknown", "BSSID 未知", "BSSID unknown", "ไม่ทราบ BSSID");
            A("c.empty", "连接任意无线网络后，这里会显示 IP、网关、DNS 与链路速率。",
                "Connect to any wireless network and the IP, gateway, DNS and link speed appear here.",
                "เชื่อมต่อไร้สายแล้ว IP เกตเวย์ DNS และความเร็วลิงก์จะแสดงที่นี่");
            A("c.ip", "IP 地址", "IP address", "IP");
            A("c.gw", "网关", "Gateway", "เกตเวย์");
            A("c.dns", "DNS", "DNS", "DNS");
            A("c.speed", "链路速率", "Link speed", "ความเร็วลิงก์");
            A("c.security", "安全类型", "Security", "ความปลอดภัย");

            // ---- add / edit dialog ----
            A("d.addTitle", "添加 WiFi", "Add Wi-Fi", "เพิ่ม Wi-Fi");
            A("d.editTitle", "编辑 WiFi", "Edit Wi-Fi", "แก้ไข Wi-Fi");
            A("d.ssid", "网络名称（SSID）", "Network name (SSID)", "ชื่อเครือข่าย (SSID)");
            A("d.security", "安全类型", "Security type", "ประเภทความปลอดภัย");
            A("d.key", "密码（8–63 位）", "Password (8–63 characters)", "รหัสผ่าน (8–63 ตัวอักษร)");
            A("d.keyOpen", "密码（开放网络无需密码）", "Password (not needed for open networks)",
                "รหัสผ่าน (เครือข่ายเปิดไม่ต้องใช้)");
            A("d.showKey", "显示密码", "Show", "แสดง");
            A("d.hideKey", "隐藏密码", "Hide", "ซ่อน");
            A("d.chipHidden", "隐藏网络", "Hidden network", "เครือข่ายซ่อน");
            A("d.chipAuto", "自动连接", "Auto connect", "อัตโนมัติ");
            A("d.chipConnect", "保存后连接", "Connect after save", "เชื่อมต่อหลังบันทึก");
            A("d.save", "保存", "Save", "บันทึก");
            A("d.saveEdit", "保存修改", "Save changes", "บันทึกการแก้ไข");
            A("d.auth0", "WPA2-PSK（AES）· 最常用", "WPA2-PSK (AES) · most common", "WPA2-PSK (AES) · ใช้ทั่วไป");
            A("d.auth1", "WPA3-SAE（AES）· 更安全", "WPA3-SAE (AES) · stronger", "WPA3-SAE (AES) · ปลอดภัยกว่า");
            A("d.auth2", "WPA-PSK（TKIP）· 兼容老设备", "WPA-PSK (TKIP) · legacy devices",
                "WPA-PSK (TKIP) · สำหรับอุปกรณ์รุ่นเก่า");
            A("d.auth3", "开放网络（无密码）", "Open network (no password)", "เครือข่ายเปิด (ไม่ใช้รหัสผ่าน)");
            A("d.cannotSave", "无法保存", "Cannot save", "บันทึกไม่ได้");
            A("d.errNoSsid", "请输入网络名称（SSID）。", "Enter a network name (SSID).", "กรุณากรอกชื่อเครือข่าย (SSID)");
            A("d.errSsidLong", "网络名称过长，最多 32 个字节（中文约 10 个字）。",
                "The name is too long: 32 bytes at most (about 10 Chinese characters).",
                "ชื่อยาวเกินไป จำกัด 32 ไบต์ (อักษรจีนประมาณ 10 ตัว)");
            A("d.errKeyLen", "WPA 密码长度需为 8–63 个字符。\r\n\r\n当前长度：{0} 个字符。",
                "A WPA password must be 8–63 characters.\r\n\r\nCurrent length: {0}.",
                "รหัสผ่าน WPA ต้องมี 8–63 ตัวอักษร\r\n\r\nความยาวปัจจุบัน: {0}");

            // ---- security labels (data model) ----
            A("a.open", "开放网络", "Open", "เครือข่ายเปิด");
            A("a.wpa3ent192", "WPA3 企业 192", "WPA3 Enterprise 192", "WPA3 Enterprise 192");
            A("a.wpa3ent", "WPA3 企业", "WPA3 Enterprise", "WPA3 Enterprise");
            A("a.wpa2ent", "WPA2 企业", "WPA2 Enterprise", "WPA2 Enterprise");
            A("a.wpaent", "WPA 企业", "WPA Enterprise", "WPA Enterprise");
            A("a.shared", "WEP 共享", "WEP Shared", "WEP Shared");
            A("a.unknown", "未知", "Unknown", "ไม่ทราบ");
            A("m.auto", "自动连接", "Auto", "อัตโนมัติ");
            A("m.manual", "手动连接", "Manual", "ด้วยตนเอง");

            A("w.errNoProfiles", "没有导出任何 WiFi 配置。请确认无线网卡已启用，且至少连接过一个网络。",
                "No Wi-Fi profile could be exported. Check that the wireless adapter is enabled and that this PC has joined at least one network.",
                "ไม่สามารถส่งออกโปรไฟล์ WiFi ได้ ตรวจสอบว่าอะแดปเตอร์ไร้สายเปิดอยู่และเคยเชื่อมต่ออย่างน้อยหนึ่งเครือข่าย");
            A("html.noTemplate", "内置模板缺失：", "The built-in report template is missing:",
                "ไม่พบเทมเพลตที่ฝังไว้:");
            A("w.errNoBackup", "备份文件不存在，无法回滚。",
                "The backup file is missing, so it cannot be restored.",
                "ไม่พบไฟล์สำรอง จึงคืนค่าไม่ได้");
            A("w.errNoWlan", "无法连接 WLAN 服务，请确认无线网卡可用。",
                "The WLAN service could not be reached. Check that the wireless adapter is available.",
                "เชื่อมต่อบริการ WLAN ไม่ได้ ตรวจสอบว่าอะแดปเตอร์ไร้สายพร้อมใช้งาน");
            A("w.apiDeleted", "已通过系统接口删除「{0}」（netsh 无法处理以 # 开头的名字）。",
                "Removed \"{0}\" through the system API, because netsh cannot handle a name that starts with '#'.",
                "ลบ \"{0}\" ผ่าน API ของระบบแล้ว เพราะ netsh จัดการชื่อที่ขึ้นต้นด้วย # ไม่ได้");
            A("w.apiDeleteFail", "系统接口也无法删除「{0}」（{1}）。",
                "The system API could not remove \"{0}\" either ({1}).",
                "API ของระบบก็ลบ \"{0}\" ไม่ได้เช่นกัน ({1})");
            A("w.apiStillThere", "删除命令已执行，但档案「{0}」仍然存在。",
                "The delete call went through, but the profile \"{0}\" is still listed.",
                "เรียกคำสั่งลบแล้ว แต่โปรไฟล์ \"{0}\" ยังอยู่ในระบบ");
            A("w.apiConnected", "已通过系统接口请求连接「{0}」。",
                "Connection to \"{0}\" requested through the system API.",
                "ขอเชื่อมต่อ \"{0}\" ผ่าน API ของระบบแล้ว");
            A("w.apiConnectMissing", "没有任何无线接口保存着「{0}」。",
                "No wireless interface holds a profile for \"{0}\".",
                "ไม่มีอินเทอร์เฟซไร้สายใดเก็บโปรไฟล์ \"{0}\"");
            A("w.apiConnectFail", "系统接口连接失败（{1}）。",
                "The system API could not start the connection ({1}).",
                "API ของระบบเริ่มการเชื่อมต่อไม่สำเร็จ ({1})");
            A("w.exportFail", "无法导出档案「{0}」，操作已取消。",
                "The profile \"{0}\" could not be exported, so nothing was changed.",
                "ส่งออกโปรไฟล์ \"{0}\" ไม่ได้ จึงยังไม่มีการเปลี่ยนแปลง");
            A("dlg.keepOldTitle", "旧网络没删掉", "The old network is still there",
                "เครือข่ายเดิมยังอยู่");
            A("dlg.keepOld", "已保存为「{1}」，但旧条目「{0}」没能删除，列表里会看到两条。可以右键旧条目单独删除。",
                "Saved as \"{1}\", but the old entry \"{0}\" could not be removed, so two rows are left. Right-click the old one to delete it.",
                "บันทึกเป็น \"{1}\" แล้ว แต่ลบรายการเดิม \"{0}\" ไม่ได้ จึงเหลือสองรายการ คลิกขวารายการเดิมเพื่อลบ");

            // ---- about ----
            A("ab.title", "关于", "About", "เกี่ยวกับ");
            A("ab.tagline", "查看、连接与管理本机已保存的 Wi-Fi 配置，并导出一份可离线查看的 HTML 报告。",
                "View, connect to and manage the Wi-Fi profiles saved on this PC, and export an offline HTML report.",
                "ดู เชื่อมต่อ และจัดการโปรไฟล์ Wi-Fi ที่บันทึกไว้ในเครื่องนี้ พร้อมส่งออกรายงาน HTML ไว้ดูแบบออฟไลน์");
            A("ab.version", "版本", "Version", "เวอร์ชัน");
            A("ab.host", "本机", "Computer", "เครื่องนี้");
            A("ab.os", "系统", "System", "ระบบ");
            A("ab.clr", "运行时", "Runtime", "รันไทม์");
            A("ab.nets", "已保存网络", "Saved networks", "เครือข่ายที่บันทึกไว้");
            A("ab.privacy", "所有数据只在本机读取与渲染：程序不联网、不上传、无需安装驱动。导出的 HTML 报告包含明文密码，请谨慎分享。",
                "Everything is read and rendered locally: the app never goes online, uploads nothing and installs no driver. The exported HTML report contains plain-text passwords, so share it carefully.",
                "ข้อมูลทั้งหมดอ่านและแสดงผลในเครื่องเท่านั้น โปรแกรมไม่เชื่อมต่ออินเทอร์เน็ต ไม่อัปโหลด และไม่ติดตั้งไดรเวอร์ รายงาน HTML ที่ส่งออกมีรหัสผ่านแบบข้อความธรรมดา โปรดแชร์อย่างระมัดระวัง");
            A("ab.openHome", "打开项目主页", "Open project page", "เปิดหน้าโปรเจกต์");
            A("ab.close", "关闭", "Close", "ปิด");
            A("ab.copy", "复制版本信息", "Copy version info", "คัดลอกข้อมูลเวอร์ชัน");
            A("ab.copied", "版本信息已复制", "Version info copied", "คัดลอกข้อมูลเวอร์ชันแล้ว");
            A("ab.check", "检查更新", "Check for updates", "ตรวจสอบอัปเดต");
            A("ab.checking", "正在检查新版本…", "Checking for updates…", "กำลังตรวจสอบอัปเดต…");
            A("ab.upToDate", "已是最新版本（{0}）", "You are up to date ({0})", "เป็นเวอร์ชันล่าสุดแล้ว ({0})");
            A("ab.newVersion", "发现新版本 {0}，可在 GitHub 下载", "New version {0} is available on GitHub",
                "มีเวอร์ชันใหม่ {0} ดาวน์โหลดได้ที่ GitHub");
            A("ab.download", "下载新版", "Download", "ดาวน์โหลด");
            A("ab.recheck", "重新检查", "Check again", "ตรวจอีกครั้ง");
            A("ab.checkFailed", "无法检查更新（当前无网络访问）",
                "Could not check for updates (no network access)",
                "ตรวจสอบอัปเดตไม่ได้ (ไม่มีการเชื่อมต่อเครือข่าย)");
        }
    }
}
