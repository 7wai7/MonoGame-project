#!/usr/bin/env python3
"""Build once, run the four UI/display combinations, collect one folder per run."""

import argparse
import csv
import datetime as dt
import json
import os
import pathlib
import subprocess
import sys
import uuid
from collections import defaultdict


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seconds", type=int, default=10)
    parser.add_argument("--rounds", type=int, default=2)
    parser.add_argument("--skip-build", action="store_true")
    args = parser.parse_args()
    if not 2 <= args.seconds <= 120 or not 1 <= args.rounds <= 10:
        parser.error("--seconds must be 2..120 and --rounds must be 1..10")

    project = pathlib.Path(__file__).resolve().parents[1]
    session = project / "BenchmarkResults" / (
        "session_" + dt.datetime.now(dt.timezone.utc).strftime("%Y%m%d_%H%M%S")
        + "_" + uuid.uuid4().hex[:8]
    )
    session.mkdir(parents=True)
    if not args.skip_build:
        build = subprocess.run(
            ["dotnet", "build", "MonoGame-project.csproj", "-c", "Release"],
            cwd=project,
            check=False,
        )
        if build.returncode:
            return build.returncode

    commit = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=project,
        capture_output=True, text=True, check=False,
    ).stdout.strip() or "unknown"
    environment = os.environ.copy()
    environment["BENCHMARK_GIT_COMMIT"] = commit
    dll = project / "bin" / "Release" / "net10.0" / "MonoGame-project.dll"
    results = []
    failures = []
    warnings = []
    matched_size = None
    combinations = [
        ("window", "off", False),
        ("window", "on", False),
        ("fullscreen", "off", False),
        ("fullscreen", "on", False),
        ("window", "off", True),
        ("fullscreen", "off", True),
    ]
    print(f"Benchmark output: {session}", flush=True)
    print("Keep each game window focused until it closes automatically.", flush=True)
    for display, ui, matched in combinations:
        label = f"{display}_ui-{ui}" + ("_matched" if matched else "")
        if matched and matched_size is None:
            failures.append(f"{label}: native window backbuffer size is unavailable")
            continue
        command = [
            "dotnet", str(dll), "--performance-benchmark",
            f"--benchmark-display={display}", f"--benchmark-ui={ui}",
            f"--benchmark-seconds={args.seconds}",
            f"--benchmark-rounds={args.rounds}",
            f"--benchmark-output={session}",
        ]
        if matched:
            command.extend([
                f"--benchmark-internal-width={matched_size[0]}",
                f"--benchmark-internal-height={matched_size[1]}",
            ])
        print(f"\nStarting {label}; focus the new game window.", flush=True)
        with (session / f"{label}_console.txt").open("w", encoding="utf-8") as log:
            process = subprocess.Popen(
                command, cwd=project, env=environment,
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, bufsize=1,
            )
            try:
                assert process.stdout is not None
                for line in process.stdout:
                    print(line, end="", flush=True)
                    log.write(line)
            except KeyboardInterrupt:
                process.terminate()
                process.wait()
                print("Benchmark interrupted; completed files remain in the session folder.")
                return 130
            exit_code = process.wait()

        run_folders = sorted(session.glob(f"*_{label}"))
        summary_file = run_folders[-1] / "summary.json" if run_folders else None
        if exit_code or summary_file is None or not summary_file.exists():
            failures.append(f"{label}: exit {exit_code}, missing summary")
            continue
        data = json.loads(summary_file.read_text(encoding="utf-8"))
        if label == "window_ui-off" and data:
            matched_size = (data[0]["backBufferWidth"], data[0]["backBufferHeight"])
        expected = 7 * args.rounds
        if len(data) != expected:
            failures.append(f"{label}: {len(data)}/{expected} stages complete")
        for stage in data:
            results.append({"run": label, **stage})

    if results:
        columns = list(results[0])
        with (session / "comparison.csv").open("w", newline="", encoding="utf-8") as output:
            writer = csv.DictWriter(output, fieldnames=columns)
            writer.writeheader()
            writer.writerows(results)

        grouped = defaultdict(list)
        resolutions = defaultdict(set)
        internal_resolutions = defaultdict(set)
        geometry = defaultdict(list)
        for row in results:
            display, ui = row["run"].split("_ui-")
            view, _, mode = row["label"].split("/")
            key = (display, ui, view, mode)
            grouped[key].append(row["meanMs"])
            resolutions[key].add((row["windowWidth"], row["windowHeight"],
                                  row["backBufferWidth"], row["backBufferHeight"]))
            internal_resolutions[key].add((row["fixedInternalWidth"],
                                           row["fixedInternalHeight"]))
            geometry[key].append((row["averageChunks"], row["averageTriangles"]))
        means = {key: sum(values) / len(values) for key, values in grouped.items()}
        deltas = []
        invalid_pairs = set()

        def add_delta(kind: str, left: tuple, right: tuple) -> None:
            if left in means and right in means:
                same_resolution = resolutions[left] == resolutions[right] and len(resolutions[left]) == 1
                same_internal = (internal_resolutions[left] == internal_resolutions[right]
                                 and len(internal_resolutions[left]) == 1)
                same_geometry = all(
                    abs(sum(p[i] for p in geometry[left]) / len(geometry[left])
                        - sum(p[i] for p in geometry[right]) / len(geometry[right])) <= 1
                    for i in (0, 1)
                )
                if kind == "ui-cost" and (not same_resolution or not same_geometry):
                    invalid_pairs.add(
                        f"{left[0]}/{left[2]}/{left[3]}: UI on/off used different resolution or terrain geometry"
                    )
                    return
                if kind == "matched-display-cost" and (not same_internal or not same_geometry):
                    invalid_pairs.add(
                        f"{left[2]}/{left[3]}: matched display comparison used different internal resolution or geometry"
                    )
                    return
                deltas.append({
                    "kind": kind,
                    "left": "/".join(left),
                    "right": "/".join(right),
                    "leftMeanMs": round(means[left], 4),
                    "rightMeanMs": round(means[right], 4),
                    "deltaMs": round(means[right] - means[left], 4),
                    "sameResolution": same_resolution,
                    "sameInternalResolution": same_internal,
                    "sameGeometry": same_geometry,
                })

        for view in ("panorama", "ground-70m"):
            for mode in ("ClearOnly", "FlatTerrain", "CurrentTerrain", "inspector-open"):
                for display in ("window", "fullscreen"):
                    add_delta("ui-cost", (display, "off", view, mode),
                              (display, "on", view, mode))
                for ui in ("off", "on"):
                    add_delta("fullscreen-cost", ("window", ui, view, mode),
                              ("fullscreen", ui, view, mode))
            for display in ("window", "fullscreen"):
                for ui in ("off", "on"):
                    add_delta("terrain-geometry-cost", (display, ui, view, "ClearOnly"),
                              (display, ui, view, "FlatTerrain"))
                    add_delta("terrain-shader-cost", (display, ui, view, "FlatTerrain"),
                              (display, ui, view, "CurrentTerrain"))
                    add_delta("inspector-cost", (display, ui, view, "CurrentTerrain"),
                              (display, ui, view, "inspector-open"))
            for mode in ("ClearOnly", "FlatTerrain", "CurrentTerrain", "inspector-open"):
                add_delta("matched-display-cost", ("window", "off_matched", view, mode),
                          ("fullscreen", "off_matched", view, mode))
        with (session / "deltas.csv").open("w", newline="", encoding="utf-8") as output:
            writer = csv.DictWriter(output, fieldnames=[
                "kind", "left", "right", "leftMeanMs", "rightMeanMs", "deltaMs",
                "sameResolution", "sameInternalResolution", "sameGeometry",
            ])
            writer.writeheader()
            writer.writerows(deltas)
        failures.extend(sorted(invalid_pairs))
        if any(row["kind"] == "fullscreen-cost" and not row["sameResolution"] for row in deltas):
            warnings.append("Fullscreen and windowed resolution differ; fullscreen deltas include pixel-count and aspect-ratio changes.")
        if any(row["kind"] == "fullscreen-cost" and not row["sameGeometry"] for row in deltas):
            warnings.append("Fullscreen and windowed terrain geometry differ; compare drawn chunks and triangles before attributing the delta to the display mode.")
    (session / "matrix.json").write_text(
        json.dumps({"commit": commit, "seconds": args.seconds, "rounds": args.rounds,
                    "failures": failures, "warnings": warnings, "stages": results}, indent=2),
        encoding="utf-8",
    )
    print(f"\nResults: {session}")
    for failure in failures:
        print("INVALID:", failure)
    for warning in warnings:
        print("NOTE:", warning)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
