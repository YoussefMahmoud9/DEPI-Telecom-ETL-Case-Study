USE [SSIS_Telecom_DB];
GO

IF OBJECT_ID(N'dbo.etl_ssis_stage_raw', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.etl_ssis_stage_raw
    (
        stage_id       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_etl_ssis_stage_raw PRIMARY KEY,
        source_file    nvarchar(512) NOT NULL,
        ID             varchar(100) NULL,
        IMSI           varchar(100) NULL,
        IMEI           varchar(100) NULL,
        CELL           varchar(100) NULL,
        LAC            varchar(100) NULL,
        EVENT_TYPE     varchar(100) NULL,
        EVENT_TS       varchar(100) NULL,
        TAC            nvarchar(100) NULL,
        SNR            nvarchar(100) NULL,
        load_ts        datetime2(3) NOT NULL CONSTRAINT DF_etl_ssis_stage_raw_load_ts DEFAULT SYSUTCDATETIME()
    );
END;
GO
