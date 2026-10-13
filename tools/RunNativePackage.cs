using System;
using Microsoft.SqlServer.Dts.Runtime;

class RunNativePackage
{
    static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Package path is required.");
        var app = new Application();
        var pkg = app.LoadPackage(args[0], null);
        var result = pkg.Execute();
        Console.WriteLine("RESULT=" + result);
        try { Console.WriteLine("SOURCE_ROW_COUNT=" + pkg.Variables["SourceRowCount"].Value); }
        catch (Exception ex) { Console.WriteLine("SOURCE_ROW_COUNT_ERROR=" + ex.Message); }
    }

    sealed class PackageEvents : DefaultEvents
    {
        readonly Package package;
        public PackageEvents(Package package) { this.package = package; }
        public override bool OnError(DtsObject source, int errorCode, string subComponent,
            string description, string helpFile, int helpContext, string idOfInterfaceWithError)
        {
            Console.WriteLine("EVENT_ERROR=" + description);
            return false;
        }
        public override void OnWarning(DtsObject source, int warningCode, string subComponent,
            string description, string helpFile, int helpContext, string idOfInterfaceWithError)
        {
            Console.WriteLine("EVENT_WARNING=" + description);
        }
    }
}
