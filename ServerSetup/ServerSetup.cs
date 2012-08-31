﻿/**
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
using System.IO;
using System.Linq;
using System.Text;
using System.Data;
using System.Reflection;

namespace mybox {

  /// <summary>
  /// Command line executable for configuring the server
  /// </summary>
  public class ServerSetup {

    private ServerSetup () {

      Console.WriteLine ("Welcome to the Mybox server setup wizard");

      int port = Common.DefaultCommunicationPort;
//    private int defaultQuota = Server.DefaultQuota;
      Type backend = typeof(SqliteDB);

      String configDir = Server.DefaultConfigDir;
      String serverDbConnectionString;
      String baseDataDir;
    
      String input = null;
      IServerDB serverDB = null;

      do {
        Console.Write("Port ["+port+"]: ");
        input = Console.ReadLine();
        if (input != String.Empty) port = int.Parse(input);
  
        Console.WriteLine("Backend [" + backend.Name + "]: " + backend.Name);
        serverDB = (IServerDB)Activator.CreateInstance(backend /*, new Object[]{null}*/ );

        baseDataDir = serverDB.BaseDataDir;
        serverDbConnectionString = serverDB.DefaultConnectionString;
  
        Console.Write("DB connection string: [{0}]: ", serverDbConnectionString);
        input = Console.ReadLine();
        if (input != String.Empty) serverDbConnectionString = input;
  
        Console.Write("Base data directory: [{0}]: ", baseDataDir);
        input = Console.ReadLine();
        if (input != String.Empty) baseDataDir = input;

        try {
          serverDB.Connect(serverDbConnectionString, baseDataDir);
          break;
        } catch (Exception e) {
          Console.WriteLine(e.Message);
          continue;
        }
      } while (true);


      int accounts = serverDB.UsersCount();
      Console.WriteLine("  The database currently contains " + accounts + " accounts");
      
      Console.WriteLine("Rebuilding files table from filesystem");
      
      if (accounts == 0)
        Console.WriteLine("You will not be able to to use Mybox unless user are created on the server.");


      Console.Write("Configuration directory [" + configDir + "]: ");
      input = Console.ReadLine();
      if (input != String.Empty)
        configDir = input;

      // config directory
      if (!Common.CreateLocalDirectory(configDir)) {
        Console.WriteLine("Config directory could not be created: " + configDir);
        Common.ExitError();
      }

      Server.WriteConfig(configDir, port, backend, serverDbConnectionString, baseDataDir);

      Console.WriteLine("Setup finished successfully.");
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
