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
using Newtonsoft.Json;

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
    private const String indexFileName = "client_index.db";

    private static String configFile = null;
    private static String logFile = null;
    private static FileIndex fileIndex = null;
    private static String dataDir = null;

    // config file constants

    public static readonly String CONFIG_SERVER = "serverName";
    public static readonly String CONFIG_PORT = "serverPort";
    public static readonly String CONFIG_USER = "user";
    public static readonly String CONFIG_PASSWORD = "password";
    public static readonly String CONFIG_DIR = "directory";

    // state members

    private Queue<String> outQueue = new Queue<String>();
    private HashSet<String> incommingFiles = new HashSet<string>();
    private ClientAccount account = new ClientAccount();
    private Dictionary<String, MyFile> S = new Dictionary<String, MyFile>();  // serverFileList
    private DirectoryWatcher directoryWatcher = null;
    private bool waiting = false;
    private Signal lastReceivedOperation;
    private Socket socket = null;
    private byte[] inputSignalBuffer = new byte[1];
    private Thread gatherDirectoryUpdatesThread;

    private bool paused = false;

    /// <summary>
    /// The timer that is used to wait for 2 seconds of silence before catchupSync is called
    /// </summary>
    private AutoResetEvent resetSilenceTimer = new AutoResetEvent(false);

    // properties

    public bool Paused {
      get { return paused; }
    }

    public static String ConfigFile {
      get { return configFile; }
    }
    
    public static String LogFile {
      get { return logFile; }
    }

    public String DataDir {
      get { return dataDir; }
    }

    public ClientAccount Account {
      get { return account; }
    }

    #endregion

    #region overlay icon handling

    public delegate void OverlayHanderDelegate(bool upToDate);

    public static OverlayHanderDelegate OverlayHandler;

    private void setOverlay(bool upToDate) {
      if (OverlayHandler != null)
        OverlayHandler(upToDate);
    }

    #endregion

    #region status handling

    public delegate void StatusHandlerDelegate(ClientStatus status);

    public static StatusHandlerDelegate StatusHandler;

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
    public static List<LoggingHandlerDelegate> LogHandlers = new List<LoggingHandlerDelegate>();

    /// <summary>
    /// The logic for adding and removing logging handlers
    /// </summary>
    public static event LoggingHandlerDelegate LoggingHandlers {
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
    private static void writeMessage(String message) {
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

      enableDirListener();

      writeMessage("Client ready. Startup sync.");

      // if the index is missing, make it the same as the client listing. Then C vs I will produce no actions.
      if (!fileIndex.FoundAtInit)
        fileIndex.RefreshIndex(dataDir);

      // perform an initial sync to catch all the files that have changed while the client was off
      fullSync();
    }
    
    /// <summary>
    /// Get a local file listing and store them in a filename=>MyFile dictionary
    /// </summary>
    /// <returns></returns>
    private Dictionary<String, MyFile> getLocalFileList() {

      Dictionary<String, MyFile> C = new Dictionary<String, MyFile>();

      try {

        List<MyFile> files = Common.GetFilesRecursive(dataDir);

        foreach (MyFile thisFile in files)
          C.Add(thisFile.name, thisFile);        

      } catch (Exception e) {
        writeMessage("Error populating local file list " + e.Message);
      }

      return C;

    }

    /// <summary>
    /// Load a config file and set member variables accordingly
    /// </summary>
    /// <param name="configFile"></param>
    public void LoadConfig(String configFile) {

      writeMessage("Loading config file " + configFile);

      try {
        IniParser iniParser = new IniParser(configFile);

        // TODO: turn these strings into constants that can be referred to

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

      dataDir = account.Directory;
    }

    /// <summary>
    /// Check the outgoing queue for items and send them each to the server
    /// </summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void processOutQueue() {

      if (outQueue.Count > 0) {
        setStatus(ClientStatus.SYNCING);
        sendCommandToServer(Signal.c2s);
        MyFile myFile = Common.SendFile(outQueue.Dequeue(), socket, dataDir);
        if (myFile != null)
          fileIndex.Update(myFile); // TODO: perform this after server confirmation

        processOutQueue();
      }

      setStatus(ClientStatus.READY);
    }

    /// <summary>
    /// Tell server to delete a file or folder
    /// </summary>
    /// <param name="name"></param>
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void deleteOnServer(String name) {

      writeMessage("Telling server to delete item " + name);

      try {
        sendCommandToServer(Signal.deleteOnServer);
        Common.SendString(socket, name);

        // TODO: wait for reply before updating index
        fileIndex.Remove(name);

      } catch (Exception e) {
        writeMessage("error requesting server item delete " + e.Message);
      }
    }

    /// <summary>
    /// Tell server to create directory
    /// </summary>
    /// <param name="name"></param>
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void createDirectoryOnServer(String name) {
      writeMessage("Telling server to create directory " + name);

      try {
        sendCommandToServer(Signal.createDirectoryOnServer);
        Common.SendString(socket, name);
        // TODO: wait for reply to know that it was created on server before updating index
        fileIndex.Update(new MyFile(name, 'd', Common.GetModTime(dataDir + name), 0, "0"));
      } catch (Exception e) {
        writeMessage("error requesting server directory create: " + e.Message);
      }
    }

    /// <summary>
    /// Ask server to send a file to this client
    /// </summary>
    /// <param name="name"></param>
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void requestFile(String name) {
      writeMessage("Requesting file " + name);

      try {
        sendCommandToServer(Signal.clientWants);
        Common.SendString(socket, name);

      } catch (Exception e) {
        writeMessage("error requesting file: " + e.Message);
      }

      incommingFiles.Add(name);
    }

    /// <summary>
    /// Running sync routine that compares the client files to the index. Does not consult the server.
    /// </summary>
    private void catchupSync() {

      writeMessage("disabling listener");
      disableDirListener(); // hack while incoming set gets figured out

      setStatus(ClientStatus.SYNCING);

      // get full local file list
      Dictionary<String, MyFile> C = getLocalFileList();

      // get index list
      Dictionary<String, MyFile> I = fileIndex.GetFiles();

      // compare to local DB
      writeMessage("catchupSync comparing C=" + C.Count + " to I=" + I.Count);

      // TODO: index updates should be transactioned/prepared

      foreach (KeyValuePair<String, MyFile> file in C) {

        String name = file.Key;
        MyFile c = file.Value;

        if (I.ContainsKey(name)) {

          // TODO: handle conflicts where a file and directory have the same name

          MyFile i = I[name];

          // if it is a file
          if (!Directory.Exists(dataDir + name)) {

            if (c.modtime != i.modtime) { // if times differ
              writeMessage(name + " c.modtime=" + c.modtime + " i.modtime=" + i.modtime);

              // if times differ, push the file to the server and update the index
              writeMessage(name + " = transfer from client to server since file changed");
              outQueue.Enqueue(c.name);
            }

          }

          I.Remove(name);
          // if it is a directory, do nothing because the files will already be coppied one by one

        } else {  // if it exists in C and not in index, push to server

          if (Directory.Exists(dataDir + name)) {
            writeMessage(name + " = create directory on server since new directory");
            createDirectoryOnServer(name);
          } else {
            writeMessage(name + " = transfer from client to server since new file");
            outQueue.Enqueue(c.name);
          }

        }
      }

      // now process items that are in the index and not on the client

      foreach (KeyValuePair<String, MyFile> file in I) {
        writeMessage(file.Key + " = remove from server");
        deleteOnServer(file.Key); // delete file or directory on server
      }


      // push changes to server

      processOutQueue();

      writeMessage("enableing listener since sync is done");
      enableDirListener();

//      writeMessage("Sync finished " + DateTime.UtcNow);

      if (outQueue.Count == 0)
        setStatus(ClientStatus.READY);

      // TODO: set to READY once incomming finish

      Thread.Sleep(2000);
      checkSync();
    }

    /// <summary>
    /// Compares the client to the client index to the server
    /// </summary>
    private void fullSync() {

      writeMessage("disabling listener");
      disableDirListener(); // hack while incoming set gets figured out

      setStatus(ClientStatus.SYNCING);

      writeMessage("fullSync started  " + DateTime.Now);

      // TODO: update all time comparisons to respect server/client time differences

      // populate S
      // file list according to server
      bool listReturned = serverDiscussion(Signal.requestServerFileList, Signal.requestServerFileList_response, null);

      if (!listReturned) {
        throw new Exception("requestServerFileList did not return in time");
      }

      // file list according to client filesystem
      Dictionary<String, MyFile> C = getLocalFileList();

      // file list according to client index database
      Dictionary<String, MyFile> I = fileIndex.GetFiles();

      // holds the name=>action according to the I vs C vs S comparison
      Dictionary<String, Signal> fileActionList = new Dictionary<string, Signal>();

      // here we make the assumption that fullSync() is only called once and right after initial connection
      long lastSync = -1;

      if (fileIndex.FoundAtInit) 
        lastSync = fileIndex.LastUpdate;

      writeMessage("fullSync comparing C=" + C.Count + " to I=" + I.Count + " to S=" + S.Count
                   + "   lastSync=" + lastSync);


      foreach (KeyValuePair<String, MyFile> file in C) {

        String name = file.Key;
        MyFile c = file.Value;

        if (I.ContainsKey(name)) {

          // TODO: handle conflicts where a file and directory have the same name

          MyFile i = I[name];

          // if it is a file
          if (!Directory.Exists(dataDir + name)) {

            if (c.modtime != i.modtime) { // if times differ
              writeMessage(name + " " + " c.modtime=" + c.modtime + " i.modtime=" + i.modtime);
              writeMessage(name + " = transfer from client to server since file changed");

              fileActionList.Add(name, Signal.c2s);
            }
          }

          I.Remove(name);

          // if it is a directory, do nothing because the files will already be coppied one by one

        }
        else {  // if it exists in C and not in index, push to server

          if (Directory.Exists(dataDir + name)) {
            writeMessage(name + " = create directory on server since new directory");
            fileActionList.Add(name, Signal.createDirectoryOnServer);
          }
          else {
            writeMessage(name + " = transfer from client to server since new file");
            fileActionList.Add(name, Signal.c2s);
          }

        }
      }

      // now process items that are in the index and not on the client

      foreach (KeyValuePair<String, MyFile> file in I) {
        // delete file or directory on server
        writeMessage(file.Key + " = remove from server since it is in I but not C");
        fileActionList.Add(file.Key, Signal.deleteOnServer);
      }

      writeMessage("finished addressing C vs I");


      // TODO: handle case where there is the same name item but it is a file on the client and dir on server

      foreach (KeyValuePair<String, MyFile> file in C) {

        String name = file.Key;
        MyFile c = file.Value;

        if (S.ContainsKey(name)) {
          MyFile s = S[name];

          // if it is not a directory and the times are different, compare times
          if (!Directory.Exists(dataDir + name) && c.modtime != s.modtime) {

            writeMessage(name + " " + lastSync + " c.modtime=" + c.modtime + " s.modtime=" + s.modtime);

            if (lastSync == -1) {
              writeMessage(name + " = conflict, since index is gone the newest file cannot be determined");
            }
            else if (c.modtime > lastSync) {
              if (s.modtime > lastSync) {
                writeMessage(name + " = conflict (both client and server file are new)");
              }
              else {
                writeMessage(name + " = transfer from client to server 1");

                if (fileActionList.ContainsKey(name)) { // can this be turned into a nested function perhaps?
                  if (fileActionList[name] != Signal.c2s) {
                    // conflict
                  }
                } else {
                  fileActionList.Add(name, Signal.c2s);
                }

              }
            }
            else {
              if (s.modtime > c.modtime) {
                writeMessage(name + " = transfer from server to client 1");

                if (fileActionList.ContainsKey(name)) {
                  if (fileActionList[name] != Signal.clientWants) {
                    // conflict
                  }
                }
                else {
                  fileActionList.Add(name, Signal.clientWants);
                }
                // TODO: set overlay icon
              }
              else {
                writeMessage(name + " = conflict (both client and server file are old)");
              }
            }
          }
          S.Remove(name);
        }
        else {

          if (c.modtime > lastSync) { // will occur if index is missing since lastSync will be -1, thus performing a merge

            if (Directory.Exists(dataDir + name)) {
              writeMessage(name + " = create directory on server");

              if (fileActionList.ContainsKey(name)) {
                if (fileActionList[name] != Signal.createDirectoryOnServer) {
                  // conflict
                }
              }
              else {
                fileActionList.Add(name, Signal.createDirectoryOnServer);
              }

            }
            else {
              writeMessage(name + " = transfer from client to server 2");

              if (fileActionList.ContainsKey(name)) {
                if (fileActionList[name] != Signal.c2s) {
                  // conflict
                }
              }
              else {
                fileActionList.Add(name, Signal.c2s);
              }

            }

          }
          else {

            writeMessage(name + " = remove on client");

            if (fileActionList.ContainsKey(name)) {
              if (fileActionList[name] != Signal.deleteOnClient) {
                // conflict
              }
            }
            else {
              fileActionList.Add(name, Signal.deleteOnClient);
            }

          }

        }
      }

      foreach (KeyValuePair<String, MyFile> file in S) {

        String name = file.Key;
        MyFile s = file.Value;

        if (s.modtime > lastSync) { // will occur if index is missing since lastSync will be -1, thus performing a merge

          if (s.type == 'd') {
            writeMessage(name + " = create local directory on client");

            if (fileActionList.ContainsKey(name)) {
              if (fileActionList[name] != Signal.createDirectoryOnClient) {
                // conflict
              }
            }
            else {
              fileActionList.Add(name, Signal.createDirectoryOnClient);
            }

          }
          else {
            writeMessage(name + " = transfer from server to client 2");

            if (fileActionList.ContainsKey(name)) {
              if (fileActionList[name] != Signal.clientWants) {
                // conflict
              }
            }
            else {
              fileActionList.Add(name, Signal.clientWants);
            }
            // TODO: set overlay icon
          }

        }
        else {

          writeMessage(name + " = remove from server");  // file or directory

          if (fileActionList.ContainsKey(name)) {
            if (fileActionList[name] != Signal.deleteOnServer) {
              // conflict
            }
          }
          else {
            fileActionList.Add(name, Signal.deleteOnServer);
          }

        }
      }

      // now process the fileLists

      writeMessage("Processing " + fileActionList.Count + " items on action list...");

      foreach (KeyValuePair<String, Signal> signalItem in fileActionList) {

        writeMessage(" " + signalItem.Key + " => " + signalItem.Value);

        switch (signalItem.Value) {
          case Signal.c2s:
            outQueue.Enqueue(signalItem.Key);
            break;

          case Signal.createDirectoryOnServer:
            createDirectoryOnServer(signalItem.Key);
            break;

          case Signal.deleteOnServer:
            deleteOnServer(signalItem.Key);
            break;

          case Signal.clientWants:
            requestFile(signalItem.Key);
            break;

          case Signal.deleteOnClient:
            if (Common.DeleteLocal(dataDir + signalItem.Key))
              fileIndex.Remove(signalItem.Key);
            break;

          case Signal.createDirectoryOnClient:
            if (Common.CreateLocalDirectory(dataDir + signalItem.Key))
              fileIndex.Update(new MyFile(signalItem.Key, 'd', Common.GetModTime(dataDir + signalItem.Key),
              0, "0"));
            break;

          default:
            throw new Exception("Unhandled signal in action list");

        }
      }

      processOutQueue();

      writeMessage("enableing listener since sync is done");
      enableDirListener();

      writeMessage("Sync finished " + DateTime.Now);

      if (incommingFiles.Count == 0)
        setStatus(ClientStatus.READY);


      Thread.Sleep(2000);
      checkSync();
    }


    /// <summary>
    /// Debug function for checking if directories and index are in sync. To be performed after a sync finishes.
    /// </summary>
    private void checkSync() {

      int count = 0;

      writeMessage("checkSync...");

      // populate S
      bool listReturned = serverDiscussion(Signal.requestServerFileList, Signal.requestServerFileList_response, null);

      if (!listReturned) {
        throw new Exception("requestServerFileList did not return in time");
        //Common.ExitError();
      }

      Dictionary<String, MyFile> C = getLocalFileList();

      Dictionary<String, MyFile> I = fileIndex.GetFiles();

      writeMessage("checkSync comparing C=" + C.Count + " to I=" + I.Count + " to S=" + S.Count);

      foreach (KeyValuePair<String, MyFile> file in I) {
        if (C.ContainsKey(file.Key) && S.ContainsKey(file.Key)) {
          C.Remove(file.Key);
          S.Remove(file.Key);
        }
        else {
          writeMessage("mismatch: " + file.Key);
          count++;
        }
      }

      foreach (KeyValuePair<String, MyFile> file in S) {
        writeMessage("mismatch: " + file.Key);
        count++;
      }

      foreach (KeyValuePair<String, MyFile> file in C) {
        writeMessage("mismatch: " + file.Key);
        count++;
      }

      writeMessage("checkSync finished with " + count + " mismatches");
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

//      writeMessage("Connected: " + socket);

      listenToServer();

      List<string> outArgs = new List<string>();
      outArgs.Add(account.User);
      outArgs.Add(account.Password);

      String jsonOut = JsonConvert.SerializeObject(outArgs);

      Console.WriteLine("jsonOut: "+ jsonOut);

      if (!serverDiscussion(Signal.attachaccount, Signal.attachaccount_response, jsonOut)) {
        writeMessage("Unable to attach account");
        throw new Exception("Unable to attach account");
      }

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
      fullSync();
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

      writeMessage("Establishing connection to port " + account.ServerPort + ". Please wait ...");

      attemptConnection(5);
    }

    /// <summary>
    /// Sends a message to the server and then periodically polls for the expected response.
    /// </summary>
    /// <param name="messageToServer"></param>
    /// <param name="expectedReturnCommand"></param>
    /// <param name="argument">Optional additional string to send along with the signal</param>
    /// <returns>true if the expected response returned before the wait expired</returns>
    private bool serverDiscussion(Signal messageToServer, Signal expectedReturnCommand, String argument) {

      int pollseconds = 1;  // TODO: make readonly globals
      int pollcount = 30;  // amount of times to poll

      writeMessage("serverDiscussion starting with expected return: " + expectedReturnCommand.ToString());
      writeMessage("serverDiscussion sending message to server: " + messageToServer.ToString());

      sendCommandToServer(messageToServer);
      if (argument != null) {
        //try {
          Common.SendString(socket, argument);
        //}
        //catch (Exception e) {
          //
        //}
      }

      for (int i = 0; i < pollcount; i++) {
        Thread.Sleep(pollseconds * 1000);
        
        if (expectedReturnCommand == lastReceivedOperation) {
          writeMessage("serverDiscussion returning true with: " + lastReceivedOperation);
          lastReceivedOperation = Signal.empty;
          return true;
        }
      }

      writeMessage("serverDiscussion returning false");
      lastReceivedOperation = Signal.empty;

      return false;
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
    public static void SetConfigDir(String absPath) {

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
      writeMessage("DirectoryUpdate " + action + " " + items);

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

      catchupSync();
    }

    /// <summary>
    /// Send a command signal to the server
    /// </summary>
    /// <param name="signal"></param>
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void sendCommandToServer(Signal signal) {
      try {
        socket.Send(Common.SignalToBuffer(signal));
      }
      catch (IOException ioe) {
        writeMessage(" ERROR sending: " + ioe.Message);
      }
    }

    /// <summary>
    /// Deal with incoming signal from server
    /// </summary>
    /// <param name="signal"></param>
    private void handleInput(Signal signal) {

      if (paused)
        return;

      writeMessage("Handling input for signal " + signal);

      // TODO: make sure these all update the index

      setStatus(ClientStatus.SYNCING);

      switch (signal) {
        case Signal.s2c:
          MyFile newFile = Common.ReceiveFile(socket, dataDir);
          if (newFile != null) {
            fileIndex.Update(newFile);
            incommingFiles.Remove(newFile.name);
            setOverlay(true);
          }
          break;

        case Signal.deleteOnClient:
          // catchup operation
          String relPath = Common.ReceiveString(socket);
          if (Common.DeleteLocal(dataDir + relPath))
            fileIndex.Remove(relPath);
          break;

        case Signal.createDirectoryOnClient:
          // catchup operation
          relPath = Common.ReceiveString(socket);
          if (Common.CreateLocalDirectory(dataDir + relPath))
            fileIndex.Update(new MyFile(relPath, 'd', Common.GetModTime(dataDir + relPath), 0, "0"));
          
          break;

        case Signal.requestServerFileList_response:

          String jsonStringFiles = Common.ReceiveString(socket);

          List<List<string>> fileDict =
            JsonConvert.DeserializeObject<List<List<string>>>(jsonStringFiles);

          S.Clear();

          foreach(List<string> fileItem in fileDict){
            // TODO: try, catch for parse errors etc
            S.Add(fileItem[0], new MyFile(fileItem[0], char.Parse(fileItem[1]), long.Parse(fileItem[2]),
             long.Parse(fileItem[3]), fileItem[4] ));
          }

          break;

        case Signal.attachaccount_response:

          // TODO: replace JSON parser with simple text parsing so we dont have to lug around the dependency

          Dictionary<string, string> jsonMap =
            JsonConvert.DeserializeObject<Dictionary<string, string>>(Common.ReceiveString(socket));

          if (jsonMap["status"] != "success") {// TODO: change to signal
            writeMessage("Unable to attach account. Server response: " + jsonMap["error"]);
            // TODO: catch these exceptions above somewhere
            //throw new Exception("Unable to attach account. Server response: " + jsonMap["error"]);
            //socket.Close();
            Stop();
          }
          else {
            //writeMessage("set account salt to: " + account.Salt);

            if (Common.AppVersion != jsonMap["serverMyboxVersion"]) {
              writeMessage("Client and Server Mybox versions do not match");
            }
          }

          break;

        default:
          writeMessage("Unknown command from server: " + signal);
          break;
      }

      lastReceivedOperation = signal;

      if (incommingFiles.Count == 0 && outQueue.Count == 0)
        setStatus(ClientStatus.READY);

    }

    /// <summary>
    /// Listen to the server via threadless async callback
    /// </summary>
    private void listenToServer() {
      //socket.Blocking = true;
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
          handleInput(Common.BufferToSignal(inputSignalBuffer));
          listenToServer();
        }
      } catch (Exception) {
        writeMessage("closed by remote host");
        //close();
        Start();
      }
    }


  }

}
