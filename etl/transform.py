from __future__ import annotations

import csv
from datetime import datetime
from pathlib import Path
from typing import Any

REQUIRED_COLUMNS = ("id", "imsi", "imei", "cell", "lac", "event_type", "event_ts")


def parse_int(value: str, name: str) -> int:
    if value == "":
        raise ValueError(f"{name} is NULL")
    try:
        return int(value)
    except ValueError as exc:
        raise ValueError(f"{name} is not an integer") from exc


def parse_event_ts(value: str) -> datetime:
    if value == "":
        raise ValueError("EVENT_TS is NULL")
    for fmt in ("%d/%m/%Y %H:%M:%S", "%m/%d/%Y %H:%M:%S", "%d/%m/%Y %H:%M", "%m/%d/%Y %H:%M"):
        try:
            return datetime.strptime(value, fmt)
        except ValueError:
            pass
    raise ValueError("EVENT_TS is invalid")


def transform_row(raw: dict[str, str], lookup: dict[str, int], file_name: str, file_hash: str, row_number: int) -> tuple[str, dict[str, Any]]:
    values = {key: (raw.get(key) or "").strip() for key in REQUIRED_COLUMNS}
    reasons: list[str] = []
    if values["imsi"] == "": reasons.append("IMSI is NULL")
    if values["cell"] == "": reasons.append("CELL is NULL")
    if values["lac"] == "": reasons.append("LAC is NULL")
    try: transaction_id = parse_int(values["id"], "ID")
    except ValueError as exc: transaction_id = None; reasons.append(str(exc))
    try: cell = parse_int(values["cell"], "CELL")
    except ValueError as exc: cell = None; reasons.append(str(exc)) if values["cell"] else None
    try: lac = parse_int(values["lac"], "LAC")
    except ValueError as exc: lac = None; reasons.append(str(exc)) if values["lac"] else None
    try: event_ts = parse_event_ts(values["event_ts"])
    except ValueError as exc: event_ts = None; reasons.append(str(exc))
    imei = values["imei"] or None
    tac, snr = ("-99999", "-99999") if imei is None or len(imei) < 14 else (imei[:8], imei[-6:])
    if reasons:
        return "rejected", {"source_file_name": file_name, "source_file_hash": file_hash, "source_row_number": row_number, "transaction_id_raw": values["id"], "imsi_raw": values["imsi"], "imei_raw": values["imei"], "cell_raw": values["cell"], "lac_raw": values["lac"], "event_type_raw": values["event_type"], "event_ts_raw": values["event_ts"], "rejection_reason": "; ".join(reasons)}
    return "valid", {"transaction_id": transaction_id, "imsi": values["imsi"], "subscriber_id": lookup.get(values["imsi"], -99999), "tac": tac, "snr": snr, "imei": imei, "cell": cell, "lac": lac, "event_type": values["event_type"] or None, "event_ts": event_ts, "source_file_name": file_name, "source_file_hash": file_hash, "source_row_number": row_number}


def read_file(path: Path, lookup: dict[str, int], file_hash: str) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    valid: list[dict[str, Any]] = []
    rejected: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="|")
        if reader.fieldnames != list(REQUIRED_COLUMNS): raise ValueError(f"Unexpected header in {path.name}: {reader.fieldnames}")
        for row_number, raw in enumerate(reader, start=2):
            if None in raw: raw = {key: raw.get(key, "") for key in REQUIRED_COLUMNS}
            kind, result = transform_row(raw, lookup, path.name, file_hash, row_number)
            (valid if kind == "valid" else rejected).append(result)
    return valid, rejected
