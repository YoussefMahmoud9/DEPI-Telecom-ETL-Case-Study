from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from pathlib import Path

from etl.transform import read_file


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def connect(connection_string: str):
    try:
        import pyodbc
    except ImportError as exc:
        raise RuntimeError("pyodbc is required for database execution; install requirements.txt") from exc
    return pyodbc.connect(connection_string, autocommit=False)


def load_lookup(cursor) -> dict[str, int]:
    return {str(imsi): int(subscriber_id) for imsi, subscriber_id in cursor.execute("SELECT imsi, subscriber_id FROM dbo.dim_imsi_reference")}


def process_file(cursor, path: Path, archive: Path, lookup: dict[str, int]) -> dict:
    file_hash = sha256(path)
    previous = cursor.execute("SELECT status FROM dbo.etl_file_audit WHERE source_file_hash = ?", file_hash).fetchone()
    if previous and previous[0] == "SUCCESS":
        return {"file": path.name, "status": "SKIPPED_ALREADY_PROCESSED", "total": 0, "loaded": 0, "rejected": 0}
    if previous:
        # A prior RUNNING/FAILED attempt is recoverable because its source file
        # remains in the inbox. Remove any remnants before retrying.
        cursor.execute("DELETE FROM dbo.fact_transaction WHERE source_file_hash = ?", file_hash)
        cursor.execute("DELETE FROM dbo.etl_rejected_record WHERE source_file_hash = ?", file_hash)
        cursor.execute("DELETE FROM dbo.etl_file_audit WHERE source_file_hash = ?", file_hash)
        cursor.connection.commit()
    valid, rejected = read_file(path, lookup, file_hash)
    seen: set[int] = set()
    deduped: list[dict] = []
    for row in valid:
        if row["transaction_id"] in seen:
            rejected.append({"source_file_name": path.name, "source_file_hash": file_hash, "source_row_number": row["source_row_number"], "transaction_id_raw": str(row["transaction_id"]), "imsi_raw": row["imsi"], "imei_raw": row["imei"] or "", "cell_raw": str(row["cell"]), "lac_raw": str(row["lac"]), "event_type_raw": row["event_type"] or "", "event_ts_raw": str(row["event_ts"]), "rejection_reason": "Duplicate transaction_id within source file"})
        else:
            seen.add(row["transaction_id"]); deduped.append(row)
    valid = deduped
    total = len(valid) + len(rejected)
    cursor.execute("INSERT INTO dbo.etl_file_audit(source_file_name, source_file_hash, source_file_size, status, total_rows) VALUES (?, ?, ?, 'RUNNING', ?)", path.name, file_hash, path.stat().st_size, total)
    cursor.connection.commit()
    try:
        if valid:
            # Keep the explicit column mapping readable and avoid relying on dict order.
            cursor.executemany("INSERT INTO dbo.fact_transaction(transaction_id, imsi, subscriber_id, tac, snr, imei, cell, lac, event_type, event_ts, source_file_name, source_file_hash, source_row_number) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", [tuple(row[k] for k in ("transaction_id","imsi","subscriber_id","tac","snr","imei","cell","lac","event_type","event_ts","source_file_name","source_file_hash","source_row_number")) for row in valid])
        if rejected:
            cursor.executemany("INSERT INTO dbo.etl_rejected_record(source_file_name, source_file_hash, source_row_number, transaction_id_raw, imsi_raw, imei_raw, cell_raw, lac_raw, event_type_raw, event_ts_raw, rejection_reason) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", [tuple(row[k] for k in ("source_file_name","source_file_hash","source_row_number","transaction_id_raw","imsi_raw","imei_raw","cell_raw","lac_raw","event_type_raw","event_ts_raw","rejection_reason")) for row in rejected])
        cursor.execute("UPDATE dbo.etl_file_audit SET status='SUCCESS', completed_utc=SYSUTCDATETIME(), loaded_rows=?, rejected_rows=? WHERE source_file_hash=?", len(valid), len(rejected), file_hash)
        cursor.connection.commit()
        archive.mkdir(parents=True, exist_ok=True)
        target = archive / path.name
        if target.exists(): target = archive / f"{path.stem}_{file_hash[:8]}{path.suffix}"
        shutil.move(str(path), str(target))
        return {"file": path.name, "status": "SUCCESS", "total": total, "loaded": len(valid), "rejected": len(rejected), "archived": str(target)}
    except Exception as exc:
        cursor.connection.rollback()
        cursor.execute("UPDATE dbo.etl_file_audit SET status='FAILED', completed_utc=SYSUTCDATETIME(), error_message=? WHERE source_file_hash=?", str(exc)[:4000], file_hash)
        cursor.connection.commit()
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description="Load pipe-delimited Telecom transaction files into SQL Server")
    parser.add_argument("--config", default="config.json")
    args = parser.parse_args()
    config = json.loads(Path(args.config).read_text(encoding="utf-8"))
    input_folder, archive_folder = Path(config["input_folder"]), Path(config["archive_folder"])
    with connect(config["connection_string"]) as connection:
        cursor = connection.cursor(); lookup = load_lookup(cursor); results = []
        for path in sorted(input_folder.glob("*.csv")):
            results.append(process_file(cursor, path, archive_folder, lookup))
    print(json.dumps(results, indent=2, default=str))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
