/**
    Mybox version 0.3.0
    https://github.com/jonocodes/mybox
 
    Copyright (C) 2012  Jono Finger (jono@foodnotblogs.com)

    This program is free software; you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation; either version 2 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along
    with this program; if not it can be found here:
    http://www.gnu.org/licenses/gpl-2.0.html
 */

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;

namespace mybox {
  
  /// <summary>
  /// The command line interface to the client connection
  /// </summary>
  class Client {

    //private delegate void setStatusHandler(ClientStatus status);

    private void setStatus(ClientStatus status) {
      Console.WriteLine ("Status: " + status.ToString ());
    }

    /// <summary>
    /// this delegate is used for the BeginInvoke to allow for thread safe updating of the GUI
    /// </summary>
    /// <param name="message"></param>
    //private delegate void writeMessageHandler(String message);

    /// <summary>
    /// this will handle logging the message to a file
    /// </summary>
    /// <param name="message"></param>
    private static void logToFile(String message) {
      File.AppendAllText(ClientServerConnection.LogFile, DateTime.Now + " : " + message + Environment.NewLine);
    }

    /// <summary>
    /// This will be hooked into the event handler in the MyWorker class and will make sure
    /// that the message is logged to the GUI
    /// </summary>
    /// <param name="message"></param>
    private static void logToConsole(String message) {
      Console.WriteLine(DateTime.Now + " : " + message);
    }

    public Client(String configDir) {

      ClientServerConnection.LogHandlers.Add(new ClientServerConnection.LoggingHandlerDelegate(logToConsole));
      ClientServerConnection.LogHandlers.Add(new ClientServerConnection.LoggingHandlerDelegate(logToFile));
      ClientServerConnection.StatusHandler = setStatus;

      try {
        ClientServerConnection.SetConfigDir(configDir);
        ClientServerConnection client = new ClientServerConnection();
        client.LoadConfig(ClientServerConnection.ConfigFile);
        client.Start();
      }
      catch (Exception e) {
        logToConsole("Error: " + e.Message);
        logToFile("Error: " + e.Message);
#if DEBUG
        Console.WriteLine("Press any key to quit...");
        Console.ReadKey(true);
#endif
      }
    }
  
    /// <summary>
    /// Handle command line args
    /// </summary>
    /// <param name="args"></param>
    public static void Main(string[] args) {

      OptionSet options = new OptionSet();

      String configDir = ClientServerConnection.DefaultConfigDir;

      options.Add("c|configdir=", "configuration directory (default=" + configDir + ")", delegate(string v) {
        configDir = Common.EndDirWithSlash(v);
      });

      options.Add("h|help", "show help screen", delegate(string v) {
        Common.ShowCliHelp(options, System.Reflection.Assembly.GetExecutingAssembly());
      });

      options.Add("v|version", "print the Mybox version", delegate(string v) {
        Console.WriteLine(Common.AppVersion);
        System.Diagnostics.Process.GetCurrentProcess().Kill();
      });


      // Note: all additional arguments are invalid since it does not take non-options

      List<string> extra = new List<string>();

      try {
        extra = options.Parse(args);
      }
      catch (OptionException) {
        Console.WriteLine("Invalid argument entered");

        // print the help screen
        Common.ShowCliHelp(options, System.Reflection.Assembly.GetExecutingAssembly());
      }

      if (extra.Count > 0) {
        Console.WriteLine("Invalid argument entered");

        // print the help screen
        Common.ShowCliHelp(options, System.Reflection.Assembly.GetExecutingAssembly());
      }


      new Client(configDir);

    }

  }

}
