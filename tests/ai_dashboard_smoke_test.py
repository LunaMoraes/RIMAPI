#!/usr/bin/env python3
"""
Smoke test for RIMAPI custom AI, Dashboard, and Experiment endpoints.
Requires RimWorld 1.7 running with RIMAPI loaded and an active colony save.

Usage:
  python tests/ai_dashboard_smoke_test.py
  python tests/ai_dashboard_smoke_test.py --write-tests
  python tests/ai_dashboard_smoke_test.py --base-url http://localhost:8765
"""

import argparse
import json
import sys
import urllib.error
import urllib.request


def request_json(base_url: str, method: str, path: str, body=None):
    url = f"{base_url.rstrip('/')}{path}"
    data = None
    headers = {"Accept": "application/json"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
        headers["Content-Length"] = str(len(data))
    elif method in ("POST", "PUT", "DELETE"):
        data = b"{}"
        headers["Content-Type"] = "application/json"
        headers["Content-Length"] = "2"

    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
            return resp.status, payload
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", errors="replace")
        try:
            payload = json.loads(raw)
        except json.JSONDecodeError:
            payload = {"raw": raw}
        return e.code, payload


def check(name: str, condition: bool, detail: str = ""):
    status = "PASS" if condition else "FAIL"
    suffix = f" ({detail})" if detail else ""
    print(f"[{status}] {name}{suffix}")
    return condition


def main():
    parser = argparse.ArgumentParser(description="RIMAPI Custom AI/Dashboard/Experiment Smoke Test")
    parser.add_argument("--base-url", default="http://localhost:8765", help="RIMAPI base URL")
    parser.add_argument("--write-tests", action="store_true", help="Also run non-destructive POST tests")
    args = parser.parse_args()

    results = []

    # 1. Core health
    code, body = request_json(args.base_url, "GET", "/api/v1/version")
    results.append(check("GET /api/v1/version", code == 200 and body.get("success") is True, f"http={code}"))

    # 2. AI Snapshot
    code, snap = request_json(args.base_url, "GET", "/api/v1/ai/snapshot")
    snap_data = snap.get("data") or {}
    results.append(
        check(
            "GET /api/v1/ai/snapshot",
            code == 200 and snap.get("success") is True and "colonists" in snap_data and "time" in snap_data,
            f"colonists={len(snap_data.get('colonists', []))}, tick={snap_data.get('time', {}).get('tick')}",
        )
    )

    # 3. AI Areas, Letters, Stockpiles, Traders
    for path in (
        "/api/v1/ai/areas",
        "/api/v1/ai/letters",
        "/api/v1/ai/zones/stockpile",
        "/api/v1/ai/trade/traders",
    ):
        code, body = request_json(args.base_url, "GET", path)
        results.append(check(f"GET {path}", code == 200 and body.get("success") is True, f"http={code}"))

    # 4. Dashboard Overview & Actions
    code, ov = request_json(args.base_url, "GET", "/api/v1/dashboard/overview?limit=10")
    ov_data = ov.get("data") or {}
    results.append(
        check(
            "GET /api/v1/dashboard/overview",
            code == 200 and ov.get("success") is True and "snapshot" in ov_data and "recent_actions" in ov_data,
            f"actions={len(ov_data.get('recent_actions', []))}, sse={ov_data.get('sse_client_count')}",
        )
    )

    code, acts = request_json(args.base_url, "GET", "/api/v1/dashboard/actions?limit=10")
    results.append(check("GET /api/v1/dashboard/actions", code == 200 and acts.get("success") is True, f"http={code}"))

    # 5. Experiment Status
    code, exp = request_json(args.base_url, "GET", "/api/v1/experiment/status")
    results.append(
        check(
            "GET /api/v1/experiment/status",
            code == 200 and exp.get("success") is True,
            f"wealth={exp.get('data', {}).get('colony_wealth')}",
        )
    )

    # 6. Validation error checks
    code, err_body = request_json(args.base_url, "GET", "/api/v1/ai/snapshot?map_id=not_an_int")
    results.append(
        check(
            "Validation 400 on invalid map_id",
            code == 400 and err_body.get("success") is False,
            f"http={code}",
        )
    )

    code, err_body = request_json(
        args.base_url,
        "POST",
        "/api/v1/order/designate/area",
        {"map_id": 0, "type": "invalid_type_xyz", "point_a": {"x": 0, "y": 0, "z": 0}, "point_b": {"x": 0, "y": 0, "z": 0}},
    )
    results.append(
        check(
            "Validation 400 on invalid designation type",
            code == 400 and err_body.get("success") is False,
            f"http={code}",
        )
    )

    # 7. Optional write tests
    if args.write_tests:
        code, body = request_json(
            args.base_url,
            "POST",
            "/api/v1/experiment/annotation",
            {"note": "Smoke test marker", "show_in_game_banner": False},
        )
        results.append(check("POST /api/v1/experiment/annotation", code == 200 and body.get("success") is True))

        code, body = request_json(
            args.base_url,
            "POST",
            "/api/v1/order/designate/area",
            {"map_id": 0, "type": "chop", "point_a": {"x": 1, "y": 0, "z": 1}, "point_b": {"x": 1, "y": 0, "z": 1}},
        )
        results.append(
            check(
                "POST /api/v1/order/designate/area (chop 1x1)",
                code == 200 and body.get("success") is True and "designated_count" in (body.get("data") or {}),
            )
        )

    passed = sum(1 for r in results if r)
    total = len(results)
    print(f"\nSummary: {passed}/{total} checks passed.")
    sys.exit(0 if passed == total else 1)


if __name__ == "__main__":
    main()
