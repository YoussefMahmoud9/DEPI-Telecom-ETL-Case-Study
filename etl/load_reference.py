from __future__ import annotations

import re
from pathlib import Path

import pyodbc


PAIR = re.compile(r"\(\s*(\d+)\s*,\s*(\d+)\s*\)")


def load(connection_string: str, script_path: Path) -> int:
    text = script_path.read_text(encoding="utf-8-sig")
    marker = text.lower().find("insert into dim_imsi_reference")
    if marker < 0:
        raise ValueError("Reference INSERT statement not found")
    pairs = [(imsi, int(subscriber_id)) for imsi, subscriber_id in PAIR.findall(text[marker:])]
    if not pairs:
        raise ValueError("No reference rows found")
    with pyodbc.connect(connection_string, autocommit=False) as connection:
        cursor = connection.cursor()
        existing = cursor.execute("SELECT COUNT(*) FROM dbo.dim_imsi_reference").fetchone()[0]
        if existing:
            return int(existing)
        cursor.fast_executemany = True
        cursor.executemany("INSERT INTO dbo.dim_imsi_reference(imsi, subscriber_id) VALUES (?, ?)", pairs)
        connection.commit()
        return len(pairs)


if __name__ == "__main__":
    import json
    config = json.loads(Path("config.json").read_text(encoding="utf-8"))
    count = load(config["connection_string"], Path("sql/02_reference.sql"))
    print(f"reference_count={count}")
