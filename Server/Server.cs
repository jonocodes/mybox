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
using System.Text;
using System.Net;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Linq;


namespace mybox {

  /// <summary>
  /// Command line executable for the server component. Manages multiple client connections.
  /// </summary>
  public class Server {

    #region members

    // map of handle => conncetion
    private Dictionary<IntPtr, ServerClientConnection> clients = new Dictionary<IntPtr, ServerClientConnection>();

    // map of userId => set of all connected clients that belong to that user
    private Dictionary<String, HashSet<IntPtr>> multiClientMap = new Dictionary<String, HashSet<IntPtr>>();

//    public static int DefaultQuota = 50;  // size in megabytes
    public int Port = Common.DefaultCommunicationPort;
    public IServerDB serverDB = null;

    public static readonly String DefaultConfigFile = Common.UserHome + "/.mybox/server.ini";
 //   public static readonly String logFile = Common.UserHome + "/.mybox/mybox_server.log";

    public static readonly String CONFIG_PORT = "port";
    public static readonly String CONFIG_DIR = "baseDataDir";
    public static readonly String CONFIG_DBSTRING = "serverDbConnectionString";
    public static readonly String CONFIG_BACKEND = "backend";


    public static void WriteMessage(String message) {
#if DEBUG
      Console.WriteLine("SERVER: {0}", message);
#else
      Console.WriteLine(DateTime.Now + " : " + message);
#endif
    
    }

    #endregion

    /// <summary>
    /// Constructor. Maintains a loop for listening for incoming clients.
    /// </summary>
    /// <param name="configFile"></param>
    public Server(String configFile) {

      WriteMessage("Starting server");
      WriteMessage("Loading config file " + configFile);

      Port = LoadConfig(configFile, out serverDB);
      serverDB.RebuildFilesTable();

      TcpListener tcpListener = new TcpListener(IPAddress.Any, Port);

      try {
        tcpListener.Start();
      }
      catch (SocketException e) {
        WriteMessage("Unable to start listener on port " + Port + e.Message);
        throw;
      }

      while (true) {
        WriteMessage(" waiting for client to connect...");
        Socket listenerSocket = tcpListener.AcceptSocket();

        WriteMessage("Client connected with handle " + listenerSocket.Handle);
        ServerClientConnection client = new ServerClientConnection(this, listenerSocket);
        clients.Add(listenerSocket.Handle, client);
      }
    }
    /*
    public static HashSet<Type> GetBackends() {

      HashSet<Type> result = new HashSet<Type>();

      var types = Assembly.GetExecutingAssembly().GetTypes().Where(m => m.IsClass && m.GetInterfaces().Contains(typeof(IServerDB)));

      foreach (var type in types)
        result.Add(type);

      return result;
    }
    */
    /// <summary>
    /// Set member variables from config file
    /// </summary>
    /// <param name="configFile"></param>
    public static int LoadConfig(String configFile, out IServerDB serverDB) {

      serverDB = null;

      int port = -1;

      try {
        IniParser iniParser = new IniParser(configFile);

        port = int.Parse(iniParser.GetSetting("settings", CONFIG_PORT));  // returns NULL when not found ?
        String baseDataDir = iniParser.GetSetting("settings", CONFIG_DIR);
        String serverDbConnectionString = iniParser.GetSetting("settings", CONFIG_DBSTRING);
        Type dbType = Type.GetType(iniParser.GetSetting("settings", CONFIG_BACKEND));

        baseDataDir = Common.EndDirWithoutSlash(baseDataDir);

        serverDB = (IServerDB)Activator.CreateInstance(dbType);
        serverDB.Connect(serverDbConnectionString, baseDataDir);

      } catch (FileNotFoundException e) {
        Console.WriteLine(e.Message);
        Common.ExitError();
      }

      return port;
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

    public void RemoveFromMultiMap(String id, IntPtr handle) {

      if (multiClientMap.ContainsKey(id)) {
        HashSet<IntPtr> thisMap = multiClientMap[id];

        if (thisMap.Contains(handle)) {
          thisMap.Remove(handle);
          multiClientMap[id] = thisMap;
        }
      }
    }
    
/*
    public void TellClientsToSync(IntPtr myHandle, String accountId) {
    
      HashSet<IntPtr> thisMap = multiClientMap[accountId];

      foreach (IntPtr thisHandle in thisMap) {
        if (thisHandle == myHandle)
          continue;
          
      clients[thisHandle].TellClientToSync();
    
    }
*/

//    /// <summary>
//    /// Send catchup commands to all connected clients attached to the same account
//    /// </summary>
//    /// <param name="myHandle">the handle of the client with the originating signal</param>
//    /// <param name="accountId">the account that the client belongs to</param>
//    /// <param name="inputOperation">the original operation that was made</param>
//    /// <param name="arg">additional arguments to send along with the operation</param>
//    [MethodImpl(MethodImplOptions.Synchronized)]
//    public void SpanCatchupOperation(IntPtr myHandle, String accountId, Signal inputOperation, String arg) {
//
//      HashSet<IntPtr> thisMap = multiClientMap[accountId];
//
//      foreach (IntPtr thisHandle in thisMap) {
//        if (thisHandle == myHandle)
//          continue;
//
//        WriteMessage("spanCatchupOperation from " + myHandle + " to " + thisHandle + " (" + inputOperation.ToString() + "," + arg + ")");
//
//        try {
//          clients[thisHandle].SendCatchup(inputOperation, arg);
//        }
//        catch (Exception e) {
//          WriteMessage("Exception in spanCatchupOperation " + e.Message);
//          Common.ExitError();
//        }
//      }
//
//    }

    /// <summary>
    /// Disconnect a ServerClientConnection from the server
    /// </summary>
    /// <param name="handle"></param>
    public void RemoveConnection(IntPtr handle) {

      if (clients.ContainsKey(handle)) {
  
        ServerClientConnection toTerminate = clients[handle];
  
        if (toTerminate.User != null) {
          WriteMessage("Removing client " + handle + " " + toTerminate.User);
          RemoveFromMultiMap(toTerminate.User.id, handle);
        }
        else
          WriteMessage("Removing accountless client " + handle);
        /*
        try {
          toTerminate.StopListener();
        }
        catch (IOException ioe) {
          Console.WriteLine("Error closing thread: " + ioe);
        }
  */
        // remove from list
        clients.Remove(handle);
      }
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

