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
using System.Text;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.IO;
using System.Web.Script.Serialization;

namespace mybox {

  /// <summary>
  /// Represents a two way connection from the server to a single client
  /// </summary>
  public class ServerClientConnection {

    #region members

    private Socket socket;
    private Server server;  // parent
    private IntPtr handle = IntPtr.Zero;

    //private Queue<String> outQueue = new Queue<String>();
    private String dataDir = null;
    private byte[] inputSignalBuffer = new byte[1];

    public ServerUser User = null;

    private JavaScriptSerializer jsonSerializer = new JavaScriptSerializer();
    
    private HashSet<int> updatedDirectories = new HashSet<int>();

    #endregion

    public ServerClientConnection(Server server, Socket inClientSocket) {
      this.server = server;
      this.handle = inClientSocket.Handle;
      this.socket = inClientSocket;

      listenToClient();
    }

    /// <summary>
    /// Listen to the client via threadless async callback
    /// </summary>
    private void listenToClient() {
      //socket.Blocking = true;
      socket.BeginReceive(inputSignalBuffer, 0, 1, SocketFlags.None, new AsyncCallback(onReceiveSignalComplete), null);
    }

    /// <summary>
    /// Async callback for client listening
    /// </summary>
    /// <param name="iar"></param>
    private void onReceiveSignalComplete(IAsyncResult iar) {
      try {
        int count = socket.EndReceive(iar);
        if (count == 0) {
          Server.WriteMessage("closed by remote host");
          close();
        }
        else {
          handleInput(Common.BufferToSignal(inputSignalBuffer));

          listenToClient();
        }
      }
      catch (Exception e) {
        Server.WriteMessage("closed by remote host with exception: " + e.Message +"\n" + e.StackTrace);
        close();
      }
    }

    public void StopListener() {
      socket.Close();
      socket = null;
    }

