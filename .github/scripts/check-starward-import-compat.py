#!/usr/bin/env python3
"""对照上游 Starward 的 DatabaseSqls，检查 Moonward 导入规则是否跟上。

只读检查，不改任何业务代码。
退出码：0 通过；1 需要开发者补导入规则；2 检查本身失败（文件缺失或上游拉不到）。
"""

from __future__ import annotations

import argparse
import hashlib
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

DEFAULT_UPSTREAM_REPO = "Scighost/Starward"
DEFAULT_UPSTREAM_REF = "main"
IMPORT_REL = Path("src/Starward/Features/Database/StarwardDataImportService.cs")
DB_REL = Path("src/Starward/Features/Database/DatabaseService.cs")
MARKER = "<!-- starward-import-compat-check -->"


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def fetch_text(url: str) -> str:
    req = urllib.request.Request(
        url,
        headers={"User-Agent": "Moonward-starward-import-compat-check"},
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        return resp.read().decode("utf-8")


def require_int(pattern: str, text: str, label: str) -> int:
    match = re.search(pattern, text)
    if not match:
        raise ValueError(f"无法解析 {label}")
    return int(match.group(1))


def pragma_versions(text: str) -> list[int]:
    return [int(n) for n in re.findall(r"PRAGMA USER_VERSION\s*=\s*(\d+)", text)]


def rollback_versions(text: str) -> list[int]:
    block = re.search(
        r"StarwardOnlyRollbacks\s*=\s*\[(.*?)\]\s*;",
        text,
        re.S,
    )
    if not block:
        return []
    return [int(n) for n in re.findall(r"\(\s*(\d+)\s*,", block.group(1))]


def keep_versions(text: str) -> set[int]:
    found = {int(n) for n in re.findall(r"import-keep:\s*(\d+)", text)}
    # ExtraStarNum：Starward v19 / Moonward v20，源码未标注时仍视为保留。
    if 19 not in found and re.search(r"ExtraStarNum", text):
        found.add(19)
    return found


def sql_bodies(text: str) -> dict[int, str]:
    bodies: dict[int, str] = {}
    for match in re.finditer(
        r"private const string Sql_v(\d+)\s*=\s*\"\"\"(.*?)\"\"\";",
        text,
        re.S,
    ):
        bodies[int(match.group(1))] = normalize_sql(match.group(2))
    return bodies


def normalize_sql(sql: str) -> str:
    lines = [line.rstrip() for line in sql.replace("\r\n", "\n").replace("\r", "\n").split("\n")]
    return "\n".join(lines).strip()


def fingerprint(problems: list[str]) -> str:
    raw = "\n".join(problems).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()[:16]


def check(
    import_cs: str,
    local_db: str,
    upstream_db: str,
) -> list[str]:
    common = require_int(r"CommonUserVersion\s*=\s*(\d+)", import_cs, "CommonUserVersion")
    known_max = require_int(
        r"KnownMaxStarwardUserVersion\s*=\s*(\d+)",
        import_cs,
        "KnownMaxStarwardUserVersion",
    )
    upstream_versions = pragma_versions(upstream_db)
    local_versions = pragma_versions(local_db)
    if not upstream_versions:
        raise ValueError("上游 DatabaseService.cs 里没有 PRAGMA USER_VERSION")
    if not local_versions:
        raise ValueError("本仓 DatabaseService.cs 里没有 PRAGMA USER_VERSION")

    upstream_max = max(upstream_versions)
    local_max = max(local_versions)
    rollbacks = set(rollback_versions(import_cs))
    keeps = keep_versions(import_cs)

    problems: list[str] = []
    problems.append(f"本仓共同祖先 CommonUserVersion = {common}")
    problems.append(f"本仓已登记的最高上游版本 KnownMax = {known_max}")
    problems.append(f"本仓回退表 StarwardOnlyRollbacks = {sorted(rollbacks) or '（空）'}")
    problems.append(f"本仓保留版本 import-keep = {sorted(keeps) or '（空）'}")
    problems.append(f"本仓 Moonward USER_VERSION 最高 = {local_max}")
    problems.append(f"上游 Starward USER_VERSION 最高 = {upstream_max}")

    findings: list[str] = []

    if upstream_max > known_max:
        findings.append(
            f"上游 Starward 已到 v{upstream_max}，本仓 KnownMax 仍是 {known_max}。"
            "导入会拒绝该版本。请补回退或 import-keep，再提高 KnownMax。"
        )

    if any(v > known_max for v in rollbacks):
        extra = sorted(v for v in rollbacks if v > known_max)
        findings.append(f"回退表里有高于 KnownMax 的版本：{extra}。请把 KnownMax 提到至少 {max(extra)}。")

    uncovered = [
        v
        for v in range(common + 1, known_max + 1)
        if v not in rollbacks and v not in keeps
    ]
    if uncovered:
        findings.append(
            f"KnownMax={known_max} 覆盖了 v{common + 1}–v{known_max}，"
            f"但这些版本既没有回退也没有 import-keep：{uncovered}。"
            "只改数字会把未处理的 Starward schema 放行。"
        )

    local_sql = sql_bodies(local_db)
    upstream_sql = sql_bodies(upstream_db)
    drifted: list[int] = []
    missing: list[str] = []
    for version in range(1, common + 1):
        if version not in local_sql:
            missing.append(f"本仓缺 Sql_v{version}")
            continue
        if version not in upstream_sql:
            missing.append(f"上游缺 Sql_v{version}")
            continue
        if local_sql[version] != upstream_sql[version]:
            drifted.append(version)
    if missing:
        findings.append("共同祖先范围内脚本不完整：" + "；".join(missing))
    if drifted:
        findings.append(
            f"共同祖先 v1–v{common} 中，这些脚本已与上游不一致：{drifted}。"
            "若变基后重新对齐，应提高 CommonUserVersion 并删掉对应回退；"
            "若是误改已发布 Sql_vN，应改回去。"
        )

    # 信息行不算失败；只返回 findings。调用方会打印摘要。
    check.summary = problems  # type: ignore[attr-defined]
    return findings


def write_github_output(name: str, value: str) -> None:
    path = os.environ.get("GITHUB_OUTPUT")
    if not path:
        return
    with open(path, "a", encoding="utf-8") as fh:
        if "\n" in value:
            fh.write(f"{name}<<EOF\n{value}\nEOF\n")
        else:
            fh.write(f"{name}={value}\n")


def write_summary(lines: list[str]) -> None:
    path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not path:
        return
    Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="检查 Starward 导入规则是否跟上上游")
    parser.add_argument("--repo-root", default=".", help="Moonward 仓库根目录")
    parser.add_argument("--upstream-repo", default=os.environ.get("UPSTREAM_REPO", DEFAULT_UPSTREAM_REPO))
    parser.add_argument("--upstream-ref", default=os.environ.get("UPSTREAM_REF", DEFAULT_UPSTREAM_REF))
    parser.add_argument("--upstream-file", default="", help="本地上游 DatabaseService.cs，跳过网络")
    args = parser.parse_args()

    root = Path(args.repo_root).resolve()
    import_path = root / IMPORT_REL
    local_db_path = root / DB_REL
    if not import_path.is_file():
        print(f"找不到 {import_path}。请在 rebase/develop 上跑，不要在纯上游 main 上跑。", file=sys.stderr)
        return 2
    if not local_db_path.is_file():
        print(f"找不到 {local_db_path}", file=sys.stderr)
        return 2

    try:
        import_cs = read_text(import_path)
        local_db = read_text(local_db_path)
        if args.upstream_file:
            upstream_db = read_text(Path(args.upstream_file))
            upstream_label = str(Path(args.upstream_file).resolve())
        else:
            url = (
                f"https://raw.githubusercontent.com/{args.upstream_repo}/"
                f"{args.upstream_ref}/src/Starward/Features/Database/DatabaseService.cs"
            )
            upstream_label = url
            print(f"拉取上游：{url}")
            upstream_db = fetch_text(url)
    except (OSError, urllib.error.URLError, ValueError) as exc:
        print(f"检查未能完成：{exc}", file=sys.stderr)
        return 2

    try:
        findings = check(import_cs, local_db, upstream_db)
        summary = getattr(check, "summary", [])
    except ValueError as exc:
        print(f"解析失败：{exc}", file=sys.stderr)
        return 2

    print("对照：", upstream_label)
    for line in summary:
        print(line)

    md = [
        "## Starward 导入兼容检查",
        "",
        f"上游：`{args.upstream_repo}@{args.upstream_ref}`",
        "",
    ]
    md.extend(f"- {line}" for line in summary)
    md.append("")

    if not findings:
        print("通过：导入规则与上游版本对齐。")
        md.append("**通过**：导入规则与上游版本对齐。")
        write_summary(md)
        write_github_output("failed", "false")
        write_github_output("fingerprint", "ok")
        write_github_output("report", "通过：导入规则与上游版本对齐。")
        return 0

    print("需要开发者处理：")
    md.append("**需要开发者处理（检查不会改代码）：**")
    md.append("")
    for item in findings:
        print(f"- {item}")
        md.append(f"- {item}")
    md.extend(
        [
            "",
            "每次上游新 `Sql_vN`：",
            "1. Starward 独有 → 写 `StarwardOnlyRollbacks`，再提高 `KnownMaxStarwardUserVersion`。",
            "2. Moonward 也要 → 追加本仓 `Sql_vN`；列已存在则加 `IsMigrationAlreadySatisfied`，并写 `import-keep: N`。",
            "3. 变基后又对齐 → 提高 `CommonUserVersion`，删掉对应回退。",
            "4. **不要**只改 KnownMax。",
        ]
    )
    report = "\n".join(findings)
    write_summary(md)
    write_github_output("failed", "true")
    write_github_output("fingerprint", fingerprint(findings))
    write_github_output("report", report)
    return 1


if __name__ == "__main__":
    sys.exit(main())
