#!/usr/bin/env python3
"""
Smoke test for RIMAPI custom AI, Command Surface, Dashboard, and Experiment endpoints.
Requires RimWorld 1.7 running with RIMAPI loaded and an active colony save.

Usage:
  python tests/ai_dashboard_smoke_test.py
  python tests/ai_dashboard_smoke_test.py --write-tests
  python tests/ai_dashboard_smoke_test.py --base-url http://localhost:8765
"""

import argparse
import http.client
import json
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request


def request_raw(base_url: str, method: str, path: str, raw_body=None, extra_headers=None):
    url = f"{base_url.rstrip('/')}{path}"
    data = None
    headers = {"Accept": "application/json"}
    if raw_body is not None:
        data = raw_body if isinstance(raw_body, bytes) else raw_body.encode("utf-8")
        headers["Content-Type"] = "application/json"
        headers["Content-Length"] = str(len(data))
    elif method in ("POST", "PUT", "PATCH", "DELETE"):
        data = b"{}"
        headers["Content-Type"] = "application/json"
        headers["Content-Length"] = "2"

    if extra_headers:
        headers.update(extra_headers)

    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
            return resp.status, payload, dict(resp.headers)
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", errors="replace")
        try:
            payload = json.loads(raw)
        except json.JSONDecodeError:
            payload = {"raw": raw}
        return e.code, payload, dict(e.headers)


def request_json(base_url: str, method: str, path: str, body=None, extra_headers=None):
    raw_body = json.dumps(body) if body is not None else None
    code, payload, _ = request_raw(base_url, method, path, raw_body=raw_body, extra_headers=extra_headers)
    return code, payload


def assert_all_snake_case(obj, path=""):
    if isinstance(obj, dict):
        for k, v in obj.items():
            if any(c.isupper() for c in k):
                return f"non-snake_case key '{path}.{k}'"
            err = assert_all_snake_case(v, f"{path}.{k}")
            if err:
                return err
    elif isinstance(obj, list):
        for i, item in enumerate(obj[:10]):
            err = assert_all_snake_case(item, f"{path}[{i}]")
            if err:
                return err
    return None


def check(name: str, condition: bool, detail: str = ""):
    status = "PASS" if condition else "FAIL"
    suffix = f" ({detail})" if detail else ""
    print(f"[{status}] {name}{suffix}")
    return condition


def verify_sse_ai_action(base_url: str):
    parsed = urllib.parse.urlparse(base_url)
    host = parsed.hostname or "localhost"
    port = parsed.port or (443 if parsed.scheme == "https" else 80)

    received = {}
    ready = threading.Event()

    def listen():
        try:
            conn = http.client.HTTPConnection(host, port, timeout=5)
            conn.request("GET", "/api/v1/events", headers={"Accept": "text/event-stream"})
            resp = conn.getresponse()
            ready.set()
            cur_event = None
            deadline = time.time() + 4.0
            while time.time() < deadline:
                line_bytes = resp.fp.readline()
                if not line_bytes:
                    break
                line = line_bytes.decode("utf-8", errors="replace").rstrip("\r\n")
                if line.startswith("event:"):
                    cur_event = line.split(":", 1)[1].strip()
                elif line.startswith("data:") and cur_event == "ai_action":
                    raw = line.split(":", 1)[1].strip()
                    received["data"] = json.loads(raw)
                    break
            conn.close()
        except Exception as exc:
            received["error"] = str(exc)
            ready.set()

    t = threading.Thread(target=listen, daemon=True)
    t.start()
    ready.wait(timeout=2.0)
    time.sleep(0.2)

    request_json(
        base_url,
        "POST",
        "/api/v1/experiment/annotation",
        {"note": "SSE snake_case probe", "show_in_game_banner": False},
        extra_headers={"X-RimApi-Actor": "sse-smoke-probe"},
    )
    t.join(timeout=4.5)

    ev = received.get("data")
    if not ev:
        return False, f"no ai_action received ({received.get('error', 'timeout')})"
    snake_err = assert_all_snake_case(ev, "sse")
    if snake_err:
        return False, snake_err
    has_fields = all(k in ev for k in ("timestamp_utc", "status_code", "tier", "actor"))
    return has_fields, f"tier={ev.get('tier')}, actor={ev.get('actor')}"


