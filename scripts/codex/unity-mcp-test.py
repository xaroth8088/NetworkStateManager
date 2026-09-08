"""Run tests in an already-open Editor via its local Unity MCP bridge."""
import argparse
import json
import pathlib
import socket
import struct
import time
import re
import hashlib


def call(port, command, params=None):
    with socket.create_connection(("127.0.0.1", port), timeout=30) as sock:
        stream = sock.makefile("rb")
        if stream.readline() != b"WELCOME UNITY-MCP 1 FRAMING=1\n":
            raise RuntimeError("Unsupported Unity MCP handshake")
        payload = json.dumps({"type": command, "params": params or {}}).encode()
        sock.sendall(struct.pack(">Q", len(payload)) + payload)
        header = stream.read(8)
        if len(header) != 8:
            raise ConnectionError("Truncated Unity MCP response during Editor reload")
        size = struct.unpack(">Q", header)[0]
        if size > 32 * 1024 * 1024:
            raise RuntimeError("Unity MCP response exceeds 32 MiB")
        reply = json.loads(stream.read(size))
        if reply.get("status") == "error":
            raise RuntimeError(reply)
        result = reply.get("result", reply)
        if not result.get("success", True):
            raise RuntimeError(result)
        return result.get("data", result)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["editmode", "playmode"])
    parser.add_argument("project")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--assemblies", default="")
    parser.add_argument("--filter", default="")
    parser.add_argument("--categories", default="")
    parser.add_argument("--timeout", type=int, default=600)
    parser.add_argument("--job", help="Resume monitoring an existing job after transport interruption")
    args = parser.parse_args()
    root = pathlib.Path(args.project).resolve()
    info = call(args.port, "get_project_info")
    if pathlib.Path(info["projectRoot"]).resolve() != root:
        raise RuntimeError(f"Port {args.port} belongs to {info['projectRoot']}, not {root}")
    console = call(args.port, "read_console", {"action": "get", "types": ["error"], "count": 1000, "format": "plain"})
    if re.search(r"error CS\d+:", json.dumps(console)):
        raise RuntimeError("Editor has compiler errors; refusing to test stale assemblies")
    def fingerprints():
        return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
                for folder in ("Assets/Runtime", "Assets/Scripts", "Assets/Tests") for p in (root / folder).rglob("*.cs")}
    source_hashes = fingerprints()
    output = root / "Logs" / "codex-tests"
    output.mkdir(parents=True, exist_ok=True)
    receipt = output / f"{args.mode}-mcp-sources.json"
    if args.job:
        source_hashes = json.loads(receipt.read_text(encoding="utf-8"))
        if fingerprints() != source_hashes:
            raise RuntimeError("Current sources differ from the resumed test run")
    params = {"mode": "EditMode" if args.mode == "editmode" else "PlayMode"}
    for key, value in [("assemblyNames", args.assemblies), ("groupNames", args.filter), ("categoryNames", args.categories)]:
        if value:
            params[key] = value.split(";")
    job = {"job_id": args.job} if args.job else call(args.port, "run_tests", params)
    if not args.job:
        receipt.write_text(json.dumps(source_hashes, indent=2), encoding="utf-8")
    print(f"Unity {info['unityVersion']}: {root.name}, job {job['job_id']}", flush=True)
    deadline = time.monotonic() + args.timeout
    while time.monotonic() < deadline:
        try:
            result = call(args.port, "get_test_job", {"job_id": job["job_id"], "includeDetails": True, "includeFailedTests": True})
        except (TimeoutError, ConnectionError, OSError) as error:
            print(f"Waiting for Editor: {error}", flush=True)
            time.sleep(2)
            continue
        (output / f"{args.mode}-mcp-results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
        if result["status"] not in ("running", "queued"):
            if fingerprints() != source_hashes:
                raise RuntimeError("Sources changed during the test run; results cannot validate the current files")
            summary = (result.get("result") or {}).get("summary", {})
            progress = result.get("progress") or {}
            print(json.dumps({"job_id": result["job_id"], "status": result["status"],
                              "summary": summary, "progress": progress}, indent=2), flush=True)
            total = summary.get("total", progress.get("total", 0))
            if result["status"] != "succeeded" or not total or summary.get("failed", 0) or progress.get("completed") != total or progress.get("failures_so_far"):
                raise SystemExit(1)
            return
        time.sleep(2)
    raise RuntimeError(f"Timed out monitoring {job['job_id']}; Editor job may still be running")


if __name__ == "__main__":
    main()
