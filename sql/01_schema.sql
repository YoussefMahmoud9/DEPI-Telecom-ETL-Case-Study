/* DEPI R5 Telecom ETL - idempotent SQL Server schema */
IF DB_ID(N'SSIS_Telecom_DB') IS NULL
BEGIN
    CREATE DATABASE SSIS_Telecom_DB;
END;
GO
USE SSIS_Telecom_DB;
GO

IF OBJECT_ID(N'dbo.dim_imsi_reference', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.dim_imsi_reference
    (
        id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_dim_imsi_reference PRIMARY KEY,
        imsi varchar(9) NOT NULL,
        subscriber_id int NOT NULL,
        CONSTRAINT UQ_dim_imsi_reference_imsi UNIQUE (imsi)
    );
END;
GO

IF OBJECT_ID(N'dbo.etl_file_audit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.etl_file_audit
    (
        audit_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_etl_file_audit PRIMARY KEY,
        source_file_name nvarchar(260) NOT NULL,
        source_file_hash char(64) NOT NULL,
        source_file_size bigint NULL,
        started_utc datetime2(3) NOT NULL CONSTRAINT DF_etl_file_audit_started DEFAULT SYSUTCDATETIME(),
        completed_utc datetime2(3) NULL,
        status varchar(20) NOT NULL,
        total_rows int NOT NULL CONSTRAINT DF_etl_file_audit_total DEFAULT 0,
        loaded_rows int NOT NULL CONSTRAINT DF_etl_file_audit_loaded DEFAULT 0,
        rejected_rows int NOT NULL CONSTRAINT DF_etl_file_audit_rejected DEFAULT 0,
        error_message nvarchar(4000) NULL,
        CONSTRAINT UQ_etl_file_audit_hash UNIQUE (source_file_hash)
    );
END;
GO

IF OBJECT_ID(N'dbo.fact_transaction', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fact_transaction
    (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_fact_transaction PRIMARY KEY,
        transaction_id int NOT NULL,
        imsi varchar(4000) NOT NULL,
        subscriber_id int NOT NULL,
        tac varchar(14) NOT NULL,
        snr varchar(14) NOT NULL,
        imei varchar(4000) NULL,
        cell int NOT NULL,
        lac int NOT NULL,
        event_type varchar(4000) NULL,
        event_ts datetime2(0) NOT NULL,
        source_file_name nvarchar(260) NOT NULL,
        source_file_hash char(64) NOT NULL,
        source_row_number int NOT NULL,
        loaded_utc datetime2(3) NOT NULL CONSTRAINT DF_fact_transaction_loaded DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_fact_transaction_source_row UNIQUE (source_file_hash, source_row_number),
        CONSTRAINT UQ_fact_transaction_file_transaction UNIQUE (source_file_hash, transaction_id)
    );
END;
GO

IF OBJECT_ID(N'dbo.etl_rejected_record', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.etl_rejected_record
    (
        reject_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_etl_rejected_record PRIMARY KEY,
        source_file_name nvarchar(260) NOT NULL,
        source_file_hash char(64) NOT NULL,
        source_row_number int NOT NULL,
        transaction_id_raw nvarchar(4000) NULL,
        imsi_raw nvarchar(4000) NULL,
        imei_raw nvarchar(4000) NULL,
        cell_raw nvarchar(4000) NULL,
        lac_raw nvarchar(4000) NULL,
        event_type_raw nvarchar(4000) NULL,
        event_ts_raw nvarchar(4000) NULL,
        rejection_reason nvarchar(1000) NOT NULL,
        rejected_utc datetime2(3) NOT NULL CONSTRAINT DF_etl_rejected_utc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_etl_rejected_source_row UNIQUE (source_file_hash, source_row_number)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fact_transaction_event_ts' AND object_id = OBJECT_ID(N'dbo.fact_transaction'))
    CREATE INDEX IX_fact_transaction_event_ts ON dbo.fact_transaction(event_ts);
GO
