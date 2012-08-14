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
using Newtonsoft.Json;

namespace mybox {

  /// <summary>
  /// Represents a two way connection from the server to a single client
  /// </summary>
  public class ServerClientConnection {

    #region members

    private Socket socket;
    private Server server;  // parent
    private IntPtr handle = IntPtr.Zero;

    private Queue<String> outQueue = new Queue<String>();
    private String dataDir = null;
    private byte[] inputSignalBuffer = new byte[1];

    public ServerUser User = null;

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
          Console.WriteLine("closed by remote host");
          close();
        }
        else {
          handleInput(Common.BufferToSignal(inputSignalBuffer));

          listenToClient();
        }
      }
      catch (Exception e) {
        Console.WriteLine("closed by remote host with exception " + e.Message +"\n" + e.StackTrace);
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

//      Console.WriteLine("close called on server " + server + " eq " + (server != null));

      if (server != null) {
//        Console.WriteLine("removing connection");
        server.RemoveConnection(handle);
      }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    private void sendCommandToClient(Signal signal) {
      try {
        socket.Send(Common.SignalToBuffer(signal));
      }
      catch (IOException ioe) {
        Console.WriteLine(handle + " ERROR sending: " + ioe.Message);
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
        Console.WriteLine("User does not exist: " + userName); // TODO: return false?
        return false;
      }

      if (!server.serverDB.CheckPassword(password, User.password)) {
        Console.WriteLine("Password incorrect for: " + userName);
        return false;
      }

      dataDir = server.serverDB.GetDataDir(User);

      if (!Directory.Exists(dataDir)) {
      
        try {
          Directory.CreateDirectory(dataDir); // TODO: make recursive
        } catch (Exception) {
          Console.WriteLine("Unable to find or create data directory: " + dataDir);
          return false;
        }
      }

      Console.WriteLine("Attached account " + userName + " to handle " + handle);
      Console.WriteLine("Local server storage in: " + dataDir);

      return true;
    }

    /// <summary>
    /// Check the outgoing queue for files to send to the client and then send them!
    /// </summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void processOutQueue() {

      if (outQueue.Count > 0) {
        sendCommandToClient(Signal.s2c);
        Common.SendFile(outQueue.Dequeue(), socket, dataDir); // TODO: check return value
        processOutQueue();
      }

    }

    /// <summary>
    /// Handle input signals from the client
    /// </summary>
    /// <param name="signal"></param>
    private void handleInput(Signal signal) {

      Console.WriteLine("Handling input for signal " + signal);

      switch (signal) {
        case Signal.c2s:

          MyFile newFile = Common.ReceiveFile(socket, dataDir);

          if (newFile != null)
            server.serverDB.UpdateFile(User, newFile);

          server.SpanCatchupOperation(handle, User.id, signal, newFile.name);
          break;

        //case Signal.clientWantsToSend:
        //  String relPath = Common.ReceiveString(socket);
        //  long timestamp = Common.ReceiveTimestamp(socket);

        //  sendCommandToClient(Signal.clientWantsToSend_response);

        //  Common.SendString(socket, relPath);

        //  // reply 'yes' if it refers to a file that does not exist or if the times do not match
        //  if (File.Exists(dataDir + relPath) && Common.GetModTime(dataDir + relPath) == timestamp) {
        //    Common.SendString(socket, "no");
        //  }
        //  else {
        //    Common.SendString(socket, "yes");
        //  }
        //  break;

        case Signal.clientWants:
          String relPath = Common.ReceiveString(socket);
          if (File.Exists(dataDir + relPath)) {
            outQueue.Enqueue(relPath);
            processOutQueue();
          }
          break;

        case Signal.deleteOnServer:
          relPath = Common.ReceiveString(socket);

          if (Common.DeleteLocal(dataDir + relPath))
            server.serverDB.RemoveFile(User, relPath);
//            index.Remove(relPath);  // TODO: check return value

          server.SpanCatchupOperation(handle, User.id, signal, relPath);
          break;

        case Signal.createDirectoryOnServer:
          relPath = Common.ReceiveString(socket);
          
          if (Common.CreateLocalDirectory(dataDir + relPath))
            server.serverDB.UpdateFile(User, new MyFile(relPath, 'd', Common.GetModTime(dataDir + relPath)
            , 0, "0"));

          server.SpanCatchupOperation(handle, User.id, signal, relPath);
          break;

        case Signal.requestServerFileList:

          List<List<string>> fileListToSerialize = server.serverDB.GetFileListSerializable(User);

          String jsonOutStringFiles = JsonConvert.SerializeObject(fileListToSerialize);

          Console.WriteLine("sending json file list: " + jsonOutStringFiles);

          try {
            sendCommandToClient(Signal.requestServerFileList_response);
            Common.SendString(socket, jsonOutStringFiles);
          }
          catch (Exception e) {
            Console.WriteLine("Error during " + Signal.requestServerFileList_response + e.Message);
            Common.ExitError();
          }

          break;

        case Signal.attachaccount:

          String args = Common.ReceiveString(socket);

          Console.WriteLine("received " + args);

          List<string> attachInput = JsonConvert.DeserializeObject<List<string>>(args);

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

          String jsonOutString = JsonConvert.SerializeObject(jsonOut);

          try {
            sendCommandToClient(Signal.attachaccount_response);
            Common.SendString(socket, jsonOutString);
          }
          catch (Exception e) {
            Console.WriteLine("Error during " + Signal.attachaccount_response + e.Message);
            Common.ExitError();
          }

          Console.WriteLine("attachaccount_response: " + jsonOutString);

          break;

        default:
          Console.WriteLine("Unknown command");
          break;

      }

    }

    /// <summary>
    /// Send catchup operation to the client based on the original inputOperation
    /// </summary>
    /// <param name="inputOperation">the initial operation for which to determine an output operation</param>
    /// <param name="arg">additional arguments for the input/output operation</param>
    public void SendCatchup(Signal inputOperation, String arg) {

      if (inputOperation == Signal.c2s) {
        try {
          Console.WriteLine("catchup s2c to client (" + handle + "): " + arg);
          if (File.Exists(dataDir + arg)) {
            sendCommandToClient(Signal.s2c);
            Common.SendFile(arg, socket, dataDir);
          }
        }
        catch (Exception e) {
          Console.WriteLine("catchup s2c to client failed: " + e.Message);
        }
      }
      else if (inputOperation == Signal.deleteOnServer) {  // handles files and directories?
        try {
          Console.WriteLine("catchup delete to client (" + handle + "): " + arg);
          sendCommandToClient(Signal.deleteOnClient);
          Common.SendString(socket, arg);
        }
        catch (Exception e) {
          Console.WriteLine("catchup delete to client failed: " + e.Message);
        }
      }
      else if (inputOperation == Signal.createDirectoryOnServer) {
        try {
          Console.WriteLine("catchup createDirectoryOnClient (" + handle + "): " + arg);
          sendCommandToClient(Signal.createDirectoryOnClient);
          Common.SendString(socket, arg);
        }
        catch (Exception e) {
          Console.WriteLine("catchup createDirectoryOnClient failed: " + e.Message);
        }
      }
      else {
        Console.WriteLine("unknown command: " + inputOperation);
      }

    }


  }
}