def main():
    parser = argparse.ArgumentParser(description="RIMAPI Custom AI/Dashboard/Experiment Smoke Test")
    parser.add_argument("--base-url", default="http://localhost:8765", help="RIMAPI base URL")
    parser.add_argument("--write-tests", action="store_true", help="Also run non-destructive POST & SSE tests")
    args = parser.parse_args()

    results = []

    # 1. Core health
    code, body = request_json(args.base_url, "GET", "/api/v1/version")
    results.append(check("GET /api/v1/version", code == 200 and body.get("success") is True, f"http={code}"))

    # 2. Compact AI Snapshot + Fair-Play Defaults + Budget
    code, snap = request_json(args.base_url, "GET", "/api/v1/ai/snapshot")
    snap_data = snap.get("data") or {}
    snap_bytes = len(json.dumps(snap).encode("utf-8"))
    snake_err = assert_all_snake_case(snap_data, "snapshot")
    results.append(
        check(
            "GET /api/v1/ai/snapshot (compact & snake_case)",
            code == 200
            and snap.get("success") is True
            and snake_err is None
            and snap_data.get("omniscient") is False
            and snap_bytes <= 32768
            and "colonists" in snap_data
            and "time" in snap_data,
            f"bytes={snap_bytes}, colonists={len(snap_data.get('colonists', []))}, snake_err={snake_err}",
        )
    )

    # 2b. Expanded Snapshot with ?include=all&omniscient=true&since_tick=0
    code, full_snap = request_json(args.base_url, "GET", "/api/v1/ai/snapshot?include=all&omniscient=true&since_tick=0")
    full_data = full_snap.get("data") or {}
    expected_sections = ("game_conditions", "designations", "prisoners", "guests", "animals", "mechs")
    results.append(
        check(
            "GET /api/v1/ai/snapshot?include=all&omniscient=true&since_tick=0",
            code == 200
            and full_snap.get("success") is True
            and full_data.get("omniscient") is True
            and "ticks_elapsed" in full_data
            and all(k in full_data for k in expected_sections),
            f"http={code}",
        )
    )

    # 3. Read endpoints
    for path in (
        "/api/v1/ai/designations",
        "/api/v1/ai/policies",
        "/api/v1/ai/areas",
        "/api/v1/ai/letters",
        "/api/v1/ai/zones/stockpile",
        "/api/v1/ai/trade/traders",
        "/api/v1/ai/designators",
    ):
        code, body = request_json(args.base_url, "GET", path)
        snake_err = assert_all_snake_case(body.get("data"), path)
        results.append(check(f"GET {path}", code == 200 and body.get("success") is True and snake_err is None, f"http={code}, snake_err={snake_err}"))

    # 4. Dashboard Overview & Actions
    code, ov = request_json(args.base_url, "GET", "/api/v1/dashboard/overview?limit=10")
    ov_data = ov.get("data") or {}
    results.append(
        check(
            "GET /api/v1/dashboard/overview",
            code == 200
            and ov.get("success") is True
            and "program_state" in ov_data
            and "is_playing" in ov_data
            and "snapshot" in ov_data
            and "recent_actions" in ov_data,
            f"actions={len(ov_data.get('recent_actions', []))}, sse={ov_data.get('sse_client_count')}",
        )
    )

    code, acts = request_json(args.base_url, "GET", "/api/v1/dashboard/actions?limit=10&include_reads=true")
    acts_data = acts.get("data") or []
    has_tier = len(acts_data) == 0 or ("tier" in acts_data[0] and "actor" in acts_data[0])
    results.append(check("GET /api/v1/dashboard/actions", code == 200 and acts.get("success") is True and has_tier, f"http={code}"))

    # 5. Experiment Status
    code, exp = request_json(args.base_url, "GET", "/api/v1/experiment/status")
    results.append(
        check(
            "GET /api/v1/experiment/status",
            code == 200 and exp.get("success") is True,
            f"wealth={exp.get('data', {}).get('colony_wealth')}",
        )
    )

    # 6. Validation & Security checks
    code, err_body = request_json(args.base_url, "GET", "/api/v1/ai/snapshot?map_id=not_an_int")
    results.append(check("Validation 400 on invalid map_id", code == 400 and err_body.get("success") is False, f"http={code}"))

    code, err_body = request_json(args.base_url, "GET", "/api/v1/ai/trade/deal")
    results.append(check("Validation 400 on missing trader_id", code == 400 and err_body.get("success") is False, f"http={code}"))

    code, err_body = request_json(args.base_url, "GET", "/api/v1/ai/pawn/options")
    results.append(check("Validation 400 on missing float-menu params", code == 400 and err_body.get("success") is False, f"http={code}"))

    code, err_body = request_json(args.base_url, "GET", "/api/v1/ai/gizmos")
    results.append(check("Validation 400 on missing gizmo target", code == 400 and err_body.get("success") is False, f"http={code}"))

    code, err_body, _ = request_raw(
        args.base_url,
        "POST",
        "/api/v1/order/designate/area",
        raw_body="{bad json",
    )
    results.append(check("Validation 400 on malformed JSON", code == 400 and err_body.get("success") is False, f"http={code}"))

    code, err_body = request_json(
        args.base_url,
        "POST",
        "/api/v1/order/designate/area",
        {"map_id": 0, "type": "invalid_type_xyz", "point_a": {"x": 0, "y": 0, "z": 0}, "point_b": {"x": 0, "y": 0, "z": 0}},
    )
    results.append(check("Validation 400 on invalid designation type", code == 400 and err_body.get("success") is False, f"http={code}"))

    code, err_body = request_json(args.base_url, "POST", "/api/v1/ai/time/advance", {"ticks": 0})
    results.append(check("Validation 400 on ticks=0", code == 400 and err_body.get("success") is False, f"http={code}"))

    code, err_body = request_json(args.base_url, "GET", "/api/v1/version", extra_headers={"Origin": "https://evil.example"})
    results.append(check("Security 403 on disallowed Origin", code == 403, f"http={code}"))

    # 7. Optional write + SSE tests
    if args.write_tests:
        code, body = request_json(
            args.base_url,
            "POST",
            "/api/v1/experiment/annotation",
            {"note": "Smoke test marker", "show_in_game_banner": False},
            extra_headers={"X-RimApi-Actor": "smoke-test-agent"},
        )
        results.append(check("POST /api/v1/experiment/annotation", code == 200 and body.get("success") is True))

        code, body = request_json(
            args.base_url,
            "POST",
            "/api/v1/order/designate/area",
            {"map_id": 0, "type": "chop", "point_a": {"x": 1, "y": 0, "z": 1}, "point_b": {"x": 1, "y": 0, "z": 1}},
        )
        res_data = body.get("data") or {}
        results.append(
            check(
                "POST /api/v1/order/designate/area (chop 1x1)",
                code == 200 and body.get("success") is True and "designated_count" in res_data and "affected_count" in res_data and "skipped_fogged_count" in res_data,
            )
        )

        colonists = snap_data.get("colonists") or []
        if colonists:
            pawn = colonists[0]
            pid = pawn["id"]
            pos = pawn["position"]
            code, opts = request_json(args.base_url, "GET", f"/api/v1/ai/pawn/options?pawn_id={pid}&x={pos['x']}&z={pos['z']}")
            results.append(check("GET /api/v1/ai/pawn/options (first colonist)", code == 200 and opts.get("success") is True, f"count={len(opts.get('data') or [])}"))

            code, giz = request_json(args.base_url, "GET", f"/api/v1/ai/gizmos?pawn_id={pid}")
            results.append(check("GET /api/v1/ai/gizmos (first colonist)", code == 200 and giz.get("success") is True, f"count={len(giz.get('data') or [])}"))

        code, adv = request_json(
            args.base_url,
            "POST",
            "/api/v1/ai/time/advance",
            {"ticks": 5, "stop_on_letter": True, "stop_on_hostile": False, "pause_after": True},
        )
        adv_data = adv.get("data") or {}
        results.append(
            check(
                "POST /api/v1/ai/time/advance (5 ticks)",
                code == 200 and adv.get("success") is True and adv_data.get("ticks_executed", 0) >= 1 and adv_data.get("is_paused") is True,
                f"executed={adv_data.get('ticks_executed')}, reason={adv_data.get('stop_reason')}",
            )
        )

        sse_ok, sse_detail = verify_sse_ai_action(args.base_url)
        results.append(check("SSE ai_action snake_case event", sse_ok, sse_detail))

    passed = sum(1 for r in results if r)
    total = len(results)
    print(f"\nSummary: {passed}/{total} checks passed.")
    sys.exit(0 if passed == total else 1)


if __name__ == "__main__":
    main()
