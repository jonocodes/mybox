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
using System.Linq;
using System.Text;

namespace mybox {

  /// <summary>
  /// Command line executable for configuring the server
  /// </summary>
  class ServerSetup {

    private int port = Server.Port;
    private int defaultQuota = Server.DefaultQuota;

    private String configFile = Server.DefaultConfigFile;
    private String baseDataDir = Server.DefaultBaseDataDir;
    private String accountsDbFile = Server.DefaultAccountsDbFile;

    private void gatherInput() {

      String input = null;
    
      Console.Write("Port ["+port+"]: ");
      input = Console.ReadLine();
      if (input != String.Empty) port = int.Parse(input); // TODO: catch

      Console.Write("Per-account quota in megabytes [" + defaultQuota + "]: ");
      input = Console.ReadLine();
      if (input != String.Empty)
        defaultQuota = int.Parse(input);

      Console.Write("Base data directory to create/use [" + baseDataDir + "]: ");
      input = Console.ReadLine();
      if (input != String.Empty)
        baseDataDir = input;

      baseDataDir = Common.EndDirWithSlash(baseDataDir);

      Console.Write("Accounts database file to create/use [" + accountsDbFile + "]: ");
      input = Console.ReadLine();
      if (input != String.Empty)
        accountsDbFile = input;

      Console.Write("Config file to create [" + configFile + "]: ");
      input = Console.ReadLine();
      if (input != String.Empty)
        configFile = input;

    }


    private bool saveConfig() {

      // TODO: handle existing file

      using (System.IO.StreamWriter file = new System.IO.StreamWriter(configFile, false)) {
        file.WriteLine("[settings]");
        file.WriteLine("port=" + port);
        file.WriteLine("defaultQuota=" + defaultQuota);
        file.WriteLine("baseDataDir=" + baseDataDir);
        file.WriteLine("accountsDbFile=" + accountsDbFile);
      }

      Console.WriteLine("Config file written: " + configFile);

      return true;
    }

    private ServerSetup() {

      Console.WriteLine("Welcome to the Mybox server setup wizard");
    
      // TODO: add facility to create a new database

      gatherInput();

      if (!Common.CreateLocalDirectory(baseDataDir)) {
        // TODO: make sure it has full write permissions after creation
        Console.WriteLine("Unable to setup directories.");
        Common.ExitError();
      }
    
      if (!AccountsDB.Setup(accountsDbFile)) {
        Console.WriteLine("Unable to setup database.");
        Common.ExitError();
      }

      if (!saveConfig()) {
        Console.WriteLine("Unable to save config file.");
        Common.ExitError();
      }

      AccountsDB accountsDB = new AccountsDB(accountsDbFile);
      int accounts = accountsDB.AccountsCount();
      Console.WriteLine("The database contains " + accounts + " accounts");

      Console.WriteLine("Setup finished successfully.");

      if (accounts == 0)
        Console.WriteLine("You will not be able to to use Mybox unless accounts are created. You can use ServerAdmin to manage accounts.");
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
