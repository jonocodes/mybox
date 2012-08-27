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
using System.IO;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;

namespace mybox {

  /// <summary>
  /// Enum primarially used to indicate sync status to a client GUI
  /// </summary>
  public enum ClientStatus {
    CONNECTING, DISCONNECTED, READY, SYNCING, PAUSED, ERROR
  }
 

  /// <summary>
  /// Structure for holding account info on the client side
  /// </summary>
  public class ClientAccount {
    public String ServerName = null;
    public int ServerPort = Common.DefaultCommunicationPort;
    public String User = null;
    public String Directory = ClientServerConnection.DefaultClientDir;
    //public String Salt = null;
    public String Password = null;
  }

  /// <summary>
  /// The communication and worker class for the client side
  /// </summary>
  public class ClientServerConnection {

    #region members and getters

    // config members

    public static readonly String DefaultClientDir = Common.UserHome + "/Mybox/";
    public static readonly String DefaultConfigDir = Common.UserHome + "/.mybox/";

    private const String configFileName = "client.ini";
    private const String logFileName = "client.log";
    private const String indexFileName = "client.db";

    private String configFile = null;
    private String logFile = null;
    private FileIndex fileIndex = null;
    private String absDataDir = null;

    // config file constants

    public static readonly String CONFIG_SERVER = "server";
    public static readonly String CONFIG_PORT = "serverPort";
    public static readonly String CONFIG_USER = "user";
    public static readonly String CONFIG_PASSWORD = "password";
    public static readonly String CONFIG_DIR = "directory";

    // state members

    private ClientAccount account = new ClientAccount();
    private DirectoryWatcher directoryWatcher = null;
    private bool waiting = false;
    private Socket socket = null;
    private Thread gatherDirectoryUpdatesThread;

    private bool paused = false;
    private bool catchupSync = false;
    
    private bool listeningToServer = false;
    
    private DirSyncer dirSyncer = null;

    public static JavaScriptSerializer JsonSerializer = new JavaScriptSerializer();
    private byte[] inputSignalBuffer = new byte[1];
    

    /// <summary>
    /// The timer that is used to wait for 2 seconds of silence before catchupSync is called
    /// </summary>
    private AutoResetEvent resetSilenceTimer = new AutoResetEvent(false);

    // properties

    public bool Paused {
      get { return paused; }
    }

    public String ConfigFile {
      get { return configFile; }
    }
    
    public String LogFile {
      get { return logFile; }
    }

    public String DataDir {
      get { return absDataDir; }
    }

    public ClientAccount Account {
      get { return account; }
    }

    #endregion

    #region overlay icon handling

    public delegate void OverlayHanderDelegate(bool upToDate);

    public OverlayHanderDelegate OverlayHandler;

    private void setOverlay(bool upToDate) {
      if (OverlayHandler != null)
        OverlayHandler(upToDate);
    }

    #endregion

    #region status handling

    public delegate void StatusHandlerDelegate(ClientStatus status);

    public StatusHandlerDelegate StatusHandler;

    private void setStatus(ClientStatus status) {
      if (StatusHandler != null)
        StatusHandler(status);
    }

    #endregion

    #region logging

    /// <summary>
    /// This defines what type of method is used for logging an event
    /// </summary>
    /// <param name="message"></param>
    public delegate void LoggingHandlerDelegate(String message);

    /// <summary>
    /// this will be a dynamic list of logging handlers
    /// </summary>
    public List<LoggingHandlerDelegate> LogHandlers = new List<LoggingHandlerDelegate>();

    /// <summary>
    /// The logic for adding and removing logging handlers
    /// </summary>
    public event LoggingHandlerDelegate LoggingHandlers {
      add {
        if (!LogHandlers.Contains(value)) {
          LogHandlers.Add(value);
        }
      }
      remove {
        LogHandlers.Remove(value);
      }
    }

    /// <summary>
    /// helper method which passes a message to all of the event handlers
    /// </summary>
    /// <param name="message"></param>
    private void writeMessage(String message) {
      foreach (LoggingHandlerDelegate handler in LogHandlers) {
        handler(message);
      }
    }

    #endregion

    public ClientServerConnection() {
      setStatus(ClientStatus.DISCONNECTED);
    }


    /// <summary>
    /// Connect to the server and perform a full sync
    /// </summary>
    public void Start() {
      attemptConnection(5);

      writeMessage("Client ready. Startup sync.");
      
      sync();
    }
    
