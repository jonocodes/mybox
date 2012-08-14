/**
    Mybox
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace mybox {

  /// <summary>
  /// Command line executable for administering accounts on the server
  /// </summary>
  class ServerAdmin {

    private IServerDB serverDb = null;

    public ServerAdmin(String configFile) {

      serverDb = Server.LoadConfig(configFile);

      Console.WriteLine("Starting ServerAdmin command line utility...");

      char choice = ' ';
    
      // menu
      while (choice != 'q') {
        Console.WriteLine("  l) List accounts");
        Console.WriteLine("  q) Quit");
        Console.Write("  > ");
        
        ConsoleKeyInfo cki = Console.ReadKey(false);

        choice = cki.KeyChar;

        Console.WriteLine();

        switch (choice) {
          case 'l':
            serverDb.ShowUsers();
            break;
        }
      }
    }

    /// <summary>
    /// Start the command line executable and handle command line arguments
    /// </summary>
    /// <param name="args"></param>
    static void Main(string[] args) {

      OptionSet options = new OptionSet();

      String configFile = Server.DefaultConfigFile;

      options.Add("c|configfile=", "configuration file (default=" + configFile + ")", delegate(string v) {
        configFile = v;
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
      } catch (OptionException) {
        Console.WriteLine("Invalid argument entered");

        // print the help screen
        Common.ShowCliHelp(options, System.Reflection.Assembly.GetExecutingAssembly());
      }

      if (extra.Count > 0) {
        Console.WriteLine("Invalid argument entered");

        // print the help screen
        Common.ShowCliHelp(options, System.Reflection.Assembly.GetExecutingAssembly());
      }

      new ServerAdmin(configFile);
    }
  }
}