    /// <summary>
    /// Close the connection
    /// </summary>
    private void close() {

//      Server.WriteMessage("close called on server " + server + " eq " + (server != null));

      if (server != null) {
//        Server.WriteMessage("removing connection");
        server.RemoveConnection(handle);
      }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    private void sendCommandToClient(Signal signal) {
      try {
//        socket.Send(Common.SignalToBuffer(signal));
      }
      catch (IOException ioe) {
        Server.WriteMessage(handle + " ERROR sending: " + ioe.Message);
        close();
      }
    }

    /// <summary>
    /// Attempt to authenticate the client via credentials. If it matches an account on the server return true.
    /// </summary>
    /// <param name="userName"></param>
    /// <param name="password"></param>
    /// <returns></returns>
    private bool attachUser(String userName, String password) {

      User = server.serverDB.GetUserByName(userName);

      if (User == null) {
        Server.WriteMessage("User does not exist: " + userName); // TODO: return false?
        return false;
      }

      if (!server.serverDB.CheckPassword(password, User.password)) {
        Server.WriteMessage("Password incorrect for: " + userName);
        return false;
      }

      dataDir = server.serverDB.GetDataDir(User);

      if (!Directory.Exists(dataDir)) {
      
        try {
          Directory.CreateDirectory(dataDir); // TODO: make recursive
        } catch (Exception) {
          Server.WriteMessage("Unable to find or create data directory: " + dataDir);
          return false;
        }
      }

      Server.WriteMessage("Attached account " + userName + " to handle " + handle);
      Server.WriteMessage("Local server storage in: " + dataDir);

      return true;
    }

    /// <summary>
    /// Handle input signals from the client
    /// </summary>
    /// <param name="signal"></param>
    private void handleInput(Signal signal) {

      Server.WriteMessage("Handling input for signal " + signal);

      switch (signal) {
          
        case Signal.syncFinished:
          
          server.serverDB.RecalcDirChecksums(updatedDirectories, int.Parse(User.id));
          
          updatedDirectories.Clear();
          
          // TODO:  server.TellClientsToSync(handle, User.id);
          // here is where we will tell the other clients to sync
          
          
          break;

        case Signal.clientWants:
          String relPath = Common.ReceiveString(socket);
          if (File.Exists(dataDir + relPath)) {
          
//            sendCommandToClient(Signal.s2c);
            Common.SendFile(relPath, socket, dataDir); // TODO: check return value
          
            //outQueue.Enqueue(relPath);
            //processOutQueue();
          }
          break;

        case Signal.c2s:

          MyFile newFile = Common.ReceiveFile(socket, dataDir);

          if (newFile != null)
            updatedDirectories.Add(server.serverDB.UpdateFile(User, newFile));

          //server.SpanCatchupOperation(handle, User.id, signal, newFile.Path);
          break;
          
        case Signal.deleteOnServer:
          relPath = Common.ReceiveString(socket);

          if (Common.DeleteLocal(dataDir + relPath))
            updatedDirectories.Add(server.serverDB.RemoveFile(User, relPath)); // TODO: check return value, or exception

          //server.SpanCatchupOperation(handle, User.id, signal, relPath);
          break;

        case Signal.createDirectoryOnServer:
          relPath = Common.ReceiveString(socket);
          
          if (Common.CreateLocalDirectory(dataDir + relPath))
            updatedDirectories.Add(server.serverDB.UpdateFile(User,
              new MyFile(relPath, FileType.DIR, 0, Common.Md5Hash(string.Empty))));

          //server.SpanCatchupOperation(handle, User.id, signal, relPath);
          break;

        case Signal.requestServerFileList:

          String path = Common.ReceiveString(socket);
          //if (path == ".")
          //  path = string.Empty;
          
          Server.WriteMessage("checking DB for file list for path: " + path);
          
          List<List<string>> fileListToSerialize = server.serverDB.GetDirListSerializable(User, path);

          String jsonOutStringFiles = jsonSerializer.Serialize(fileListToSerialize);  //JsonConvert.SerializeObject(fileListToSerialize);

          Server.WriteMessage("sending json file list: " + jsonOutStringFiles);

          try {
//            sendCommandToClient(Signal.requestServerFileList_response);
            Common.SendString(socket, jsonOutStringFiles);
          }
          catch (Exception e) {
            Server.WriteMessage("Error during " + Signal.requestServerFileList_response + e.Message);
            Common.ExitError();
          }

          break;

        case Signal.attachaccount:

          String args = Common.ReceiveString(socket);

          Server.WriteMessage("received " + args);

          List<string> attachInput = jsonSerializer.Deserialize<List<string>>(args); //JsonConvert.DeserializeObject<List<string>>(args);

          String userName = attachInput[0];
          String password = attachInput[1];

          Dictionary<string, string> jsonOut = new Dictionary<string, string>();
          jsonOut.Add("serverMyboxVersion", Common.AppVersion);

          if (attachUser(userName, password)) {
            jsonOut.Add("status", "success");
            //jsonOut.Add("quota", Account.quota.ToString());
            //jsonOut.Add("salt", Account.salt);

            server.AddToMultiMap(User.id, handle);
          }
          else {
            jsonOut.Add("status", "failed");
            jsonOut.Add("error", "login invalid");

            close ();
            // TODO: disconnect the client here
          }
          
          String jsonOutString = jsonSerializer.Serialize(jsonOut); // String jsonOutString = JsonConvert.SerializeObject(jsonOut);

          try {
//            sendCommandToClient(Signal.attachaccount_response);
            Common.SendString(socket, jsonOutString);
          }
          catch (Exception e) {
            Server.WriteMessage("Error during " + Signal.attachaccount_response + e.Message);
            Common.ExitError();
          }

          Server.WriteMessage("attachaccount_response: " + jsonOutString);

          break;

        default:
          Server.WriteMessage("Unknown command");
          break;

      }

    }

//    /// <summary>
//    /// Send catchup operation to the client based on the original inputOperation
//    /// </summary>
//    /// <param name="inputOperation">the initial operation for which to determine an output operation</param>
//    /// <param name="arg">additional arguments for the input/output operation</param>
//    public void SendCatchup(Signal inputOperation, String arg) {
//
//      if (inputOperation == Signal.c2s) {
//        try {
//          Server.WriteMessage("catchup s2c to client (" + handle + "): " + arg);
//          if (File.Exists(dataDir + arg)) {
//            sendCommandToClient(Signal.s2c);
//            Common.SendFile(arg, socket, dataDir);
//          }
//        }
//        catch (Exception e) {
//          Server.WriteMessage("catchup s2c to client failed: " + e.Message);
//        }
//      }
//      else if (inputOperation == Signal.deleteOnServer) {  // handles files and directories?
//        try {
//          Server.WriteMessage("catchup delete to client (" + handle + "): " + arg);
//          sendCommandToClient(Signal.deleteOnClient);
//          Common.SendString(socket, arg);
//        }
//        catch (Exception e) {
//          Server.WriteMessage("catchup delete to client failed: " + e.Message);
//        }
//      }
//      else if (inputOperation == Signal.createDirectoryOnServer) {
//        try {
//          Server.WriteMessage("catchup createDirectoryOnClient (" + handle + "): " + arg);
//          sendCommandToClient(Signal.createDirectoryOnClient);
//          Common.SendString(socket, arg);
//        }
//        catch (Exception e) {
//          Server.WriteMessage("catchup createDirectoryOnClient failed: " + e.Message);
//        }
//      }
//      else {
//        Server.WriteMessage("unknown command: " + inputOperation);
//      }
//
//    }


  }
}
