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

    public String name; // TODO: make this a getter property. and perhaps change to 'path'
		public long modtime;  // when the file data was last modified
    public char type; // d=directory, f=file, l=link (link not yet supported) // TODO: change to enum

    public long size;
    public String checksum;

    public MyFile(String name, char type, long modtime, long size, String checksum) {
      this.name = name;
      this.modtime = modtime;
      this.type = type;
      this.size = size;
      this.checksum = checksum;
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

    private static DateTime epoch = new DateTime(1970,1,1,0,0,0,0);

    /// <summary>
    /// The system temp directory
    /// </summary>
    public static readonly String TempDir = Path.GetTempPath();

    private static Random _random = new Random();

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
    /// Convert UNIX timestamp to a datetime object
    /// </summary>
    /// <returns>
    /// DateTime
    /// </returns>
    /// <param name='unixTimeStamp'>
    /// Unix time stamp.
    /// </param>
    public static DateTime UnixTimeStampToDateTime( long unixTimeStamp ) {
      return epoch.AddSeconds(unixTimeStamp);
    }

    /// <summary>
    /// Convert DateTime to unix timestamp.
    /// </summary>
    /// <returns>
    /// The unix timestamp.
    /// </returns>
    /// <param name='dateTime'>
    /// DateTime
    /// </param>
    public static long DateTimeToUnixTimestamp(DateTime dateTime) {
      return Convert.ToInt64(Math.Floor((dateTime - epoch).TotalSeconds));
    }

    /// <summary>
    /// Get the data modification time of a local file
    /// </summary>
    /// <param name="fullPath"></param>
    /// <returns></returns>
    public static long GetModTime(String fullPath) {
      return DateTimeToUnixTimestamp(File.GetLastWriteTimeUtc(fullPath));
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
    /// Md5 digest a string.
    /// </summary>
    /// <returns>
    /// The hash.
    /// </returns>
    /// <param name='input'>
    /// Any string
    /// </param>
    public static String Md5Hash (String input) {
      MD5 md5 = new MD5CryptoServiceProvider();
      String result = BitConverter.ToString (md5.ComputeHash (System.Text.Encoding.ASCII.GetBytes (input)));
      return result.Replace ("-", String.Empty).ToLower();
    }


    public static byte[] FileChecksumToBytes(String absPath) {
      MD5 md5 = new MD5CryptoServiceProvider();
      byte[] hash;

      using(Stream fileStream = new FileStream(absPath, FileMode.Open))
        using(Stream bufferedStream = new BufferedStream(fileStream, 1200000))
          hash = md5.ComputeHash(bufferedStream);
      
      return hash;
    }

    public static String FileChecksumToString(String absPath) {
      return BitConverter.ToString(FileChecksumToBytes(absPath)).Replace("-", String.Empty).ToLower();
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
            result.Add(new MyFile(absPath.Replace(baseDir, ""), 'f',
                                  GetModTime(absPath), new FileInfo(absPath).Length,"0"));
          }

          string[] dirs = Directory.GetDirectories(dir);

          foreach (string absPath in dirs) {
            result.Add(new MyFile(absPath.Replace(baseDir, ""), 'd', GetModTime(absPath), 0, "0"));
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

      //Console.WriteLine("ReceiveString getting bytes " + length);

      // FIXME: This fails with memory error with large amounts of bytes
      byte[] dataBuffer = new byte[length]; // TODO: remove array memory allocation from this function

      // string
      socket.Receive(dataBuffer, length, 0);

      //Console.WriteLine("RecieveString got string " +System.Text.Encoding.UTF8.GetString(dataBuffer, 0, length));

      return System.Text.Encoding.UTF8.GetString(dataBuffer, 0, length);
    }

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
        
        // TODO: make sure "file length" matches actual file size
        
        long modtime = Common.GetModTime(fullPath); // TODO: make timestamps into int
        byte[] timestamp = BitConverter.GetBytes(modtime); // assume long = int64 = 8 bytes
        
        // temporarially calc checksum here, though should be done higher up
        byte[] checksum = FileChecksumToBytes(fullPath);
        String checksumString = BitConverter.ToString(checksum).Replace("-", String.Empty).ToLower();

        Console.WriteLine("Sending file " + relPath + " " + modtime);

        socket.Send(fileNameLen);//2
        socket.Send(fileName);
        
        socket.Send(timestamp);//8
        
        socket.Send(checksum);//16 bytes, or 32 characters?
        socket.Send(fileDataLen);//4, TODO: set this to 8 bits for files larger then 4GB ?
        socket.Send(fileData);
        
        myFile = new MyFile(relPath, 'f', modtime, fileData.Length, checksumString);
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
        // receive order: name length, name, timestamp, checksum, data length, data
        
        socket.Receive(buffer, 2, 0);
        Int16 nameLength = BitConverter.ToInt16(buffer, 0);

        socket.Receive(buffer, nameLength, 0);
        String relPath = System.Text.Encoding.UTF8.GetString(buffer, 0, nameLength);

        // timestamp
        socket.Receive(buffer, 8, 0); // TODO: make timestamps into int
        DateTime timestamp = UnixTimeStampToDateTime(BitConverter.ToInt64(buffer, 0));
        
        // checksum
        socket.Receive(buffer, 16, 0);
        String checksumString = BitConverter.ToString(buffer, 0, 16).Replace("-", String.Empty).ToLower();

        String tempLocation = TempDir + Path.DirectorySeparatorChar + "mb" +
          DateTimeToUnixTimestamp(DateTime.Now).ToString() + _random.Next(0, 26).ToString() + _random.Next(0, 26).ToString();

        Console.WriteLine("Receiving file: " + relPath);
        Console.WriteLine("  timestmp:" + DateTimeToUnixTimestamp(timestamp) + " checksum: " + checksumString);
        Console.WriteLine("  temp: " + tempLocation);
        
        // data
        socket.Receive(buffer, 4, 0); // assumes filesize cannot be larger then int bytes, 4GB?
        Int32 fileLength = BitConverter.ToInt32(buffer, 0);

        int fileBytesRead = 0;

        MD5 md5 = MD5.Create();

        using (FileStream fs = File.Create(tempLocation, buf_size)) {
          using (CryptoStream cs = new CryptoStream(fs, md5, CryptoStreamMode.Write)) {

            while (fileBytesRead + buf_size <= fileLength) {
              fileBytesRead += socket.Receive(buffer, buf_size, 0);
              cs.Write(buffer, 0, buf_size);
            }

            if (fileBytesRead < fileLength) {
              socket.Receive(buffer, fileLength - fileBytesRead, 0);  // make sure this reads to the end
              cs.Write(buffer, 0, fileLength - fileBytesRead);
            }
          }
        }

        String calculatedChecksum = BitConverter.ToString(md5.Hash).Replace("-", String.Empty).ToLower();

        // network fault tolerance, varify checksum before moving file from temp to dir
        System.Console.WriteLine("  calculated checksum: " + calculatedChecksum);

        if (calculatedChecksum == checksumString) {

          File.Move(tempLocation, baseDir + relPath);
          File.SetLastWriteTimeUtc(baseDir + relPath, timestamp);

          myFile = new MyFile(relPath, 'f', timestamp.Ticks, fileLength, checksumString);
        }
        else
          throw new Exception("Received file checksum did not match");
      }
      catch (Exception e) {
        Console.WriteLine("Operation failed: " + e.Message);
      }

      return myFile;
    }

  }
  /*
  /// <summary>
  /// Php BB crypto service provider.
  /// Depricated since the hashes are non-portable?
  /// </summary>
  public class phpBBCryptoServiceProvider
  {
    /// <summary>
    /// The encryption string base.
    /// </summary>
    private string itoa64 = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public phpBBCryptoServiceProvider() {
      Console.WriteLine("initializeing phpBBCryptoServiceProvider");
    }

    /// <summary>
    /// Compares the password string given with the hash retrieved from your database.
    /// </summary>
    /// <param name="password">Plaintext password.</param>
    /// <param name="hash">Hash from a SQL database</param>
    /// <returns>True if the password is correct, False otherwise.</returns>
    public bool phpbbCheckHash(string password, string hash)
    {
        if (hash.Length == 34) return (hashCryptPrivate(ASCIIEncoding.ASCII.GetBytes(password), hash, itoa64) == hash);
        return false;
    }

    /// <summary>
    /// This function will return the resulting hash from the password string you specify.
    /// </summary>
    /// <param name="password">String to hash.</param>
    /// <returns>Encrypted hash.</returns>
    /// <remarks>
    /// Although this will return the md5 for an older password, I have not added
    /// support for older passwords, so they will not work with this class unless
    /// I or someone else updates it.
    /// </remarks>
    public string phpbb_hash(string password)
    {
        Console.WriteLine("phpbb_hash called");

        // Generate a random string from a random number with the length of 6.
        // You could use a static string instead, doesn't matter. E.g.
        // byte[] random = ASCIIEncoding.ASCII.GetBytes("abc123");
        byte[] random = ASCIIEncoding.ASCII.GetBytes(new Random().Next(100000, 999999).ToString());

        Console.WriteLine("random " + random);
  
        string hash = hashCryptPrivate(ASCIIEncoding.ASCII.GetBytes(password), hashGensaltPrivate(random, itoa64), itoa64);

        Console.WriteLine("hash length: " + hash.Length);
        Console.WriteLine("hash: " + hash);

        if (hash.Length == 34) return hash;

        return sMD5(password);
    }

    /// <summary>
    /// The workhorse that encrypts your hash.
    /// </summary>
    /// <param name="password">String to be encrypted. Use: ASCIIEncoding.ASCII.GetBytes();</param>
    /// <param name="genSalt">Generated salt.</param>
    /// <param name="itoa64">The itoa64 string.</param>
    /// <returns>The encrypted hash ready to be compared.</returns>
    /// <remarks>
    /// password:  Saves conversion inside the function, lazy coding really.
    /// genSalt:   Returns from hashGensaltPrivate(random, itoa64);
    /// return:    Compare with phpbbCheckHash(password, hash)
    /// </remarks>
    private string hashCryptPrivate(byte[] password, string genSalt, string itoa64)
    {
        string output = "*";
        MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
        if (!genSalt.StartsWith("$H$")) return output;
        //   $count_log2 = strpos($itoa64, $setting[3]);
        int count_log2 = itoa64.IndexOf(genSalt[3]);
        if (count_log2 < 7 || count_log2 > 30) return output;

        int count = 1 << count_log2;
        byte[] salt = ASCIIEncoding.ASCII.GetBytes(genSalt.Substring(4, 8));

        if (salt.Length != 8) return output;

        byte[] hash = md5.ComputeHash(Combine(salt, password));

        do
        {
            hash = md5.ComputeHash(Combine(hash, password));
        } while (count-- > 1);

        output = genSalt.Substring(0, 12);
        output += hashEncode64(hash, 16, itoa64);

        return output;
    }

    /// <summary>
    /// Private function to concat byte arrays.
    /// </summary>
    /// <param name="b1">Source array.</param>
    /// <param name="b2">Array to add to the source array.</param>
    /// <returns>Combined byte array.</returns>
    private byte[] Combine(byte[] b1, byte[] b2)
    {
        byte[] retVal = new byte[b1.Length + b2.Length];
        Array.Copy(b1, 0, retVal, 0, b1.Length);
        Array.Copy(b2, 0, retVal, b1.Length, b2.Length);
        return retVal;
    }

    /// <summary>
    /// Encode the hash.
    /// </summary>
    /// <param name="input">The hash to encode.</param>
    /// <param name="count">[This parameter needs documentation].</param>
    /// <param name="itoa64">The itoa64 string.</param>
    /// <returns>Encoded hash.</returns>
    private string hashEncode64(byte[] input, int count, string itoa64)
    {
        string output = "";
        int i = 0; int value = 0;

        do
        {
            value = input[i++];
            output += itoa64[value & 0x3f];

            if (i < count) value |= input[i] << 8;
            output += itoa64[(value >> 6) & 0x3f];
            if (i++ >= count)
                break;

            if (i < count) value |= input[i] << 16;
            output += itoa64[(value >> 12) & 0x3f];
            if (i++ >= count)
                break;

            output += itoa64[(value >> 18) & 0x3f];

        } while (i < count);

        return output;
    }

    /// <summary>
    /// Generate salt for hash generation.
    /// </summary>
    /// <param name="input">Any random information.</param>
    /// <param name="itoa64">The itoa64 string.</param>
    /// <returns>Generated salt string</returns>
    private string hashGensaltPrivate(byte[] input, string itoa64)
    {
        int iteration_count_log2 = 6;

        string output = "$H$";
        output += itoa64[Math.Min(iteration_count_log2 + 5, 30)];
        output += hashEncode64(input, 6, itoa64);

        return output;
    }

    /// <summary>
    /// Returns a hexadecimal string representation for the encrypted MD5 parameter.
    /// </summary>
    /// <param name="password">String to be encrypted.</param>
    /// <returns>String</returns>
    private string sMD5(string password) { return sMD5(password, false); }

    /// <summary>
    /// Returns a hexadecimal string representation for the encrypted MD5 parameter.
    /// </summary>
    /// <param name="password">String to be encrypted.</param>
    /// <param name="raw">Whether or not to produce a raw string.</param>
    /// <returns>String</returns>
    private string sMD5(string password, bool raw)
    {
        MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
        if (raw) return Encoding.ASCII.GetString(md5.ComputeHash(Encoding.ASCII.GetBytes(password)));
        else return BitConverter.ToString(md5.ComputeHash(Encoding.ASCII.GetBytes(password))).Replace("-", "");
    }
  }
   */
}
