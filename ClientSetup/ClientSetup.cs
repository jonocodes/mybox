/**
    Mybox version 0.3.0
    https://github.com/mybox/myboxSharp
 
    Copyright (C) 2011  Jono Finger (jono@foodnotblogs.com)

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
using System.Linq;
using System.Text;

namespace mybox {

  /// <summary>
  /// Command line executable for configuring the client
  /// </summary>
  class ClientSetup {

    private ClientAccount account = null;
    private String password = null;
    private String configDir = null;

    //private String configFile = Client.configFile;//TODO: make this setable during gatherInput


    private void gatherInput() {

      String input = null;

      Console.Write("Configuration directory ["+ configDir +"]: ");
      input = Console.ReadLine();
      if (input != String.Empty) configDir = input;

      Console.Write("Data directory ["+ account.Directory +"]: ");
      input = Console.ReadLine();
      if (input != String.Empty) account.Directory = input;

      Console.Write("Server name ["+ account.ServerName +"]: ");
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

      Console.Write("Email ["+ account.Email +"]: ");
      input = Console.ReadLine();
      if (input != String.Empty) account.Email = input;

      Console.Write("Password [" + password + "]: "); // TODO: bullet out console entry
      input = Console.ReadLine();
      if (input != String.Empty) password = input;

    }

    private bool saveConfig() {
    
      // TODO: handle existing file

      using (System.IO.StreamWriter file = new System.IO.StreamWriter(ClientServerConnection.ConfigFile, false)) {
        file.WriteLine("[settings]");
        file.WriteLine("serverName=" + account.ServerName);
        file.WriteLine("serverPort=" + account.ServerPort.ToString());
        file.WriteLine("email=" + account.Email);
        file.WriteLine("salt=" + account.Salt);
        file.WriteLine("directory=" + account.Directory);
      }

      Console.WriteLine("Config file written: " + ClientServerConnection.ConfigFile);

      return true;
    }


    public ClientSetup() {

      // set up the defaults
      account = new ClientAccount();
      account.ServerName = "localhost";
      account.ServerPort = Common.DefaultCommunicationPort;
      account.Email = "bill@gates.com";
      password = "bill";

      configDir = ClientServerConnection.DefaultConfigDir;

      Console.WriteLine("Welcome to the Mybox client setup wizard");
    
      gatherInput();

      account.Directory = Common.EndDirWithSlash(account.Directory);
      configDir = Common.EndDirWithSlash(configDir);
    
      // attach the account to the server to get the user
      // TODO: clean up this function and its arguments
      ClientServerConnection client = new ClientServerConnection();
      account = client.StartGetAccountMode(account.ServerName, account.ServerPort, account.Email, account.Directory);
      //client.close();

    
      // data directory
      if (!Common.CreateLocalDirectory(account.Directory)) {
        Console.WriteLine("Data directory could not be created: " + account.Directory);
        Common.ExitError();
      }

      // config directory
      if (!Common.CreateLocalDirectory(configDir)) {
        Console.WriteLine("Config directory could not be created: " + configDir);
        Common.ExitError();
      }

      try {
        ClientServerConnection.SetConfigDir(configDir);
      } catch (Exception) {
        // toss config file not found exception since it is expected for a new setup
      }

      if (!saveConfig())
        Console.WriteLine("Unable to save config file");
      else
        Console.WriteLine("Setup finished successfully");

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
