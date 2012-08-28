using System;
using System.IO;
using System.Threading;
using mybox;

namespace mybox
{
  /// <summary>
  /// Manual tester since NUnit output was anoying me
  /// </summary>
  class ManualTester {
      
    String file1client;
    String file2client;
    String dir1client;
    String file3client;
    String file1server;

    public void Setup() {
      
      Tester.Setup();
      
      file1client = Tester.accountA.Directory + "file1";
      file2client = Tester.accountA.Directory + "file2";
      dir1client = Tester.accountA.Directory + "dir1";
      file3client = dir1client + Path.DirectorySeparatorChar + "file3";
      
      file1server = Tester.baseServerUserDir + "file1";
      
    }
    
    public bool SleepAndTestClientToServer() {
      Thread.Sleep(3000);
      return Tester.AreDirectoriesEqual(Tester.accountA.Directory, Tester.baseServerUserDir);
    }
    
    public ManualTester() {
    
      Setup();
      
      File.AppendAllText(file1server, "abc");
    
      Tester.StartProcesses();
    
//      Directory.CreateDirectory(dir1client);
      File.AppendAllText(file3client, "abc");
      //File.AppendAllText(file1client, "abc");
      
      if (SleepAndTestClientToServer())
        Console.WriteLine("                 ===== TEST PASSED ====");
      else
        Console.WriteLine("                 ===== TEST FAILED ====");
    }
    
    public static void Main (string[] args) {
      new ManualTester();
    }
  }
}