    /// <summary>
    /// Load a config file and set member variables accordingly
    /// </summary>
    /// <param name="configFile"></param>
    public void LoadConfig(String configFile) {

      try {
        IniParser iniParser = new IniParser(configFile);

        account.ServerName = iniParser.GetSetting("settings", CONFIG_SERVER); // returns NULL when not found
        account.ServerPort = int.Parse(iniParser.GetSetting("settings", CONFIG_PORT));
        account.User = iniParser.GetSetting("settings", CONFIG_USER);
        account.Directory = iniParser.GetSetting("settings", CONFIG_DIR);
        account.Password = iniParser.GetSetting("settings", CONFIG_PASSWORD);
      } catch (FileNotFoundException e) {
        throw new Exception(e.Message);
      }

      // make sure directory ends with a slash
      account.Directory = Common.EndDirWithSlash(account.Directory);

      // check values

      if (account.ServerName == null || account.ServerName == string.Empty) {
        throw new Exception("Unable to determine host from settings file");
      }

      if (account.User == null || account.User == string.Empty) {
        throw new Exception("Unable to determine user id");
      }

      if (account.Directory == null)
        account.Directory = DefaultClientDir;

      if (!Directory.Exists(account.Directory)) {
        throw new Exception("Directory " + account.Directory + " does not exist");
      }

      absDataDir = Common.EndDirWithoutSlash(account.Directory);
    }


    //public void stop() {
    //  disableDirListener();
    //  setStatus(ClientStatus.DISCONNECTED);
    //  //socket.Close();
    //}

    //public void close() {
    //  setStatus(ClientStatus.DISCONNECTED);

    //  // tidy up
    //  socket.Shutdown(SocketShutdown.Both);
    //  socket.Disconnect(false);
    //  socket.Close();
    //}



    /// <summary>
    /// Try to connect to the server and attach an account
    /// </summary>
    /// <param name="pollInterval">number of seconds between connection retry</param>
    private void attemptConnection(int pollInterval) {

      if (account.ServerName == null) {
        throw new Exception("Client not configured");
      }

      setStatus(ClientStatus.CONNECTING);

      writeMessage("Establishing connection to " + account.ServerName + ":" + account.ServerPort + " ...");

      // repeatedly attempt to connect to the server
      while (true) {

        socket = ConnectSocket(account.ServerName, account.ServerPort);

        if (socket != null)
          break; // reachable if there is no exception thrown

        writeMessage("There was an error reaching server. Will try again in " + pollInterval + " seconds...");
        Thread.Sleep(1000 * pollInterval);

      }

      List<string> outArgs = new List<string>();
      outArgs.Add(account.User);
      outArgs.Add(account.Password);

      String jsonOut = JsonSerializer.Serialize(outArgs);

      writeMessage("jsonOut: "+ jsonOut);

      sendCommandToServer(Signal.attachaccount);
      Common.SendString(socket, jsonOut);
      
      Dictionary<string, string> jsonMap =
        JsonSerializer.Deserialize<Dictionary<string, string>>(Common.ReceiveString(socket));

      if (jsonMap["status"] != "success") {// TODO: change to signal
        writeMessage("Unable to attach account. Server response: " + jsonMap["error"]);
        // TODO: catch these exceptions above somewhere
        //throw new Exception("Unable to attach account. Server response: " + jsonMap["error"]);
        //socket.Close();
        Stop();
      }
      else {
        if (Common.AppVersion != jsonMap["serverMyboxVersion"]) {
          writeMessage("Client and Server Mybox versions do not match");
        }
      }
      
      dirSyncer = new DirSyncer(absDataDir, fileIndex, socket);
      
      setStatus(ClientStatus.READY);
    }


    public void Stop() {

      writeMessage("Disconnecting started");

      socket.Disconnect(false);

      disableDirListener();

      setStatus(ClientStatus.DISCONNECTED);

      writeMessage("Disconnecting finished");
    }

    /// <summary>
    /// Pause syncing. Stops listening to the server and stops dlistening for directory updates.
    /// </summary>
    public void Pause() {

      // TODO: disable listenToServer
      paused = true;
      disableDirListener();

      setStatus(ClientStatus.PAUSED);

      writeMessage("Pausing finished");
    }

    /// <summary>
    /// Resumes syncing. Enables listening to the server and listening for directory updates. Then performs a full sync.
    /// </summary>
    public void Unpause() {

      paused = false;

      writeMessage("Unpausing started");

      //listenToServer();
      //fullSync();
      enableDirListener();

      writeMessage("Unausing finished");
    }

    /// <summary>
    /// Enables the directory update watcher
    /// </summary>
    public void enableDirListener() {
      writeMessage("Listening on directory " + account.Directory);

      if (directoryWatcher == null)
        directoryWatcher = new DirectoryWatcher(account.Directory, this);
      else
        directoryWatcher.Listen();
    }

    /// <summary>
    /// Disables the directory update watcher
    /// </summary>
    public void disableDirListener() {
      if (directoryWatcher != null)
        directoryWatcher.Pause();
    }

