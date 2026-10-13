using System;
using Microsoft.SqlServer.Dts.Runtime;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime.Wrapper;
using Microsoft.SqlServer.Dts.Runtime.Enumerators.File;
using Microsoft.SqlServer.Dts.Tasks.FileSystemTask;

class BuildNativePackage
{
    static CManagedComponentWrapper Design(IDTSComponentMetaData100 m) =>
        (CManagedComponentWrapper)m.Instantiate();

    static void UseAll(CManagedComponentWrapper d, IDTSInput100 input)
    {
        var v = input.GetVirtualInput();
        foreach (IDTSVirtualInputColumn100 c in v.VirtualInputColumnCollection)
        {
            try { d.SetUsageType(input.ID, v, c.LineageID, DTSUsageType.UT_READONLY); }
            catch (System.Runtime.InteropServices.COMException) { }
        }
    }

    static Microsoft.SqlServer.Dts.Runtime.TaskHost SqlTask(Executables executables, string name, string sql)
    {
        var task = (Microsoft.SqlServer.Dts.Runtime.TaskHost)executables.Add("STOCK:SQLTask");
        task.Name = name;
        task.Properties["Connection"].SetValue(task, "CM_SQLServer");
        task.Properties["SqlStatementSource"].SetValue(task, sql);
        task.Properties["ResultSetType"].SetValue(task, 0);
        return task;
    }

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var simple = new System.Reflection.AssemblyName(args.Name).Name + ".dll";
            var dirs = new[]
            {
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\SSIS\150\Binn",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\PublicAssemblies\SSIS\150"
            };
            foreach (var dir in dirs)
            {
                var path = System.IO.Path.Combine(dir, simple);
                if (System.IO.File.Exists(path)) return System.Reflection.Assembly.LoadFrom(path);
            }
            return null;
        };
        var app = new Microsoft.SqlServer.Dts.Runtime.Application();
        var pkg = new Microsoft.SqlServer.Dts.Runtime.Package
        {
            Name = "TelecomNativeETL",
            Description = "Native SSIS telecom ETL: Foreach file, Flat File Source, validation staging, SQL finalization and archive."
        };

        pkg.Variables.Add("CurrentFilePath", false, "User", "");
        pkg.Variables.Add("CurrentFileName", false, "User", "");
        pkg.Variables.Add("InboxDirectory", false, "User", @"E:\Depi\Techincal\Case Study Telecom ETL\runtime\native-inbox");
        pkg.Variables.Add("ArchiveDirectory", false, "User", @"E:\Depi\Techincal\Case Study Telecom ETL\runtime\native-archive");
        pkg.Variables.Add("ArchiveTargetPath", false, "User", "");
        pkg.Variables.Add("SourceRowCount", false, "User", 0);
        pkg.Variables["User::CurrentFilePath"].EvaluateAsExpression = true;
        pkg.Variables["User::CurrentFilePath"].Expression = "@[User::InboxDirectory] + \"\\\\\" + @[User::CurrentFileName]";
        pkg.Variables["User::ArchiveTargetPath"].EvaluateAsExpression = true;
        pkg.Variables["User::ArchiveTargetPath"].Expression = "@[User::ArchiveDirectory] + \"\\\\\" + @[User::CurrentFileName]";

        var file = pkg.Connections.Add("FLATFILE");
        file.Name = "CM_SourceFile";
        file.Properties["Format"].SetValue(file, "Delimited");
        file.Properties["ColumnNamesInFirstDataRow"].SetValue(file, false);
        file.Properties["HeaderRowsToSkip"].SetValue(file, 1);
        file.Properties["HeaderRowDelimiter"].SetValue(file, Environment.NewLine);
        file.Properties["AlwaysCheckForRowDelimiters"].SetValue(file, false);
        file.Properties["ConnectionString"].SetValue(file, @"E:\Depi\Techincal\Case Study Telecom ETL\runtime\native-inbox\01_clean_data.csv");
        var flat = (IDTSConnectionManagerFlatFile100)file.InnerObject;
        flat.RowDelimiter = Environment.NewLine;
        flat.ColumnNamesInFirstDataRow = false;
        var cols = (IDTSConnectionManagerFlatFileColumns100)file.Properties["Columns"].GetValue(file);
        string[] names = { "ID", "IMSI", "IMEI", "CELL", "LAC", "EVENT_TYPE", "EVENT_TS", "SOURCE_FILE" };
        foreach (var name in names)
        {
            var c = cols.Add();
            ((IDTSName100)c).Name = name;
            c.DataType = name == "SOURCE_FILE" ? DataType.DT_WSTR : DataType.DT_STR;
            c.ColumnWidth = 100;
            c.TextQualified = false;
            c.ColumnType = "Delimited";
            c.ColumnDelimiter = name == "SOURCE_FILE" ? Environment.NewLine : "|";
        }
        file.SetExpression("ConnectionString", "@[User::CurrentFilePath]");

        var db = pkg.Connections.Add("OLEDB");
        db.Name = "CM_SQLServer";
        db.ConnectionString = "Provider=MSOLEDBSQL;Data Source=.\\SQLEXPRESS;Initial Catalog=SSIS_Telecom_Native_Test_DB;Integrated Security=SSPI;Trust Server Certificate=True;";

        var archiveConn = pkg.Connections.Add("FILE");
        archiveConn.Name = "CM_ArchiveFolder";
        archiveConn.Properties["ConnectionString"].SetValue(archiveConn, @"E:\Depi\Techincal\Case Study Telecom ETL\runtime\native-archive");
        archiveConn.Properties["FileUsageType"].SetValue(archiveConn, 2);
        var archiveTargetConn = pkg.Connections.Add("FILE");
        archiveTargetConn.Name = "CM_ArchiveTarget";
        archiveTargetConn.Properties["ConnectionString"].SetValue(archiveTargetConn, @"E:\Depi\Techincal\Case Study Telecom ETL\runtime\native-archive\01_clean_data.csv");
        archiveTargetConn.SetExpression("ConnectionString", "@[User::ArchiveTargetPath]");
        var currentFileConn = pkg.Connections.Add("FILE");
        currentFileConn.Name = "CM_CurrentFile";
        currentFileConn.Properties["ConnectionString"].SetValue(currentFileConn, @"E:\Depi\Techincal\Case Study Telecom ETL\runtime\native-inbox\01_clean_data.csv");
        currentFileConn.SetExpression("ConnectionString", "@[User::CurrentFilePath]");

        var loop = (Microsoft.SqlServer.Dts.Runtime.ForEachLoop)pkg.Executables.Add("STOCK:FOREACHLOOP");
        loop.Name = "FEL_ProcessCSVFiles";
        var mapName = loop.VariableMappings.Add();
        mapName.VariableName = "User::CurrentFileName";
        mapName.ValueIndex = 0;

        var enumInfo = app.ForEachEnumeratorInfos["Foreach File Enumerator"];
        var enumHost = enumInfo.CreateNew();
        loop.ForEachEnumerator = enumHost;
        var fileEnum = (ForEachFileEnumerator)enumHost.InnerObject;
        fileEnum.Directory = @"E:\Depi\Techincal\Case Study Telecom ETL\runtime\native-inbox";
        fileEnum.FileSpec = "*.csv";
        fileEnum.Recurse = false;
        fileEnum.FileNameRetrieval = FileNameRetrievalType.FNRT_NameDotExtension;

        var clearStage = SqlTask(loop.Executables, "SQL_ClearNativeStage", "TRUNCATE TABLE dbo.etl_ssis_stage_raw;");
        var dataTask = (Microsoft.SqlServer.Dts.Runtime.TaskHost)loop.Executables.Add("STOCK:PipelineTask");
        dataTask.Name = "DFT_ProcessCurrentCSV";
        var pipePtr = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(dataTask.InnerObject);
        var pipe = (MainPipe)System.Runtime.InteropServices.Marshal.GetTypedObjectForIUnknown(pipePtr, typeof(MainPipe));
        System.Runtime.InteropServices.Marshal.Release(pipePtr);

        var src = pipe.ComponentMetaDataCollection.New();
        src.Name = "SRC_TelecomPipeFile";
        src.ComponentClassID = app.PipelineComponentInfos["Flat File Source"].CreationName;
        var srcD = Design(src);
        srcD.ProvideComponentProperties();
        src.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(file);
        src.RuntimeConnectionCollection[0].ConnectionManagerID = file.ID;
        srcD.AcquireConnections(null);
        srcD.ReinitializeMetaData();
        srcD.ReleaseConnections();

        var rowCount = pipe.ComponentMetaDataCollection.New();
        rowCount.Name = "ROWCOUNT_SourceRows";
        rowCount.ComponentClassID = app.PipelineComponentInfos["Row Count"].CreationName;
        var rowCountD = Design(rowCount);
        rowCountD.ProvideComponentProperties();
        rowCountD.SetComponentProperty("VariableName", "User::SourceRowCount");
        var p1 = pipe.PathCollection.New();
        p1.AttachPathAndPropagateNotifications(src.OutputCollection[0], rowCount.InputCollection[0]);
        UseAll(rowCountD, rowCount.InputCollection[0]);

        var split = pipe.ComponentMetaDataCollection.New();
        split.Name = "SPLIT_RequiredFieldValidation";
        split.ComponentClassID = app.PipelineComponentInfos["Conditional Split"].CreationName;
        var splitD = Design(split);
        splitD.ProvideComponentProperties();
        var rejectedOutput = split.OutputCollection.New();
        rejectedOutput.Name = "RejectedRequiredFields";
        rejectedOutput.ExclusionGroup = 1;
        rejectedOutput.SynchronousInputID = split.InputCollection[0].ID;
        rejectedOutput.ErrorRowDisposition = DTSRowDisposition.RD_IgnoreFailure;
        rejectedOutput.TruncationRowDisposition = DTSRowDisposition.RD_IgnoreFailure;
        var rejectionEvaluationOrder = rejectedOutput.CustomPropertyCollection.New();
        rejectionEvaluationOrder.Name = "EvaluationOrder";
        rejectionEvaluationOrder.Value = 0;
        var rejectionExpression = rejectedOutput.CustomPropertyCollection.New();
        rejectionExpression.Name = "Expression";
        rejectionExpression.Value = "ISNULL([IMSI]) || ISNULL([CELL]) || ISNULL([LAC])";
        var rejectionFriendlyExpression = rejectedOutput.CustomPropertyCollection.New();
        rejectionFriendlyExpression.Name = "FriendlyExpression";
        rejectionFriendlyExpression.Value = "Missing required field (IMSI, CELL, or LAC)";
        var splitPath = pipe.PathCollection.New();
        splitPath.AttachPathAndPropagateNotifications(rowCount.OutputCollection[0], split.InputCollection[0]);
        UseAll(splitD, split.InputCollection[0]);

        var union = pipe.ComponentMetaDataCollection.New();
        union.Name = "UNION_AllValidatedAndRejected";
        union.ComponentClassID = app.PipelineComponentInfos["Union All"].CreationName;
        var unionD = Design(union);
        unionD.ProvideComponentProperties();
        var unionPathDefault = pipe.PathCollection.New();
        unionPathDefault.AttachPathAndPropagateNotifications(split.OutputCollection[0], union.InputCollection[0]);
        UseAll(unionD, union.InputCollection[0]);
        var unionInput2 = union.InputCollection[1];
        var unionPathRejected = pipe.PathCollection.New();
        unionPathRejected.AttachPathAndPropagateNotifications(rejectedOutput, unionInput2);
        UseAll(unionD, unionInput2);
        while (union.InputCollection.Count > 2)
            union.InputCollection.RemoveObjectByIndex(union.InputCollection.Count - 1);

        var multicast = pipe.ComponentMetaDataCollection.New();
        multicast.Name = "MCAST_LookupAndPersistence";
        multicast.ComponentClassID = app.PipelineComponentInfos["Multicast"].CreationName;
        var multicastD = Design(multicast);
        multicastD.ProvideComponentProperties();
        multicast.Name = "MCAST_LookupAndPersistence";
        var multicastPath = pipe.PathCollection.New();
        multicastPath.AttachPathAndPropagateNotifications(union.OutputCollection[0], multicast.InputCollection[0]);
        UseAll(multicastD, multicast.InputCollection[0]);

        var lookup = pipe.ComponentMetaDataCollection.New();
        lookup.Name = "LKP_IMSIReference";
        lookup.ComponentClassID = app.PipelineComponentInfos["Lookup"].CreationName;
        var lookupD = Design(lookup);
        lookupD.ProvideComponentProperties();
        lookup.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(db);
        lookup.RuntimeConnectionCollection[0].ConnectionManagerID = db.ID;
        lookupD.SetComponentProperty("SqlCommand", "SELECT imsi, subscriber_id FROM dbo.dim_imsi_reference");
        lookupD.SetComponentProperty("NoMatchBehavior", 1);
        var lookupPath = pipe.PathCollection.New();
        lookupPath.AttachPathAndPropagateNotifications(multicast.OutputCollection[0], lookup.InputCollection[0]);
        UseAll(lookupD, lookup.InputCollection[0]);
        lookupD.AcquireConnections(null);
        lookupD.ReinitializeMetaData();
        lookupD.ReleaseConnections();
        var lookupVirtual = lookup.InputCollection[0].GetVirtualInput();
        foreach (IDTSVirtualInputColumn100 c in lookupVirtual.VirtualInputColumnCollection)
        {
            try
            {
                lookupD.SetUsageType(lookup.InputCollection[0].ID, lookupVirtual, c.LineageID,
                    c.Name == "IMSI" ? DTSUsageType.UT_READONLY : DTSUsageType.UT_IGNORED);
            }
            catch (System.Runtime.InteropServices.COMException) { }
        }
        var lookupInput = lookup.InputCollection[0];
        var refImsi = lookupInput.ExternalMetadataColumnCollection.New();
        refImsi.Name = "imsi";
        refImsi.DataType = DataType.DT_STR;
        refImsi.Length = 9;
        refImsi.CodePage = 1252;
        var inputImsi = lookupInput.InputColumnCollection["IMSI"];
        lookupD.MapInputColumn(lookupInput.ID, inputImsi.ID, refImsi.ID);
        lookupD.SetInputColumnProperty(lookupInput.ID, inputImsi.ID, "JoinToReferenceColumn", "imsi");
        lookupInput.HasSideEffects = true;

        var derived = pipe.ComponentMetaDataCollection.New();
        derived.Name = "DER_ValidateAndTransformText";
        derived.ComponentClassID = app.PipelineComponentInfos["Derived Column"].CreationName;
        var derivedD = Design(derived);
        derivedD.ProvideComponentProperties();
        var p2 = pipe.PathCollection.New();
        p2.AttachPathAndPropagateNotifications(multicast.OutputCollection[1], derived.InputCollection[0]);
        UseAll(derivedD, derived.InputCollection[0]);
        var dOut = derived.OutputCollection[0];
        int pos = dOut.OutputColumnCollection.Count;
        var tac = derivedD.InsertOutputColumnAt(dOut.ID, pos++, "TAC", "");
        derivedD.SetOutputColumnProperty(dOut.ID, tac.ID, "Expression", "LEN([IMEI]) >= 14 ? LEFT([IMEI], 8) : \"-99999\"");
        var snr = derivedD.InsertOutputColumnAt(dOut.ID, pos, "SNR", "");
        derivedD.SetOutputColumnProperty(dOut.ID, snr.ID, "Expression", "LEN([IMEI]) >= 14 ? RIGHT([IMEI], 6) : \"-99999\"");

        var dest = pipe.ComponentMetaDataCollection.New();
        dest.Name = "DST_SSISStageRaw";
        dest.ComponentClassID = app.PipelineComponentInfos["OLE DB Destination"].CreationName;
        var destD = Design(dest);
        destD.ProvideComponentProperties();
        dest.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(db);
        dest.RuntimeConnectionCollection[0].ConnectionManagerID = db.ID;
        destD.SetComponentProperty("AccessMode", 0);
        destD.SetComponentProperty("OpenRowset", "dbo.etl_ssis_stage_raw");
        var p3 = pipe.PathCollection.New();
        p3.AttachPathAndPropagateNotifications(dOut, dest.InputCollection[0]);
        destD.AcquireConnections(null);
        destD.ReinitializeMetaData();
        var vi = dest.InputCollection[0].GetVirtualInput();
        foreach (IDTSVirtualInputColumn100 c in vi.VirtualInputColumnCollection)
        {
            var ic = destD.SetUsageType(dest.InputCollection[0].ID, vi, c.LineageID, DTSUsageType.UT_READONLY);
            try
            {
                string external = c.Name == "SOURCE_FILE" ? "source_file" : c.Name;
                var ext = dest.InputCollection[0].ExternalMetadataColumnCollection[external];
                destD.MapInputColumn(dest.InputCollection[0].ID, ic.ID, ext.ID);
            }
            catch { }
        }
        destD.ReleaseConnections();

        var finalizeCurrent = SqlTask(loop.Executables, "SQL_FinalizeCurrentFile", "EXEC dbo.usp_finalize_native_stage;");
        var archive = (Microsoft.SqlServer.Dts.Runtime.TaskHost)loop.Executables.Add("STOCK:FileSystemTask");
        archive.Name = "FST_ArchiveCurrentFile";
        var fs = (FileSystemTask)archive.InnerObject;
        fs.Operation = DTSFileSystemOperation.MoveFile;
        fs.Source = "CM_CurrentFile";
        fs.Destination = "CM_ArchiveTarget";
        fs.OverwriteDestinationFile = true;

        loop.PrecedenceConstraints.Add(clearStage, dataTask);
        loop.PrecedenceConstraints.Add(dataTask, finalizeCurrent);
        loop.PrecedenceConstraints.Add(finalizeCurrent, archive);

        app.SaveToXml(@"ssis\VsGenerated\TelecomETL\TelecomETL\Package.dtsx", pkg, null);
        Console.WriteLine("NATIVE_PACKAGE_CREATED_FOREACH");
    }
}
