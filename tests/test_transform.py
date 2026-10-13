import hashlib
import unittest
from pathlib import Path
from etl.transform import read_file, transform_row

LOOKUP = {"001010001": 1001, "001010002": 1002}

class TransformTests(unittest.TestCase):
    def test_valid_and_imei_derivation(self):
        kind, row = transform_row({"id":"1","imsi":"001010001","imei":"12345678901234","cell":"123","lac":"12","event_type":"1","event_ts":"01/01/2026 10:00:00"}, LOOKUP, "x.csv", "h", 2)
        self.assertEqual(kind, "valid"); self.assertEqual(row["subscriber_id"], 1001); self.assertEqual(row["tac"], "12345678"); self.assertEqual(row["snr"], "901234")

    def test_required_field_rejections(self):
        base = {"id":"1","imsi":"001010001","imei":"12345678901234","cell":"123","lac":"12","event_type":"1","event_ts":"01/01/2026 10:00:00"}
        for field, expected in (("imsi", "IMSI is NULL"), ("cell", "CELL is NULL"), ("lac", "LAC is NULL"), ("event_ts", "EVENT_TS is NULL")):
            row = dict(base); row[field] = ""; kind, rejected = transform_row(row, LOOKUP, "x.csv", "h", 2); self.assertEqual(kind, "rejected"); self.assertIn(expected, rejected["rejection_reason"])

    def test_invalid_unknown_and_short_or_null_imei(self):
        base = {"id":"1","imsi":"009999999","imei":"123","cell":"123","lac":"12","event_type":"1","event_ts":"bad"}
        kind, rejected = transform_row(base, LOOKUP, "x.csv", "h", 2); self.assertEqual(kind, "rejected"); self.assertIn("EVENT_TS is invalid", rejected["rejection_reason"])
        base["event_ts"] = "01/01/2026 10:00:00"; kind, valid = transform_row(base, LOOKUP, "x.csv", "h", 2); self.assertEqual(kind, "valid"); self.assertEqual(valid["subscriber_id"], -99999); self.assertEqual(valid["tac"], "-99999")
        base["imei"] = ""; _, valid = transform_row(base, LOOKUP, "x.csv", "h", 2); self.assertEqual(valid["tac"], "-99999")

    def test_synthetic_sample_file_parses(self):
        path = Path(__file__).parents[1] / "sample_data" / "synthetic_transactions.csv"
        valid, rejected = read_file(path, LOOKUP, hashlib.sha256(path.read_bytes()).hexdigest())
        self.assertEqual(len(valid), 2); self.assertEqual(len(rejected), 1)

if __name__ == "__main__": unittest.main()
