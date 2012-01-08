/**
    Mybox version 0.3.0
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
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace mybox {

  /// <summary>
  /// Byte signal enumeration for commands sent between server and client
  /// </summary>
  public enum Signal : byte {
    empty = 0,
//    clientWantsToSend = 1,
//    clientWantsToSend_response = 2,
    clientWants = 3,
    deleteOnServer = 4,
//    renameOnServer = 5,
    createDirectoryOnServer = 6,
    requestServerFileList = 7,
    requestServerFileList_response = 8,
    attachaccount = 9,
    attachaccount_response = 10,
    s2c = 11,
    deleteOnClient = 12,
//    renameOnClient = 13,
    createDirectoryOnClient = 14,
    c2s = 15,
    yes = 16,
    no = 17
  }


  /// <summary>
  /// Structure for holding information about a file
  /// </summary>
  public class MyFile {

    public String name; // TODO: make this a getter property
		public long modtime;  // when the file data was last modified

    public long updatetime; // when the file was last transfered     // is this being used anymore?

    public char type; // d=directory, f=file, l=link (link not yet supported)

    public MyFile(String name, char type, long modtime, long updatetime) {
      this.name = name;
      this.modtime = modtime;
      this.type = type;
      this.updatetime = updatetime;
    }

  }

  /// <summary>
  /// A class which is used to store common functions common to the client and server
  /// </summary>
  public class Common {

    /// <summary>
    /// Mybox version number
    /// </summary>
    public const String AppVersion = "0.3.0";
    
    /// <summary>
    /// The default communication port for the server and client socket connection
    /// </summary>
    public const int DefaultCommunicationPort = 4446;

    private static int buf_size = 1024;

    /// <summary>
    /// The user's system home directory primarialy used for determining where the config directory is
    /// </summary>
    public static readonly String UserHome = (Environment.OSVersion.Platform == PlatformID.Unix ||
               Environment.OSVersion.Platform == PlatformID.MacOSX)
              ? Environment.GetEnvironmentVariable("HOME")
              : Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");

    /// <summary>
    /// Convert the signal to a byte array
    /// </summary>
    /// <param name="signal"></param>
    /// <returns></returns>
    public static byte[] SignalToBuffer(Signal signal) {
      return new byte[] { (byte)signal };
    }

    /// <summary>
    /// Convert a byte array to a single byte signal
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    public static Signal BufferToSignal(byte[] buffer) {
      return (Signal)buffer[0];
    }

    /// <summary>
    /// Get the current timestamp in UTC as a long
    /// </summary>
    /// <returns></returns>
    public static long NowUtcLong() {
      return DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// Get the data modification time of a local file
    /// </summary>
    /// <param name="fullPath"></param>
    /// <returns></returns>
    public static long GetModTime(String fullPath) {
      return File.GetLastWriteTimeUtc(fullPath).Ticks;
    }

    /// <summary>
    /// Make sure the input directory path has a trailing slash.
    /// </summary>
    /// <param name="absPath"></param>
    /// <returns>The input if it ends with a slash. The input with a slash appended, if it did not have a slash.</returns>
    public static string EndDirWithSlash(String absPath) {

      if (absPath.EndsWith("/") || absPath.EndsWith(@"\"))
        return absPath;

      return absPath + "/";
    }

    /// <summary>
    /// Display the command line options and then exit.
    /// </summary>
    /// <param name="options"></param>
    public static void ShowCliHelp(OptionSet options, Assembly thisAssembly) {

      Console.WriteLine("Usage: " + Path.GetFileName(thisAssembly.Location) + " <options>");

      options.WriteOptionDescriptions(Console.Out);

      System.Diagnostics.Process.GetCurrentProcess().Kill();
    }

    /// <summary>
    /// Quit the program when an error occurs.
    /// </summary>
    public static void ExitError() {
#if DEBUG
      Console.WriteLine("Press any key to quit...");
      Console.ReadKey(true);
#endif
      Environment.Exit(1);
    }

    /// <summary>
    /// Generates a random salt for password encryption purposes
    /// </summary>
    /// <param name="size">the length in bytes of the salt</param>
    /// <returns></returns>
    public static string GenerateSalt(int size) {
      //Generate a cryptographic random number.
      RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
      byte[] data = new byte[size];
      rng.GetBytes(data);

      // encrypt it so it can be stored in the DB without funky characters
      //SHA1CryptoServiceProvider cryptoProvider = new SHA1CryptoServiceProvider();
      //data = cryptoProvider.ComputeHash(data);

      // Return a Base64 string representation of the random number.
      return Convert.ToBase64String(data);
    }

    /// <summary>
    /// Enrcypt a password with a salt and return the result
    /// </summary>
    /// <param name="pwd"></param>
    /// <param name="salt"></param>
    /// <returns></returns>
    public static string EncryptPassword(string pwd, string salt) {

      SHA1CryptoServiceProvider cryptoProvider = new SHA1CryptoServiceProvider();

      //byte[] data = Convert.FromBase64String(pwd + salt);// does not work
      byte[] data = System.Text.Encoding.ASCII.GetBytes(pwd + salt);  // is this safe?

      // TODO: loop this encryption 5-1000 times
      data = cryptoProvider.ComputeHash(data);

      return Convert.ToBase64String(data);
    }


    /// <summary>
    /// Gets a recursive listing of files from a directory
    /// </summary>
    /// <param name="baseDir"></param>
    /// <returns></returns>
    public static List<MyFile> GetFilesRecursive (string baseDir) {

      List<MyFile> result = new List<MyFile>();

      Stack<string> stack = new Stack<string> ();

      stack.Push (baseDir);

      while (stack.Count > 0) {

        string dir = stack.Pop();

        try {

          string[] files = Directory.GetFiles(dir, "*.*");

          foreach (string absPath in files) {
            result.Add(new MyFile(absPath.Replace(baseDir, ""), 'f', Common.GetModTime(absPath), -1));
          }

          //result.AddRange(Directory.GetFiles(dir, "*.*"));

          foreach (string absPath in Directory.GetDirectories(dir)) {
            result.Add(new MyFile(absPath.Replace(baseDir, ""), 'd', Common.GetModTime(absPath), -1));
            stack.Push(absPath);
          }
        }
        catch {
          // Could not open the directory
        }
      }
      return result;
    }

    /// <summary>
    /// Create a directory on the local filesystem if it does not exist
    /// </summary>
    /// <param name="absPath"></param>
    /// <returns>true if the directory exists when the function finishes</returns>
    public static bool CreateLocalDirectory(String absPath) {

      Console.WriteLine("Creating local directory " + absPath);

      if (!Directory.Exists(absPath)) {
        try {
          Directory.CreateDirectory(absPath);
        } catch (Exception e) {
          Console.WriteLine("Error creating directory: " + e.Message);
          return false;
        }
      }

      return true;
    }

    /// <summary>
    /// Delete a local file or directory
    /// </summary>
    /// <param name="absPath">the absolute path to the item</param>
    /// <returns></returns>
    public static bool DeleteLocal(String absPath) {

      Console.WriteLine("Deleting local item " + absPath);

      try {
        if (File.Exists(absPath))
          File.Delete(absPath);
        else if (Directory.Exists(absPath))
          Directory.Delete(absPath, true);
      }
      catch (Exception e) {
        Console.WriteLine("Error deleting: " + e.Message);
        return false;
      }

      return true;
    }

    /// <summary>
    /// Send a string along a socket
    /// </summary>
    /// <param name="socket"></param>
    /// <param name="str"></param>
    /// <returns></returns>
    public static bool SendString(Socket socket, String str) {

      byte[] strBytes = Encoding.UTF8.GetBytes(str); //file name
      byte[] lenBytes = BitConverter.GetBytes((Int16)(strBytes.Length)); //length of file name

      socket.Send(lenBytes);//2
      socket.Send(strBytes);//lenBytes

      return true;
    }

    /// <summary>
    /// Get a string from a socket which is sent by SendString
    /// </summary>
    /// <param name="socket"></param>
    /// <returns></returns>
    public static String ReceiveString(Socket socket) {
      byte[] lengthBuffer = new byte[2];

      // string length
      socket.Receive(lengthBuffer, 2, 0);
      Int16 length = BitConverter.ToInt16(lengthBuffer, 0);

      // FIXME: This fails with memory error with large amounts of bytes
      byte[] dataBuffer = new byte[length]; // TODO: remove array memory allocation from this function

      // string
      socket.Receive(dataBuffer, length, 0);
      return System.Text.Encoding.UTF8.GetString(dataBuffer, 0, length);
    }


    //[MethodImpl(MethodImplOptions.Synchronized)]
    //public /*synchronized*/ void SendCommand(Socket socket, Signal signal) {
    //  try {
    //    socket.Send(Common.signalToBuffer(signal));
    //  } catch (IOException ioe) {
    //    Console.WriteLine(handle + " ERROR sending: " + ioe.Message);
    //    close();
    //  }
    //}

    //public static bool SendTimestamp(Socket socket, long timestamp) {
    //  byte[] buffer = BitConverter.GetBytes(timestamp);
    //  socket.Send(buffer);
    //  return true;
    //}

    //public static long ReceiveTimestamp(Socket socket) {
    //  byte[] buffer = new byte[8];
    //  socket.Receive(buffer, 8, 0);
    //  return BitConverter.ToInt64(buffer, 0);
    //}

    /// <summary>
    /// Send a local file accross a socket
    /// </summary>
    /// <param name="relPath">the relative path inside the data directory</param>
    /// <param name="socket"></param>
    /// <param name="baseDir">the base directory for which to append the relPath to</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static MyFile SendFile(String relPath, Socket socket, String baseDir) {

      MyFile myFile = null;

      try {
        String fullPath = baseDir + relPath;

        byte[] fileName = Encoding.UTF8.GetBytes(relPath); //file name
        byte[] fileNameLen = BitConverter.GetBytes((Int16)(fileName.Length)); //length of file name
        byte[] fileData = File.ReadAllBytes(fullPath); //file
        byte[] fileDataLen = BitConverter.GetBytes(fileData.Length); // file length

        long modtime = Common.GetModTime(fullPath);

        byte[] timestamp = BitConverter.GetBytes(modtime); // assume long = int64 = 8 bytes

        Console.WriteLine("Sending file " + relPath + " " + modtime);

        socket.Send(fileNameLen);//2
        socket.Send(fileName);
        socket.Send(timestamp);//8
        socket.Send(fileDataLen);//4
        socket.Send(fileData);

        myFile = new MyFile(relPath, 'f', modtime, Common.NowUtcLong());
      }
      catch (Exception e) {
        Console.WriteLine("Operation failed: " + e.Message);
      }

      return myFile;
    }


    /// <summary>
    /// Receive a file over a socket
    /// </summary>
    /// <param name="socket"></param>
    /// <param name="baseDir">the base directory the file will live in</param>
    /// <returns></returns>
    public static MyFile ReceiveFile(Socket socket, string baseDir) {

      byte[] buffer = new byte[buf_size];

      MyFile myFile = null;

      try {
        // name length, name, data length, data
        socket.Receive(buffer, 2, 0);
        Int16 nameLength = BitConverter.ToInt16(buffer, 0);

        socket.Receive(buffer, nameLength, 0);
        String relPath = System.Text.Encoding.UTF8.GetString(buffer, 0, nameLength);

        // timestamp
        socket.Receive(buffer, 8, 0);
        DateTime timestamp = DateTime.FromBinary(BitConverter.ToInt64(buffer, 0));

        Console.WriteLine("Receiving file: " + relPath + " " + timestamp.Ticks/* + " " + BitConverter.ToString(buffer, 0, 8)*/);

        // data
        socket.Receive(buffer, 4, 0); // assumes filesize cannot be larger then int bytes, 4GB?
        Int32 fileLength = BitConverter.ToInt32(buffer, 0);

        int fileBytesRead = 0;


        String absPath = baseDir + relPath;

        FileStream fs = File.Create(absPath, buf_size);

        while (fileBytesRead + buf_size <= fileLength) {
          fileBytesRead += socket.Receive(buffer, buf_size, 0);
          fs.Write(buffer, 0, buf_size);
        }

        if (fileBytesRead < fileLength) {
          socket.Receive(buffer, fileLength - fileBytesRead, 0);  // make sure this reads to the end
          fs.Write(buffer, 0, fileLength - fileBytesRead);
        }

        fs.Close();

        File.SetLastWriteTimeUtc(absPath, timestamp);

        myFile = new MyFile(relPath, 'f', timestamp.Ticks, Common.NowUtcLong());
      }
      catch (Exception e) {
        Console.WriteLine("Operation failed: " + e.Message);
      }

      return myFile;
    }

  }
}
