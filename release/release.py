"""
CasPrepared release script (run locally: the build needs the game's assemblies).

    python release/release.py 1.1.0                 # both builds
    python release/release.py 1.1.0 --only standalone
    python release/release.py 1.1.0 --dry-run       # build and package only, no GitHub release

Steps:
  1. Checks: version matches Plugin.cs / .csproj, working tree is clean and pushed, tags are free.
  2. Builds every package in Release (without copying into the game).
  3. Packages into release/out/<version>/:
       <Assembly>-<version>.zip              GitHub / Nexus (folder for BepInEx/plugins)
       <Assembly>-<version>-thunderstore.zip Thunderstore (+ icon.png, manifest.json, README.md)
  4. Creates one GitHub release per package (tags v<version> and S-<version>) with its zips.
     Release notes come from CHANGELOG.md ("## <version>" section) or --notes.
     Publishing a release starts .github/workflows/publish.yml (Nexus Mods + Thunderstore).
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "release", "out")
CONFIG = os.path.join(ROOT, "release", "packages.json")
GH_FALLBACK = r"C:\Program Files\GitHub CLI\gh.exe"


def fail(msg):
    print("ERROR: " + msg, file=sys.stderr)
    sys.exit(1)


def run(cmd, capture=False):
    print("> " + " ".join(cmd))
    r = subprocess.run(cmd, cwd=ROOT, text=True, capture_output=capture)
    if r.returncode != 0:
        if capture:
            print(r.stdout + r.stderr, file=sys.stderr)
        fail("command failed: " + " ".join(cmd))
    return r.stdout if capture else None


def git(*args):
    return run(["git", *args], capture=True).strip()


def gh_path():
    found = shutil.which("gh")
    if found:
        return found
    if os.path.exists(GH_FALLBACK):
        return GH_FALLBACK
    fail("GitHub CLI not found. Install it (winget install GitHub.cli) and run: gh auth login")


def read(path):
    with open(os.path.join(ROOT, path), encoding="utf-8") as f:
        return f.read()


def check_versions(pkg, version):
    plugin = read(os.path.join(pkg["project"], "Plugin.cs"))
    m = re.search(r'ModVersion\s*=\s*"([^"]+)"', plugin)
    if not m or m.group(1) != version:
        fail(f"{pkg['project']}/Plugin.cs has ModVersion {m.group(1) if m else '?'}, expected {version}")
    csproj = read(os.path.join(pkg["project"], pkg["project"] + ".csproj"))
    m = re.search(r"<Version>([^<]+)</Version>", csproj)
    if not m or m.group(1) != version:
        fail(f"{pkg['project']}.csproj has Version {m.group(1) if m else '?'}, expected {version}")


def changelog_section(version):
    path = os.path.join(ROOT, "CHANGELOG.md")
    if not os.path.exists(path):
        return None
    text = read("CHANGELOG.md")
    m = re.search(r"^## +" + re.escape(version) + r"[^\n]*\n(.*?)(?=^## |\Z)", text, re.S | re.M)
    return m.group(1).strip() if m else None


def build(pkg):
    cmd = ["dotnet", "build", pkg["project"], "-c", "Release", "-nologo", "-v", "q"]
    if pkg["project"] == "CasPrepared":
        cmd.append("-p:DeployToGame=false")
    run(cmd)
    out = os.path.join(ROOT, pkg["project"], "bin", "Release", "net48")
    files = [os.path.join(out, pkg["assembly"] + ext) for ext in (".dll", ".pdb")]
    for f in files:
        if not os.path.exists(f):
            fail("build output missing: " + f)
    return files


def package(pkg, cfg, version, files, outdir):
    ts = cfg["thunderstore"]
    name = pkg["assembly"]
    plugin_files = [(f"{name}/{os.path.basename(f)}", open(f, "rb").read()) for f in files]

    plain = os.path.join(outdir, f"{name}-{version}.zip")
    with zipfile.ZipFile(plain, "w", zipfile.ZIP_DEFLATED) as z:
        for arc, data in plugin_files:
            z.writestr(arc, data)

    manifest = {
        "name": pkg["thunderstore"]["name"],
        "version_number": version,  # Thunderstore needs plain x.y.z (no "S-" prefix)
        "website_url": ts["website_url"],
        "description": pkg["thunderstore"]["description"],
        "dependencies": pkg["thunderstore"]["dependencies"],
    }
    store = os.path.join(outdir, f"{name}-{version}-thunderstore.zip")
    with zipfile.ZipFile(store, "w", zipfile.ZIP_DEFLATED) as z:
        for arc, data in plugin_files:
            z.writestr(arc, data)
        z.writestr("icon.png", open(os.path.join(ROOT, ts["icon"]), "rb").read())
        z.writestr("manifest.json", json.dumps(manifest, indent=2))
        z.writestr("README.md", read(pkg["thunderstore"]["readme"]).encode("utf-8"))
    print(f"  {os.path.relpath(plain, ROOT)}\n  {os.path.relpath(store, ROOT)}")
    return [plain, store]


def main():
    cfg = json.loads(read(os.path.relpath(CONFIG, ROOT)))
    ap = argparse.ArgumentParser(description="Build, package and release CasPrepared.")
    ap.add_argument("version", help="x.y.z, without prefixes")
    ap.add_argument("--only", choices=list(cfg["packages"].keys()), help="release one package only")
    ap.add_argument("--notes", help="file with release notes (default: CHANGELOG.md section)")
    ap.add_argument("--dry-run", action="store_true", help="build and package only, no GitHub release")
    args = ap.parse_args()

    version = args.version
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        fail("version must look like 1.2.3 (Thunderstore needs plain x.y.z; prefixes are added per package)")
    packages = {k: v for k, v in cfg["packages"].items() if args.only in (None, k)}

    for pkg in packages.values():
        check_versions(pkg, version)

    notes = None
    if args.notes:
        with open(args.notes, encoding="utf-8") as f:
            notes = f.read().strip()
    else:
        notes = changelog_section(version)
    if not notes:
        fail(f"no release notes: add a '## {version}' section to CHANGELOG.md or pass --notes")

    gh = None
    if not args.dry_run:
        gh = gh_path()
        run([gh, "auth", "status"], capture=True)
        if git("status", "--porcelain"):
            fail("working tree has uncommitted changes: commit and push first")
        git("fetch", "origin")
        branch = git("rev-parse", "--abbrev-ref", "HEAD")
        if git("rev-parse", "HEAD") != git("rev-parse", f"origin/{branch}"):
            fail(f"HEAD is not pushed to origin/{branch}")
        for pkg in packages.values():
            tag = pkg["tag_prefix"] + version
            if git("tag", "--list", tag) or git("ls-remote", "--tags", "origin", tag):
                fail(f"tag {tag} already exists")

    outdir = os.path.join(OUT, version)
    if os.path.isdir(outdir):
        shutil.rmtree(outdir)
    os.makedirs(outdir)

    assets = {}
    for key, pkg in packages.items():
        print(f"\n== {pkg['release_title']} {version}")
        assets[key] = package(pkg, cfg, version, build(pkg), outdir)

    if args.dry_run:
        print(f"\nDry run: packages are in {os.path.relpath(outdir, ROOT)}. Nothing was released.")
        return

    notes_file = os.path.join(outdir, "notes.md")
    with open(notes_file, "w", encoding="utf-8") as f:
        f.write(notes + "\n")
    target = git("rev-parse", "HEAD")
    for key, pkg in packages.items():
        tag = pkg["tag_prefix"] + version
        cmd = [gh, "release", "create", tag, *assets[key],
               "--title", f"{pkg['release_title']} {version}",
               "--notes-file", notes_file, "--target", target]
        # the multiplayer build stays the repository's "latest" release
        cmd.append("--latest" if key == "multiplayer" else "--latest=false")
        run(cmd)
    print("\nReleases created. The publish workflow uploads them to Nexus Mods and Thunderstore:")
    print("  " + run([gh, "repo", "view", "--json", "url", "-q", ".url"], capture=True).strip() + "/actions")


if __name__ == "__main__":
    main()