    /// <summary>
    /// Initiates the client mode for just connecting to get the account
    /// </summary>
    /// <returns></returns>
    public void StartGetAccountMode(ClientAccount account) {

      if (account.ServerName == null) {
        throw new Exception("Client not configured");
      }

      this.account = account;

      attemptConnection(5);
    }
    
    /// <summary>
    /// Connect a socket to a server and port
    /// </summary>
    /// <param name="server"></param>
    /// <param name="port"></param>
    /// <returns>null if not connected</returns>
    public static Socket ConnectSocket(string server, int port) {
      Socket s = null;
      IPHostEntry hostEntry = null;

      // Get host related information.
      hostEntry = Dns.GetHostEntry(server);

      // Loop through the AddressList to obtain the supported AddressFamily. This is to avoid
      // an exception that occurs when the host IP Address is not compatible with the address family
      // (typical in the IPv6 case).
      foreach (IPAddress address in hostEntry.AddressList) {
        IPEndPoint ipe = new IPEndPoint(address, port);
        Socket tempSocket = new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        try {
          tempSocket.Connect(ipe);
        }
        catch (Exception) {
          continue;
        }
        if (tempSocket.Connected) {
          s = tempSocket;
          break;
        }
        else {
          continue;
        }
      }
      return s;
    }

    /// <summary>
    /// Set the config directory
    /// </summary>
    /// <param name="absPath"></param>
    public void SetConfigDir(String absPath) {

      if (!Directory.Exists(absPath))
        throw new Exception("Specified config directory does not exist: " + absPath);

      configFile = absPath + configFileName;

      if (!File.Exists(configFile))
        throw new Exception("Config file " + configFile + " not found");

      logFile = absPath + logFileName;

      fileIndex = new FileIndex(absPath + indexFileName);
    }

    /// <summary>
    /// This function is called by the directory watcher to notify the client that files have changed
    /// </summary>
    /// <param name="action">the type of change (currently not being used)</param>
    /// <param name="items">the item that changed</param>
    public void DirectoryUpdate(String action, String items) {
      writeMessage("Directory Listener Update " + action + " " + items);

      if (gatherDirectoryUpdatesThread != null && waiting) {
        resetSilenceTimer.Set(); // resets the timer
      }
      else {
        gatherDirectoryUpdatesThread = new Thread(updateSilenceTimer);
        gatherDirectoryUpdatesThread.Start();
      }

      waiting = true;
    }

    /// <summary>
    /// This function is responsible for making sure the sync does not happen until 2 seconds of
    /// update silence.
    /// </summary>
    private void updateSilenceTimer() {

      while (resetSilenceTimer.WaitOne(2000)) { }  // returns false when the signal is not received

      waiting = false;

      writeMessage("Wait finished.");

      sync();
    }

    //[MethodImpl(MethodImplOptions.Synchronized)]
    private void sync() {
    
      setStatus(ClientStatus.SYNCING);

      disableDirListener();
      
      if (listeningToServer)
        socket.Send(Common.SignalToBuffer(Signal.clientWantsToSync));

      dirSyncer.Sync(catchupSync);
      
      listenToServer();  // put client into listening mode
      
      enableDirListener();
      
      setStatus(ClientStatus.READY);
      
      catchupSync = false;
    }

    /// <summary>
    /// Send a command signal to the server
    /// </summary>
    /// <param name="signal"></param>
//    [MethodImpl(MethodImplOptions.Synchronized)]
    private void sendCommandToServer(Signal signal) {
      try {
        socket.Send(Common.SignalToBuffer(signal));
      }
      catch (IOException ioe) {
        writeMessage(" ERROR sending: " + ioe.Message);
      }
    }

    /// <summary>
    /// Listen to the server via threadless async callback
    /// </summary>
    private void listenToServer() {
      listeningToServer = true;
      socket.BeginReceive(inputSignalBuffer, 0, 1, SocketFlags.None, new AsyncCallback(onReceiveSignalComplete), null);
    }

    /// <summary>
    /// Async callback for Socket listener
    /// </summary>
    /// <param name="iar"></param>
    private void onReceiveSignalComplete(IAsyncResult iar) {
      try {
        int count = socket.EndReceive(iar);
        if (count == 0) {
          writeMessage("closed by remote host");
          //close();
          Start();
        } else {
        
          listeningToServer = false;
          
          Signal input = Common.BufferToSignal(inputSignalBuffer);
          
          if (input == Signal.serverRequestingSync) {
            disableDirListener();
            catchupSync = true;
            sync();
          } else if (input == Signal.serverReadyToSync) {
            // just toss it, becasuse we want it to waste the listener
          }
          else
            throw new Exception("Client received unknown signal " + inputSignalBuffer);

//          listenToServer();
        }
      } catch (Exception) {
        writeMessage("closed by remote host");
        //close();
        Start();
      }
      
    }

  }

}
