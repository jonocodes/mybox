using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace mybox
{
  [TestFixture]
  public class SyncTest {
  
    String file1client;
    String file2client;
    String dir1client;
    String file3client;
    String file1server;

    [SetUp]
    public void Init() {
      
      Tester.Setup();
      
      file1client = Tester.accountA.Directory + "file1";
      file2client = Tester.accountA.Directory + "file2";
      dir1client = Tester.accountA.Directory + "dir1";
      file3client = dir1client + Path.DirectorySeparatorChar + "file3";
      
      file1server = Tester.baseServerUserDir + "file1";
      
    }
    
    [Test]
    public void aUpdateFile() {
    
      if (File.Exists(file1client))
        File.Delete(file1client);
        
      File.AppendAllText(file1client, "abc");
      File.AppendAllText(file1client, "def");
      
      Thread.Sleep(2500);
      Assert.IsTrue(File.ReadAllText(file1client) == "abcdef");
      Assert.IsTrue(File.ReadAllText(file1server) == "abcdef");
    }
  
    [Test]
    public void bAddFiles() {
    
      File.AppendAllText(file1client, "abc");
      
      File.AppendAllText(file1client, "def");
      
      File.AppendAllText(file2client, "xyz");
      Tester.SleepAndTestClientToServer();
      
      File.Delete(file1client);
      Tester.SleepAndTestClientToServer();
    }
    
    
    [Test]
    public void cAddDirectory() {
      
      Directory.CreateDirectory(dir1client);
      
      File.AppendAllText(file3client, "abc");
      
      Tester.SleepAndTestClientToServer();
    }
    /*
    [Test]
    public void dDeleteServerFile() {
    
      File.AppendAllText(file1client, "abc");
      File.AppendAllText(file2client, "def");
      
      FileIndex fileIndex = new FileIndex(Tester.)
      
    }
    */
  }
}

