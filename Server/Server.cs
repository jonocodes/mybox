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
using System.Text;
using System.Net;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Data;

namespace mybox {

  /// <summary>
  /// Command line executable for the server component. Manages multiple client connections.
  /// </summary>
  public class Server {

    #region members

    private Dictionary<IntPtr, ServerClientConnection> clients = new Dictionary<IntPtr, ServerClientConnection>();

    // map of userId => set of all connected clients that belong to that user
    private Dictionary<String, HashSet<IntPtr>> multiClientMap = new Dictionary<String, HashSet<IntPtr>>();

    public static int DefaultQuota = 50;  // size in megabytes
    public static int Port = Common.DefaultCommunicationPort;
    public static String AccountsDbfile = null;
    public AccountsDB Accounts = null;

    private static String baseDataDir = null;

    public static readonly String DefaultAccountsDbFile = Common.UserHome + "/.mybox/mybox_server_accounts.db";
    public static readonly String DefaultConfigFile = Common.UserHome + "/.mybox/mybox_server.ini";
    public static readonly String DefaultBaseDataDir = Common.UserHome + "/.mybox/mbServerSpace/";
 //   public static readonly String logFile = Common.UserHome + "/.mybox/mybox_server.log";
    private const String indexFileNamePostfix = "_index.db";

    #endregion

    /// <summary>
    /// Constructor. Maintains a loop for listening for incoming clients.
    /// </summary>
    /// <param name="configFile"></param>
    public Server(String configFile) {

      Console.WriteLine("Starting server");
      Console.WriteLine("Loading config file " + configFile);

      LoadConfig(configFile);

      Console.WriteLine("database: " + AccountsDbfile);

      Accounts = new AccountsDB(AccountsDbfile);

      TcpListener tcpListener = new TcpListener(IPAddress.Any, Port);

      try {
        tcpListener.Start();
      }
      catch (SocketException e) {
        Console.WriteLine("Unable to start listener on port " + Port + e.Message);
        throw;
      }

      while (true) {
        Console.WriteLine(" waiting for client to connect...");
        Socket listenerSocket = tcpListener.AcceptSocket();

        Console.WriteLine("Client connected with handle " + listenerSocket.Handle);
        ServerClientConnection client = new ServerClientConnection(this, listenerSocket);
        clients.Add(listenerSocket.Handle, client);
      }
    }

    /// <summary>
    /// Set member variables from config file
    /// </summary>
    /// <param name="configFile"></param>
    public static void LoadConfig(String configFile) {

      try {
        IniParser iniParser = new IniParser(configFile);

        Port = int.Parse(iniParser.GetSetting("settings", "port"));  // returns NULL when not found ?
        DefaultQuota = int.Parse(iniParser.GetSetting("settings", "defaultQuota"));
        baseDataDir = iniParser.GetSetting("settings", "baseDataDir");
        AccountsDbfile = iniParser.GetSetting("settings", "accountsDbFile");
      } catch (FileNotFoundException e) {
        Console.WriteLine(e.Message);
        Common.ExitError();
      }

      if (AccountsDbfile == null)
        AccountsDbfile = DefaultAccountsDbFile;

      if (baseDataDir == null)
        baseDataDir = DefaultBaseDataDir;

      baseDataDir = Common.EndDirWithSlash(baseDataDir);

      Common.CreateLocalDirectory(baseDataDir);

    }

    /// <summary>
    /// Add a client connection handle to the client multimap
    /// </summary>
    /// <param name="id"></param>
    /// <param name="handle"></param>
    public void AddToMultiMap(String id, IntPtr handle) {

      if (multiClientMap.ContainsKey(id)) {
        HashSet<IntPtr> thisMap = multiClientMap[id];
        thisMap.Add(handle);
        multiClientMap[id] = thisMap;  // should overwrite the old map
      }
      else {
        HashSet<IntPtr> thisMap = new HashSet<IntPtr>();
        thisMap.Add(handle);
        multiClientMap[id] = thisMap;
      }
    }

    /// <summary>
    /// Get the absolute path to the data directory for an account on the server. It should end with a slash.
    /// </summary>
    /// <param name="account"></param>
    /// <returns></returns>
    public static String GetAbsoluteDataDirectory(AccountsDB.Account account) {
      return baseDataDir + account.id + "/";
    }

    /// <summary>
    /// Get the location of the index file for an account on the server.
    /// </summary>
    /// <param name="account"></param>
    /// <returns></returns>
    public static String GetIndexLocation(AccountsDB.Account account) {
      return baseDataDir + account.id + indexFileNamePostfix;
    }

    public MyFile SendIndex(AccountsDB.Account account, Socket socket) {
      return Common.SendFile(account.id + indexFileNamePostfix, socket, baseDataDir);
    }

    /// <summary>
    /// Send catchup commands to all connected clients attached to the same account
    /// </summary>
    /// <param name="myHandle">the handle of the client with the originating signal</param>
    /// <param name="accountId">the account that the client belongs to</param>
    /// <param name="inputOperation">the original operation that was made</param>
    /// <param name="arg">additional arguments to send along with the operation</param>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SpanCatchupOperation(IntPtr myHandle, String accountId, Signal inputOperation, String arg) {

      //    Console.WriteLine("spanCatchupOperation from " + myHandle + " to all account=" + accountId + " (" + operation.toString() +","+ arg +")");

      HashSet<IntPtr> thisMap = multiClientMap[accountId];

      foreach (IntPtr thisHandle in thisMap) {
        if (thisHandle == myHandle)
          continue;

        Console.WriteLine("spanCatchupOperation from " + myHandle + " to " + thisHandle + " (" + inputOperation.ToString() + "," + arg + ")");

        try {
          clients[thisHandle].SendCatchup(inputOperation, arg);
        }
        catch (Exception e) {
          Console.WriteLine("Exception in spanCatchupOperation " + e.Message);
          Common.ExitError();
        }
      }

    }

    /// <summary>
    /// Disconnect a ServerClientConnection from the server
    /// </summary>
    /// <param name="handle"></param>
    public void removeConnection(IntPtr handle) {

      // update the client map

      ServerClientConnection toTerminate = clients[handle];

      if (toTerminate.Account != null) {
        Console.WriteLine("Removing client " + handle + " (" + toTerminate.Account.email + ")");

        HashSet<IntPtr> thisMap = multiClientMap[toTerminate.Account.id];
        thisMap.Remove(handle);
        multiClientMap[toTerminate.Account.id] = thisMap;
      }
      else
        Console.WriteLine("Removing accountless client " + handle);

      //try {
      //  toTerminate.stopListener();
      //}
      //catch (IOException ioe) {
      //  Console.WriteLine("Error closing thread: " + ioe);
      //}

      //      Console.WriteLine("Removing client " + handle);

      // remove from list
      clients.Remove(handle);
    }



    /// <summary>
    /// Start the server executable and handle command line arguments
    /// </summary>
    /// <param name="args"></param>
    public static void Main(string[] args) {

      OptionSet options = new OptionSet();

      String configFile = DefaultConfigFile;

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

      new Server(configFile);

    }
  }


}

