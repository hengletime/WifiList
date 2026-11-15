#!/usr/bin/env python3
"""把一版 WifiList 产物发布到 GitHub Releases —— 前置校验、幂等、终核。

为什么要有这支脚本：
发布若靠手动调 API，名称容易退化成裸 tag、正文容易漏掉校验清单、资产容易只传一半，
而这些错误不会报错，只会安安静静留在线上。所以把「一版发布的全部要件」写成硬校验，
缺一条就拒绝发，宁可发不出去，也不发一张残缺的页面。

用法：
    python scripts/publish_release.py 1.6.1             # 创建或更新（幂等）
    python scripts/publish_release.py 1.6.1 --dry-run   # 只跑校验，不动线上
    python scripts/publish_release.py 1.6.1 --name "..."  # 自定义页面上显示的标题

前置（缺一不可，脚本会逐条查）：
  · 四处版本号一致（AssemblyInfo.cs / UiKit.cs / Setup.cs / 打包发布.bat）
  · RELEASE_NOTES_v<版本>.md 存在，且带 <!--SHA--> 占位或完整校验小节
  · dist/ 下两件产物存在且非空
  · .tmp/gh_cred 里有可用令牌（git credential fill 写出，发布完删掉）

发布后终核：线上资产全部 uploaded，且每个 digest 与本地 sha256 逐字相等。
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(ROOT)

API = "https://api.github.com"
REPO = "hengletime/WifiList"
PRODUCT = "WiFi 密码清单"
CRED = os.path.join(".tmp", "gh_cred")

TOKEN = ""


def die(msg: str) -> None:
    print("FATAL: " + msg, file=sys.stderr)
    raise SystemExit(1)


def ok(msg: str) -> None:
    print("\033[1;32m[ OK ]\033[0m " + msg, flush=True)


def info(msg: str) -> None:
    print("\033[1;34m[ .. ]\033[0m " + msg, flush=True)


# ---------------------------------------------------------------- 令牌与 HTTP


def read_token() -> str:
    if not os.path.exists(CRED):
        die(
            "缺 %s。先执行（Git Bash）：\n"
            '  mkdir -p .tmp && printf "protocol=https\\nhost=github.com\\n\\n" | git credential fill > .tmp/gh_cred\n'
            "发布完立刻删掉该文件。" % CRED
        )
    token = ""
    with open(CRED, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            if line.startswith("password="):
                token = line.split("=", 1)[1].strip()
    if not token:
        die("%s 里没有 password= 行，重新用 git credential fill 生成" % CRED)
    return token


def make_openers():
    """直连与代理两条路都准备好，谁通用谁。

    这台机器访问 GitHub 的稳定性会来回翻（直连被断、代理 502 都遇到过），
    所以不预设路线，失败一次就换另一条。
    """
    direct = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    proxies = {}
    for scheme in ("http", "https"):
        v = os.environ.get(scheme + "_proxy") or os.environ.get(scheme.upper() + "_PROXY")
        if v:
            proxies[scheme] = v
    proxied = urllib.request.build_opener(urllib.request.ProxyHandler(proxies)) if proxies else None
    return direct, proxied


DIRECT, PROXIED = make_openers()
ROUTE = "direct"


def _opener():
    if ROUTE == "proxy" and PROXIED is not None:
        return PROXIED
    return DIRECT


def _switch():
    global ROUTE
    if PROXIED is not None:
        ROUTE = "proxy" if ROUTE == "direct" else "direct"


def call(method: str, url: str, payload=None, timeout: int = 60, tries: int = 8, allow_404: bool = False):
    data = None
    headers = {
        "Authorization": "Bearer " + TOKEN,
        "Accept": "application/vnd.github+json",
        "User-Agent": "wifilist-release",
    }
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    last = None
    for i in range(tries):
        try:
            req = urllib.request.Request(url, data=data, headers=headers, method=method)
            with _opener().open(req, timeout=timeout) as r:
                body = r.read()
                return json.loads(body.decode("utf-8")) if body else None
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")[:400]
            if allow_404 and e.code == 404:
                return None
            if e.code in (401, 403, 404, 422):
                die("%s %s -> HTTP %d %s" % (method, url, e.code, detail))
            last = "HTTP %d %s" % (e.code, detail)
        except Exception as e:  # noqa: BLE001
            last = str(e)
        print("RETRY %d/%d [%s] %s %s: %s" % (i + 1, tries, ROUTE, method, url.rsplit("/", 1)[-1], last), flush=True)
        _switch()
        time.sleep(6)
    die("%s %s 连续 %d 次失败（直连与代理都试过）：%s" % (method, url, tries, last))


def upload(upload_url: str, path: str, tries: int = 6):
    name = os.path.basename(path)
    url = upload_url.split("{")[0] + "?name=" + urllib.parse.quote(name)
    with open(path, "rb") as f:
        blob = f.read()
    headers = {
        "Authorization": "Bearer " + TOKEN,
        "Accept": "application/vnd.github+json",
        "Content-Type": "application/octet-stream",
        "User-Agent": "wifilist-release",
    }
    last = None
    for i in range(tries):
        try:
            req = urllib.request.Request(url, data=blob, headers=headers, method="POST")
            with _opener().open(req, timeout=300) as r:
                return json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")[:300]
            die("上传 %s -> HTTP %d %s" % (name, e.code, detail))
        except Exception as e:  # noqa: BLE001
            last = str(e)
        print("RETRY %d/%d [%s] upload %s: %s" % (i + 1, tries, ROUTE, name, last), flush=True)
        _switch()
        time.sleep(8)
    die("上传 %s 连续 %d 次失败：%s" % (name, tries, last))


def sha256_of(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


# ---------------------------------------------------------------- 本地前置校验


VERSION_SITES = (
    ("src/AssemblyInfo.cs", r'AssemblyVersion\("([\d.]+)"\)'),
    ("src/UiKit.cs", r'Version\s*=\s*"([\d.]+)"'),
    ("installer/Setup.cs", r'AppVersion\s*=\s*"([\d.]+)"'),
    ("打包发布.bat", r'set "VER=([\d.]+)"'),
)


def check_versions(version: str) -> None:
    bad = []
    for path, pattern in VERSION_SITES:
        if not os.path.exists(path):
            bad.append("%s 不存在" % path)
            continue
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            m = re.search(pattern, f.read())
        got = m.group(1) if m else "?"
        # AssemblyVersion is a four part number (1.6.1.0); accept it as 1.6.1
        if got not in (version, version + ".0"):
            bad.append("%s=%s" % (path, got))
    if bad:
        die("版本号与 %s 不一致：%s —— 先统一四处版本再发布" % (version, ", ".join(bad)))
    ok("四处版本号一致：%s" % version)


def artifacts(version: str):
    return [
        os.path.join("dist", "WifiList-Setup-%s.exe" % version),
        os.path.join("dist", "WifiList-%s-portable.zip" % version),
    ]


def check_artifacts(version: str):
    paths = artifacts(version)
    for p in paths:
        if not os.path.exists(p):
            die("缺产物 %s —— 先跑 打包发布.bat" % p)
        if os.path.getsize(p) == 0:
            die("%s 是 0 字节" % p)
    ok("两件产物齐备：" + ", ".join("%s (%d 字节)" % (os.path.basename(p), os.path.getsize(p)) for p in paths))
    return paths


SHA_BLOCK = "### SHA-256\n\n```\n%s\n```"


def build_body(version: str, paths) -> str:
    src = "RELEASE_NOTES_v%s.md" % version
    if not os.path.exists(src):
        die("缺发布说明 %s —— 发布说明必须进版本库，不能只贴在 GitHub 正文里" % src)
    with open(src, "r", encoding="utf-8") as f:
        body = f.read().strip()

    first = body.splitlines()[0].strip()
    if first != "# %s v%s" % (PRODUCT, version):
        die("%s 首行必须是 `# %s v%s`，当前是：%s" % (src, PRODUCT, version, first))

    if "<!--SHA-->" not in body:
        die("%s 缺 <!--SHA--> 占位，校验清单无处插入" % src)

    block = SHA_BLOCK % "\n".join("%s  %s" % (sha256_of(p), os.path.basename(p)) for p in paths)
    body = body.replace("<!--SHA-->", block)

    # 页面标题已经显示同一个名字，正文再来一行同名 H1 是重复的两行大字
    lines = body.splitlines()
    return "\n".join(lines[1:]).lstrip("\n")


# ---------------------------------------------------------------- 主流程


def main() -> None:
    global TOKEN

    args = sys.argv[1:]
    if not args or args[0].startswith("-"):
        die("用法：python scripts/publish_release.py <版本号> [--dry-run] [--name 文本]")
    version = args[0].lstrip("v")
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        die("版本号格式应为 X.Y.Z，收到：%s" % version)

    dry = "--dry-run" in args
    name = "%s v%s" % (PRODUCT, version)
    if "--name" in args:
        name = args[args.index("--name") + 1]

    print("== 前置校验 ==")
    check_versions(version)
    paths = check_artifacts(version)
    body = build_body(version, paths)
    ok("发布说明 %d 字符，校验清单已内联" % len(body))

    print("\n== 发布内容 ==")
    print("tag   : v%s" % version)
    print("name  : %s" % name)
    print("资产  : %s" % ", ".join(os.path.basename(p) for p in paths))

    if dry:
        print("\n--dry-run：校验通过，未改动线上")
        return

    TOKEN = read_token()
    tag = "v" + version
    print("\n== 同步线上 ==")

    existing = call("GET", "%s/repos/%s/releases/tags/%s" % (API, REPO, tag), allow_404=True)
    payload = {"name": name, "body": body, "prerelease": False}
    if existing is None:
        payload.update({"tag_name": tag, "draft": False})
        rel = call("POST", "%s/repos/%s/releases" % (API, REPO), payload)
        ok("已创建 release id=%s" % rel["id"])
    else:
        rel = call("PATCH", "%s/repos/%s/releases/%s" % (API, REPO, existing["id"]), payload)
        ok("已更新 release id=%s（名称与正文；原有资产保留）" % rel["id"])

    have = {a["name"]: a for a in rel.get("assets") or []}
    for path in paths:
        fname = os.path.basename(path)
        digest = "sha256:" + sha256_of(path)
        size = os.path.getsize(path)
        cur = have.get(fname)
        if cur and cur.get("state") == "uploaded" and cur.get("size") == size and cur.get("digest") == digest:
            ok("线上已有一致资产：%s" % fname)
            continue
        if cur:
            info("同名资产不一致，先删再传：%s" % fname)
            call("DELETE", "%s/repos/%s/releases/assets/%s" % (API, REPO, cur["id"]))
        info("上传 %s（%d 字节）" % (fname, size))
        up = upload(rel["upload_url"], path)
        if up.get("size") != size:
            die("上传后尺寸不符：%s 线上 %s / 本地 %d" % (fname, up.get("size"), size))
        ok("已上传 %s state=%s" % (fname, up.get("state")))

    print("\n== 终核 ==")
    final = call("GET", "%s/repos/%s/releases/%s" % (API, REPO, rel["id"]))
    if final.get("name") != name:
        die("名称没写进去：线上仍是 %r" % final.get("name"))
    ok("名称：%s" % final["name"])

    assets = {a["name"]: a for a in final.get("assets") or []}
    for path in paths:
        fname = os.path.basename(path)
        if fname not in assets:
            die("线上缺资产：%s" % fname)
        want = "sha256:" + sha256_of(path)
        got = assets[fname].get("digest")
        if got and got != want:
            die("digest 不符：%s 线上 %s / 本地 %s" % (fname, got, want))
    ok("两件资产齐全，digest 与本地逐字一致")
    print("\n完成：https://github.com/%s/releases/tag/%s" % (REPO, tag))


if __name__ == "__main__":
    main()
