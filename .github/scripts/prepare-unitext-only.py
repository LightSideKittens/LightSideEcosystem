#!/usr/bin/env python3
import json
import os
from pathlib import Path
import shutil


def main():
    if os.environ.get("GITHUB_ACTIONS") != "true":
        raise RuntimeError("Package removal is only supported in a GitHub Actions checkout.")
    if os.environ.get("CI_BENCHMARK_SUITE") == "motion":
        raise ValueError("Leave UniText Only cannot run the MoveIt benchmark.")

    root = Path(os.environ["GITHUB_WORKSPACE"]).resolve(strict=True)
    packages = root / "Packages"
    manifests = {}
    for path in sorted(packages.glob("*/package.json")):
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        if data["name"].startswith("media.lightside."):
            manifests[data["name"]] = (path.parent, data)

    retained = set()
    pending = ["media.lightside.unitext", "media.lightside.benchmark"]
    while pending:
        name = pending.pop()
        if name in retained:
            continue
        if name not in manifests:
            raise FileNotFoundError(f"Required embedded package is missing: {name}")
        retained.add(name)
        pending.extend(dependency for dependency in manifests[name][1].get("dependencies", {})
                       if dependency.startswith("media.lightside."))

    removed = sorted(manifests.keys() - retained)
    targets = [manifests[name][0] for name in removed]
    targets.extend(root / "Assets" / folder for folder in (
        "MoveIt_MySpace", "MoveIt_Playground", "UniShapes", "UniShapes_MySpace",
        "UniLottie", "UniLottie_MySpace", "UniText_Promo",
    ))
    targets.extend(path.with_name(path.name + ".meta") for path in list(targets))
    targets = [path for path in targets if path.exists() or path.is_symlink()]
    for path in targets:
        resolved = path.resolve(strict=True)
        if resolved == root or root not in resolved.parents or path.is_symlink():
            raise ValueError(f"Removal target is outside the checkout or is a symbolic link: {path}")

    manifest_path = packages / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    for key in ("dependencies", "overrides"):
        entries = manifest.get(key, {})
        for name in list(entries):
            if name.startswith("media.lightside.") and name not in retained:
                del entries[name]
    if "testables" in manifest:
        manifest["testables"] = [name for name in manifest["testables"]
                                 if not name.startswith("media.lightside.") or name in retained]

    for path in targets:
        print(f"Removing {path.relative_to(root).as_posix()}")
        if path.is_dir():
            shutil.rmtree(path)
        else:
            path.unlink()
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (packages / "packages-lock.json").unlink(missing_ok=True)

    report = {
        "retainedPackages": sorted(retained),
        "removedPackages": removed,
        "removedPaths": [path.relative_to(root).as_posix() for path in targets],
    }
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
