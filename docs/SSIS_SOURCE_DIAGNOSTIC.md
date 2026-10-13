# Native SSIS source-boundary diagnostic

Date: 2026-10-13

The initial native package returned DTExec success but loaded zero rows. Verbose pipeline logging showed:

- Flat File Source: `total number of data rows processed ... 0`
- OLE DB Destination: `wrote 0 rows`
- Warning: `There is a partial row at the end of the file`

The input working copy was independently inspected as a pipe-delimited file with a header and CRLF records. The generated flat-file connection manager contained the delimiter strings, but each `IDTSConnectionManagerFlatFileColumn100` was missing `ColumnType = "Delimited"`. The runtime therefore did not treat the configured column delimiters as usable delimited columns.

The generator was corrected to set `ColumnType` explicitly for every flat-file column. A single-file rebuild and execution then produced:

- Flat File Source rows: 5
- OLE DB Destination rows: 5
- SQL staging rows after one clean DTExec run: 5
- DTExec exit code: 0

The staging table was cleared before the clean run. The original assignment files were not modified.

The package now uses a real Foreach File Enumerator over isolated working copies. Each working copy is normalized to CRLF for the SSIS Flat File Source and includes an additional `source_file` field so the original filename travels through the native data flow. The data flow includes a native Conditional Split for required-field validation, a Multicast branch, and a native IMSI Lookup configured against `dbo.dim_imsi_reference`; the Lookup input is marked as having side effects so DTExec does not optimize it away when its result is not persisted as a separate stage column. Native TAC/SNR Derived Columns remain on the persistence branch. SQL finalization and a per-file File System Task execute only after the data flow succeeds. A per-file destination connection fixed an archival issue where a folder was incorrectly supplied as a single-file destination.

The final native run read all 1,832 rows, finalized 1,741 valid and 91 rejected records, matched 1,004 IMSIs, assigned `-99999` to 737 misses, produced zero TAC/SNR mismatches, and archived 13 distinct files. A rerun remained idempotent. An induced archive failure returned DTExec 1 while all 13 files remained in the inbox; restoring the archive location and rerunning returned DTExec 0. The isolated test database was used so the verified Python baseline in `SSIS_Telecom_DB` remained intact.
