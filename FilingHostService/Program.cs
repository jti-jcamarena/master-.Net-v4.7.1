using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using Serilog;
using Serilog.Events;

namespace FilingHostService
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {
            // Create logger interface for service host (Add Serilog ref to other classes to use static Log attribute)
            if (!String.IsNullOrEmpty(@ConfigurationManager.AppSettings["ofsLogFile"]))  // logger enabled?
            {
                if (Environment.UserInteractive) // non deployed mode?
                {
                    Log.Logger = new LoggerConfiguration()
                        .WriteTo.Console()
                        .WriteTo.RollingFile(@ConfigurationManager.AppSettings["ofsLogFile"])
                        .CreateLogger();
                }
                else  // deployed, no console, just file
                {
                    Log.Logger = new LoggerConfiguration()
                        .WriteTo.RollingFile(@ConfigurationManager.AppSettings["ofsLogFile"])
                        .CreateLogger();
                }
            } else { // default to disabled logger, nothing will be logged
                Log.Logger = new LoggerConfiguration().CreateLogger();
            }

            // Start ESL service
            try
            {
                if (Environment.UserInteractive)    // undeployed mode
                {
                    //Log.Logger.Information("undeployed mode");
                    var service = new FilingWindowsService();
                    service.TestStartupAndStop(args);
                }
                else // deployed mode 
                {
                    //Log.Logger.Information("deployed mode");
                    ServiceBase[] ServicesToRun;
                    ServicesToRun = new ServiceBase[]
                    {
                    new FilingWindowsService()
                        {
                            ServiceName = "ESL_OdysseyRfService"
                        }
                    };
                    ServiceBase.Run(ServicesToRun);
                }
            } catch ( Exception ex ){
                Log.Fatal(ex, "Exception::Main - Unhandled Host exceptions");
            } finally { // called regardless!
                Log.CloseAndFlush();
            }
        }
    }
}
