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

    public AccountsDB.Account Account = null;

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
      catch (Exception) {
        Console.WriteLine("closed by remote host");
        close();
      }
    }

    /// <summary>
    /// Close the connection
    /// </summary>
    private void close() {
      if (server != null)
        server.removeConnection(handle);
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
    /// <param name="email"></param>
    /// <returns></returns>
    private bool attachAccount(String email) {

      Account = server.Accounts.GetAccountByEmail(email);

      if (Account == null) {
        Console.WriteLine("Account does not exist " + email); // TODO: return false?
        return false;
      }

      dataDir = Server.GetAbsoluteDataDirectory(Account);

      // create directory if it does not exist
      if (!Directory.Exists(dataDir))
        Directory.CreateDirectory(dataDir);

      Console.WriteLine("Attached account " + Account + " to handle " + handle);
      Console.WriteLine("Local server storage in: " + dataDir);

      return true;
    }

    /// <summary>
    /// Check the outgoing queue for files to send to the client and then send them!
    /// </summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void checkOutQueue() {

      if (outQueue.Count > 0) {
        sendCommandToClient(Signal.s2c);  // should ove into SendFile
        Common.SendFile(outQueue.Dequeue(), socket, dataDir);
        checkOutQueue();
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
          MyFile myFile = Common.RecieveFile(socket, dataDir);
          server.SpanCatchupOperation(handle, Account.id, signal, myFile.name);
          break;

        //case Signal.clientWantsToSend:
        //  String relPath = Common.RecieveString(socket);
        //  long timestamp = Common.RecieveTimestamp(socket);

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
          String relPath = Common.RecieveString(socket);
          if (File.Exists(dataDir + relPath)) {
            outQueue.Enqueue(relPath);
            checkOutQueue();
          }
          break;

        case Signal.deleteOnServer:
          relPath = Common.RecieveString(socket);
          Common.DeleteLocal(dataDir + relPath);
          server.SpanCatchupOperation(handle, Account.id, signal, relPath);
          break;

        case Signal.renameOnServer:
          relPath = Common.RecieveString(socket);
          String relPathB = Common.RecieveString(socket);

          if (File.Exists(dataDir + relPath))
            Common.RenameLocal(dataDir + relPath, dataDir + relPathB);

          server.SpanCatchupOperation(handle, Account.id, signal, relPath + "->" + relPathB);
          break;

        case Signal.createDirectoryOnServer:
          relPath = Common.RecieveString(socket);
          Common.CreateLocalDirectory(dataDir + relPath);
          server.SpanCatchupOperation(handle, Account.id, signal, relPath);
          break;

        case Signal.requestServerFileList:
          List<MyFile> fileList = Common.GetFilesRecursive(dataDir);

          string jsonString = JsonConvert.SerializeObject(fileList);
          // TODO: eventually use MyFile.serialize to save bytes. but even more eventually send the .db file instead of a string

          sendCommandToClient(Signal.requestServerFileList_response);
          Common.SendString(socket, jsonString);

          break;

        case Signal.attachaccount:

          String args = Common.RecieveString(socket);

          Dictionary<string, string> attachInput = JsonConvert.DeserializeObject<Dictionary<string, string>>(args);

          String email = attachInput["email"];
          //        String password = (String)attachInput.get("password");

          Dictionary<string, string> jsonOut = new Dictionary<string, string>();
          jsonOut.Add("serverMyboxVersion", Common.AppVersion);

          if (attachAccount(email)) {
            jsonOut.Add("status", "success");
            jsonOut.Add("quota", Account.quota.ToString());
            jsonOut.Add("salt", Account.salt);

            server.AddToMultiMap(Account.id, handle);
          }
          else {
            jsonOut.Add("status", "failed");
            jsonOut.Add("error", "invalid account");

            // TODO: disconnect the client here
          }

          String jsonOutString = JsonConvert.SerializeObject(jsonOut);//fastJSON.JSON.Instance.ToJSON(jsonOut);

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
      else if (inputOperation == Signal.renameOnServer) {  // handles files and directories?
        try {
          Console.WriteLine("catchup rename to client (" + handle + "): " + arg);
          sendCommandToClient(Signal.renameOnClient);
          Common.SendString(socket, arg);
        }
        catch (Exception e) {
          Console.WriteLine("catchup rename to client failed: " + e.Message);
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
