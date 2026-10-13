USE [SSIS_Telecom_Native_Test_DB];
GO

CREATE OR ALTER PROCEDURE dbo.usp_finalize_native_stage
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ;WITH numbered AS
    (
        SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.source_file ORDER BY s.stage_id) AS source_row_number,
               CONVERT(char(64), HASHBYTES('SHA2_256', CONVERT(varchar(512), s.source_file)), 2) AS source_file_hash
        FROM dbo.etl_ssis_stage_raw AS s
    ), parsed AS
    (
        SELECT n.*, TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(n.ID)), '')) AS transaction_id,
               TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(n.CELL)), '')) AS cell_value,
               TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(n.LAC)), '')) AS lac_value,
               COALESCE(
                   TRY_CONVERT(datetime2, NULLIF(LTRIM(RTRIM(n.EVENT_TS)), ''), 103),
                   TRY_CONVERT(datetime2, NULLIF(LTRIM(RTRIM(n.EVENT_TS)), ''), 101),
                   TRY_CONVERT(datetime2, NULLIF(LTRIM(RTRIM(n.EVENT_TS)), ''), 120)
               ) AS event_value,
               r.subscriber_id
        FROM numbered AS n
        LEFT JOIN dbo.dim_imsi_reference AS r ON r.imsi = NULLIF(LTRIM(RTRIM(n.IMSI)), '')
    ), classified AS
    (
        SELECT p.*, NULLIF(CONCAT_WS('; ',
            CASE WHEN NULLIF(LTRIM(RTRIM(p.IMSI)), '') IS NULL THEN 'IMSI is NULL' END,
            CASE WHEN p.transaction_id IS NULL THEN 'ID is not an integer' END,
            CASE WHEN NULLIF(LTRIM(RTRIM(p.CELL)), '') IS NULL THEN 'CELL is NULL' WHEN p.cell_value IS NULL THEN 'CELL is not an integer' END,
            CASE WHEN NULLIF(LTRIM(RTRIM(p.LAC)), '') IS NULL THEN 'LAC is NULL' WHEN p.lac_value IS NULL THEN 'LAC is not an integer' END,
            CASE WHEN p.event_value IS NULL THEN CASE WHEN NULLIF(LTRIM(RTRIM(p.EVENT_TS)), '') IS NULL THEN 'EVENT_TS is NULL' ELSE 'EVENT_TS is invalid' END END
        ), '') AS rejection_reason
        FROM parsed AS p
    )
    SELECT * INTO #classified FROM classified;

    BEGIN TRANSACTION;
    DELETE f FROM dbo.fact_transaction AS f WHERE EXISTS (SELECT 1 FROM #classified AS c WHERE f.source_file_hash = c.source_file_hash);
    DELETE r FROM dbo.etl_rejected_record AS r WHERE EXISTS (SELECT 1 FROM #classified AS c WHERE r.source_file_hash = c.source_file_hash);
    DELETE a FROM dbo.etl_file_audit AS a WHERE EXISTS (SELECT 1 FROM #classified AS c WHERE a.source_file_hash = c.source_file_hash);

    INSERT dbo.fact_transaction
    (transaction_id, imsi, subscriber_id, tac, snr, imei, cell, lac, event_type, event_ts, source_file_name, source_file_hash, source_row_number)
    SELECT transaction_id, LTRIM(RTRIM(IMSI)), COALESCE(subscriber_id, -99999),
           CASE WHEN LEN(NULLIF(LTRIM(RTRIM(IMEI)), '')) >= 14 THEN LEFT(LTRIM(RTRIM(IMEI)), 8) ELSE '-99999' END,
           CASE WHEN LEN(NULLIF(LTRIM(RTRIM(IMEI)), '')) >= 14 THEN RIGHT(LTRIM(RTRIM(IMEI)), 6) ELSE '-99999' END,
           NULLIF(LTRIM(RTRIM(IMEI)), ''), cell_value, lac_value, NULLIF(LTRIM(RTRIM(EVENT_TYPE)), ''), event_value,
           source_file, source_file_hash, source_row_number
    FROM #classified WHERE rejection_reason IS NULL;

    INSERT dbo.etl_rejected_record
    (source_file_name, source_file_hash, source_row_number, transaction_id_raw, imsi_raw, imei_raw, cell_raw, lac_raw, event_type_raw, event_ts_raw, rejection_reason)
    SELECT source_file, source_file_hash, source_row_number, ID, IMSI, IMEI, CELL, LAC, EVENT_TYPE, EVENT_TS, rejection_reason
    FROM #classified WHERE rejection_reason IS NOT NULL;

    INSERT dbo.etl_file_audit
    (source_file_name, source_file_hash, status, total_rows, loaded_rows, rejected_rows, completed_utc)
    SELECT source_file, source_file_hash, 'SUCCESS', COUNT(*),
           SUM(CASE WHEN rejection_reason IS NULL THEN 1 ELSE 0 END),
           SUM(CASE WHEN rejection_reason IS NOT NULL THEN 1 ELSE 0 END), SYSUTCDATETIME()
    FROM #classified GROUP BY source_file, source_file_hash;

    COMMIT TRANSACTION;
END;
GO
