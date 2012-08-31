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
using System.Net.Sockets;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace mybox {

  /// <summary>
  /// Command line executable for configuring the client
  /// </summary>
  public class ClientSetup {

    private ClientAccount account = null;
    private String configDir = null;

    private void gatherInput() {

      String input = null;

      Console.Write("Configuration directory ["+ configDir +"]: ");
      input = Console.ReadLine();
      if (input != String.Empty) configDir = input;

      Console.Write("Data directory ["+ account.Directory +"]: ");
      input = Console.ReadLine();
      if (input != String.Empty) account.Directory = input;

      Console.Write("Server ["+ account.ServerName +"]: ");
      input = Console.ReadLine();
      if (input != String.Empty) account.ServerName = input;

      Console.Write("Server port ["+ account.ServerPort +"]: ");
      input = Console.ReadLine();
      if (input != String.Empty)
        account.ServerPort = int.Parse(input);  //catch
      
      // attempt to connect to the server to see if it is up
      Socket socket = ClientServerConnection.ConnectSocket(account.ServerName, account.ServerPort);

      if (socket == null) {
        Console.WriteLine("Unable to contact server");
        Common.ExitError();
      }

      Console.Write("User ["+ account.User +"]: ");
      input = Console.ReadLine();
      if (input != String.Empty) account.User = input;

      Console.Write("Password [" + account.Password + "]: "); // TODO: bullet out console entry
      input = Console.ReadLine();

      if (input != String.Empty) account.Password = input;
    }

    /// <summary>
    /// This will be hooked into the event handler in the MyWorker class and will make sure
    /// that the message is logged to the GUI
    /// </summary>
    /// <param name="message"></param>
    private static void logToConsole(String message) {
      Console.WriteLine(DateTime.Now + " : " + message);
    }

    public ClientSetup() {

      // set up the defaults
      account = new ClientAccount();
      account.ServerName = "localhost";
      account.ServerPort = Common.DefaultCommunicationPort;
      account.User = "test";
      account.Password = "badpassword";
      
      configDir = ClientServerConnection.DefaultConfigDir;

      Console.WriteLine("Welcome to the Mybox client setup wizard");
    
      gatherInput();

      account.Directory = Common.EndDirWithSlash(account.Directory);
      configDir = Common.EndDirWithSlash(configDir);
      
      // data directory
      if (!Common.CreateLocalDirectory(account.Directory)) {
        Console.WriteLine("Data directory could not be created: " + account.Directory);
        Common.ExitError();
      }
    
      try {
        ClientServerConnection.WriteConfig(account, configDir);
        //Console.WriteLine("Config file written: " + configFile);
        
        ClientServerConnection clientConnection = new ClientServerConnection(configDir);
  
        clientConnection.LogHandlers.Add(new ClientServerConnection.LoggingHandlerDelegate(logToConsole));
  
        Console.WriteLine("client initialized. trying connection...");
  
        bool connected = clientConnection.StartGetAccountMode(account);
        
        if (!connected)
          throw new Exception("Account connection failed");

      }
      catch (Exception e) {
        Console.WriteLine("Error during setup: " + e.Message);
      }

    }


    static void Main(string[] args) {

      new ClientSetup();

#if DEBUG
      Console.WriteLine("Press any key to quit...");
      Console.ReadKey(true);
#endif
    }

  }
}
