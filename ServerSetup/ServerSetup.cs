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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Data;

namespace mybox {

  /// <summary>
  /// Command line executable for configuring the server
  /// </summary>
  class ServerSetup {

    private int port = Server.Port;
//    private int defaultQuota = Server.DefaultQuota;

    private String configFile = Server.DefaultConfigFile;
//    private String serverDbConnectionString = Server.DefaultAccountsDbConnectionString;

    private void gatherInput() {

      String input = null;
    
      Console.Write("Port ["+port+"]: ");
      input = Console.ReadLine();
      if (input != String.Empty) port = int.Parse(input); // TODO: catch

      Console.Write("Config file to create [" + configFile + "]: ");
      input = Console.ReadLine();
      if (input != String.Empty)
        configFile = input;

    }

    private bool saveConfig () {

      string configDir = Path.GetDirectoryName(configFile);

      if (!Directory.Exists(configDir)) {
        if (!Common.CreateLocalDirectory(configDir)) {
          Console.WriteLine("Unable to create config directory " + configDir);
          Common.ExitError ();
        }
      }

      // TODO: handle existing file

      using (System.IO.StreamWriter file = new System.IO.StreamWriter(configFile, false)) {
        file.WriteLine("[settings]");
        file.WriteLine(Server.CONFIG_PORT + "=" + port);
//        file.WriteLine(Server.CONFIG_DBSTRING + "=" + serverDbConnectionString);
      }

      Console.WriteLine ("Config file written: " + configFile);

      return true;
    }

    private ServerSetup () {

      Console.WriteLine ("Welcome to the Mybox server setup wizard");

      gatherInput();

      if (!saveConfig()) {
        Console.WriteLine("Unable to save config file.");
        Common.ExitError();
      }

      // TODO: elegently notify the user if the owncloud db is unreachable

      ServerDB serverDB = new OwnCloudDB(null);
      int accounts = serverDB.AccountsCount();
      Console.WriteLine("The database contains " + accounts + " accounts");

      Console.WriteLine("Setup finished successfully.");

      if (accounts == 0)
        Console.WriteLine("You will not be able to to use Mybox unless accounts are created. Do this in Owncloud.");
    }

    /// <summary>
    /// Start the command line executable
    /// </summary>
    /// <param name="args"></param>
    static void Main(string[] args) {

      new ServerSetup();

#if DEBUG
      Console.WriteLine("Press any key to quit...");
      Console.ReadKey(true);
#endif
    }

  }
}
