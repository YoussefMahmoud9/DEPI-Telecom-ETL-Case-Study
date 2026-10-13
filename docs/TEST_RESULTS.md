# Verification record

Run date: 2026-10-13 (Africa/Cairo)

The original assignment files are intentionally excluded from this public repository. The dataset-specific figures below are private local verification evidence; the included smoke test uses only the synthetic data under `sample_data/`.

## Completed locally

- `python -m unittest discover -s tests -v`: **4 tests passed**.
- `python -m compileall -q etl tests`: **passed**.
- All 13 supplied pipe-delimited files parsed without modifying the originals.
- Supplied data inventory: **1,832 data rows**.
- SQL Server baseline execution: **1,741 valid rows loaded**, **91 rows rejected**, **13 files archived**, **0 failed files**.
- Reference lookup: **1,000 IMSI reference rows**, **1,004 matched valid rows**, **737 unknown IMSIs assigned `-99999`**.
- TAC/SNR verification: **0 mismatches**.
- Idempotent rerun verification: **database remained at 1,741 / 91 and the already-processed file was skipped**.
- CSV header, delimiter, encoding, null patterns, and source-length contradictions were inspected.
- Native compatibility evidence: Visual Studio 2022 17.14.40 built the project with `TargetServerVersion=SQLServer2019`; SQL Server 2019 DTExec 15.0.2000.5 executed the final package successfully after the SSIS runtime was installed side-by-side.
- Native runtime installation: SQL Server 2019 Integration Services was installed side-by-side; `MsDtsServer150` and `SSISTELEMETRY150` are running. The original `SQLEXPRESS` service and database remain available.

## Native SSIS verification

Visual Studio Community 2022 17.14.40 rebuilt the solution with SSIS Projects extension 1.4 and `TargetServerVersion=SQLServer2019`. SQL Server 2019 DTExec 15.0.2000.5 executed `ssis/VsGenerated/TelecomETL/TelecomETL/Package.dtsx` against the isolated `SSIS_Telecom_Native_Test_DB`.

The package uses a real Foreach File Enumerator over 13 isolated working copies, a dynamic Flat File Connection Manager, native Flat File Source, Row Count, Conditional Split for required-field validation, Multicast branching, native Lookup against `dbo.dim_imsi_reference`, Derived Column TAC/SNR expressions, OLE DB staging, transactional SQL finalization, and a File System Task with a per-file destination expression. The Lookup is forced to execute through a side-effect-marked input; its DTExec log records a 1,000-row reference cache for every file. SQL finalization remains responsible for authoritative transaction and rejection persistence, while the native components execute the required validation and lookup data-flow operations.

Actual clean native results:

| Metric | Result |
|---|---:|
| Flat File Source rows | 1,832 |
| Valid fact rows | 1,741 |
| Rejected rows | 91 |
| Lookup matches | 1,004 |
| Lookup misses | 737 |
| TAC/SNR mismatches | 0 |
| Audit rows/files | 13 |
| Archived files | 13 |
| DTExec exit code | 0 |

The final execution log is `runtime/dtexec-lookup14.log` (local runtime evidence, intentionally ignored from Git). It contains native Lookup cache/process events for all 13 files. The final recovery log records an archive failure with exit code 1 and all 13 files remaining in the inbox, followed by a successful retry with exit code 0.

The native rerun also returned DTExec exit code 0 and remained at 1,741 fact rows, 91 rejected rows, and 13 audit rows. An archive-failure test returned exit code 1 with all 13 files still in the inbox; after restoring the archive directory, the recovery run returned exit code 0 and produced the verified 1,741/91 counts and 13 archived files.

## Publication status

- GitHub publication was not attempted. The repository is locally prepared; public publication still requires the author’s explicit approval.
