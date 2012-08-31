using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Linq;
using NUnit.Framework;
using System.IO;
using System.Threading;
using MySql.Data.MySqlClient;
using DbConnection = System.Data.IDbConnection;
using DbReader = System.Data.IDataReader;
using DbCommand = System.Data.IDbCommand;
using mybox;

namespace mybox
{
  public static class Tester {
    
    public static String baseTestDir;
    public static String baseServerDataDir;
    public static String baseServerUserDir;
    public static String serverConfigDir;
    public static ClientAccount accountA;
    public static String clientConfigDirA;
    
    public static ClientAccount accountB;
    public static String clientConfigDirB;
    
    public static FileIndex clientIndexA;
    public static FileIndex clientIndexB;
    
    public static Thread serverThread;
    public static Thread clientThreadA;
    public static Thread clientThreadB;

    public static String serverDb;
    public static bool setupRun = false;
    
    public static bool TestClientsInSync() {
      
      ClientFile rootA = clientIndexA.GetFile("/");
      ClientFile rootB = clientIndexB.GetFile("/");
      
      String[] listA = Directory.GetFiles(accountA.Directory, "*", SearchOption.AllDirectories);
      String[] listB = Directory.GetFiles(accountB.Directory, "*", SearchOption.AllDirectories);
      
      Console.WriteLine("Comparing client directories. File count {0} vs {1}", listA.Length, listB.Length);
      Console.WriteLine("Comparing roots [{0}] vs [{1}]", rootA.Checksum, rootB.Checksum);
      
      if (!rootA.Equals(rootB)) {
        Console.WriteLine("Roots do not match");
        return false;
      }
      
      foreach (String fullPath in listA)
        Console.WriteLine(fullPath);
      
      if (listA.Length != listB.Length)
        return false;
        
      return true;
    }
    
    public static bool AreDirectoriesEqual(string dir1, string dir2) {
      
      Console.WriteLine("comparing " + dir1 + " and " + dir2);
      
      if (!Directory.Exists(dir1) || !Directory.Exists(dir2)) // fail if the dirs done exist
        return false;
      
      String[] list1 = Directory.GetFileSystemEntries(dir1, "*",  SearchOption.AllDirectories);
      String[] list2 = Directory.GetFileSystemEntries(dir2, "*",  SearchOption.AllDirectories);
      
      if (list1.Length != list2.Length) {
        Console.WriteLine("files: " + list1.Length + " vs " + list2.Length);
        return false;
      }

      Dictionary<string, string> dir1map = new Dictionary<string, string>();

      foreach (String fullPath in list1) {
        String relPath = fullPath.Replace(dir1, String.Empty);
        String checksum = "dir";
        
        if (File.Exists(fullPath))
          checksum = Common.FileChecksumToString(fullPath);
          
        dir1map.Add(relPath, checksum);
        Console.WriteLine("list1 -> {0} ({1})", relPath, checksum);
      }
      
      foreach (String fullPath in list2) {
        String relPath = fullPath.Replace(dir2, String.Empty);
        
        if (!dir1map.ContainsKey(relPath))
          return false;
        else {
          String checksum = "dir";
          
          if (File.Exists(fullPath))
            checksum = Common.FileChecksumToString(fullPath);
          
          if (dir1map[relPath] != checksum)
            return false;
            
          Console.WriteLine("list2 -> {0} ({1})", relPath, checksum);
        }
      }

      return true;
    }

    public static void SleepAndTestClientToServer() {
      Thread.Sleep (2500);
      Assert.IsTrue(AreDirectoriesEqual(accountA.Directory, baseServerUserDir));
    }
    
    public static void StartProcesses() {
      // start processes
      
      if (serverThread == null) {
        serverThread  = new Thread((ThreadStart)delegate {  new Server(serverConfigDir);  });
        serverThread.Start();
        Thread.Sleep(500); // wait for the server to start
      }
      
      if (clientThreadA == null) {
        clientThreadA = new Thread((ThreadStart)delegate {  new Client(clientConfigDirA);  });
        clientThreadA.Start();
        Thread.Sleep(700);
      }
      
      if (clientThreadB == null) {
        clientThreadB = new Thread((ThreadStart)delegate {  new Client(clientConfigDirB);  });
        clientThreadB.Start();
      }
      
    }
    
    public static void Setup() {
    
      if (setupRun)
        return;
        
/*
      if (serverThread != null && serverThread.IsAlive)
        serverThread.Abort();
        
      if (clientThreadA != null && clientThreadA.IsAlive)
        clientThreadA.Abort();
        
      */
      
      baseTestDir = Path.GetTempPath() + Path.DirectorySeparatorChar + "myboxTest" + Path.DirectorySeparatorChar;
      baseServerDataDir = baseTestDir + "serverData" + Path.DirectorySeparatorChar;
      baseServerUserDir = baseServerDataDir + "1" + Path.DirectorySeparatorChar;
      serverConfigDir = baseTestDir;
      
      int port = 4441;   // use a non-default port to avoid conflicts with a running server
      
      accountA = new ClientAccount();
      accountA.ServerName = "localhost";
      accountA.ServerPort = port;
      accountA.User = "test";
      accountA.Password = "badpassword";
      accountA.Directory = baseTestDir + "clientDataA" + Path.DirectorySeparatorChar;
      
      clientConfigDirA = baseTestDir + "clientConfigA" + Path.DirectorySeparatorChar;
      
      accountB = new ClientAccount();
      accountB.ServerName = accountA.ServerName;
      accountB.ServerPort = port;
      accountB.User = accountA.User;
      accountB.Password = accountA.Password;
      accountB.Directory = baseTestDir + "clientDataB" + Path.DirectorySeparatorChar;
      
      clientConfigDirB = baseTestDir + "clientConfigB" + Path.DirectorySeparatorChar;

      serverDb = "URI=file:" + baseServerDataDir + "server.db,version=3";
      
      // delete old directories and create new ones
      
      if (Directory.Exists(baseTestDir))
        Directory.Delete(baseTestDir, true);
      
      Directory.CreateDirectory(baseTestDir);
      Directory.CreateDirectory(baseServerDataDir);
      
      Directory.CreateDirectory(clientConfigDirA);
      Directory.CreateDirectory(accountA.Directory);
      
      Directory.CreateDirectory(clientConfigDirB);
      Directory.CreateDirectory(accountB.Directory);
      
      // server setup
      Server.WriteConfig(serverConfigDir, port, typeof(SqliteDB), serverDb, baseServerDataDir);
      
      // set up two client accounts
      ClientServerConnection.WriteConfig(accountA, clientConfigDirA);
      ClientServerConnection.WriteConfig(accountB, clientConfigDirB);

      clientIndexA = new FileIndex(clientConfigDirA + "client.db");
      clientIndexB = new FileIndex(clientConfigDirB + "client.db");
      
      // manually insert account into test database
      // needed for mysql but not sqlite
      /*
      using (DbConnection dbConnection = new MySqlConnection("Server=localhost;Uid=root;Pwd=root")){
        dbConnection.Open();
        using (DbCommand dbCommand = dbConnection.CreateCommand()){
          dbCommand.CommandText = "DROP DATABASE IF EXISTS myboxTest";
          dbCommand.ExecuteNonQuery();
        }
        using (DbCommand dbCommand = dbConnection.CreateCommand()){
          dbCommand.CommandText = "CREATE DATABASE myboxTest";
          dbCommand.ExecuteNonQuery();
        }
      }
      */
      // TODO: manually insert user with known ID here
      
      setupRun = true;
    }

  }
}
