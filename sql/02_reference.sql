/* Synthetic reference data for the public smoke-test repository. */
USE [SSIS_Telecom_DB];
GO
IF NOT EXISTS (SELECT 1 FROM dbo.dim_imsi_reference WHERE imsi = '001010001')
    INSERT INTO dim_imsi_reference(imsi, subscriber_id) VALUES ('001010001', 1001);
IF NOT EXISTS (SELECT 1 FROM dbo.dim_imsi_reference WHERE imsi = '001010002')
    INSERT INTO dim_imsi_reference(imsi, subscriber_id) VALUES ('001010002', 1002);
GO
