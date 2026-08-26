import argparse
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BUILD_INFO = ROOT / "src/SleddersLuaRuntime/Core/BuildInfo.cs"
PROJECT = ROOT / "src/SleddersLuaRuntime/SleddersLuaRuntime.csproj"
CS_PATTERN = re.compile(r'(public const string RuntimeVersion\s*=\s*")([^"]+)(";)')


def current():
    text = BUILD_INFO.read_text(encoding="utf-8")
    match = CS_PATTERN.search(text)
    if not match:
        raise SystemExit("RuntimeVersion was not found in BuildInfo.cs")
    return match.group(2)


def base(version):
    match = re.match(r'^(\d+)\.(\d+)\.(\d+)', version)
    if not match:
        raise SystemExit(f"Runtime version has no SemVer base: {version}")
    return ".".join(match.groups())


def replace_xml_value(text, tag, value):
    pattern = re.compile(rf'(<{re.escape(tag)}>)([^<]*)(</{re.escape(tag)}>)')
    updated, count = pattern.subn(r'\g<1>' + value + r'\g<3>', text, count=1)
    if count != 1:
        raise SystemExit(f"Could not update <{tag}> in project file")
    return updated


def set_version(version):
    if not re.match(r'^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$', version):
        raise SystemExit(f"Invalid runtime version: {version}")

    build_text = BUILD_INFO.read_text(encoding="utf-8")
    new_build, count = CS_PATTERN.subn(r'\g<1>' + version + r'\g<3>', build_text, count=1)
    if count != 1:
        raise SystemExit("Could not update RuntimeVersion")

    numeric = base(version) + ".0"
    project_text = PROJECT.read_text(encoding="utf-8")
    project_text = replace_xml_value(project_text, "Version", version)
    project_text = replace_xml_value(project_text, "AssemblyVersion", numeric)
    project_text = replace_xml_value(project_text, "FileVersion", numeric)
    project_text = replace_xml_value(project_text, "InformationalVersion", version)

    BUILD_INFO.write_text(new_build, encoding="utf-8")
    PROJECT.write_text(project_text, encoding="utf-8")


parser = argparse.ArgumentParser()
sub = parser.add_subparsers(dest="command", required=True)
sub.add_parser("get")
sub.add_parser("base")
p = sub.add_parser("set")
p.add_argument("version")
args = parser.parse_args()

if args.command == "get":
    print(current())
elif args.command == "base":
    print(base(current()))
elif args.command == "set":
    set_version(args.version)
    print(args.version)
