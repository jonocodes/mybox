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
    
    String clientFile;
    String clientDir;
    String serverDir;
    String serverFile;

    public void Setup() {
      
      Tester.Setup();
      
      clientDir = Tester.accountA.Directory + "clientDir";
      clientFile = clientDir + "/clientFile";
      serverDir = Tester.accountA.Directory + "serverDir";
      serverFile = serverDir + "/serverFile";

    }
    
    public bool SleepAndTestClientToServer() {
      Thread.Sleep(3000);
      return Tester.AreDirectoriesEqual(Tester.accountA.Directory, Tester.baseServerUserDir);
    }
    
    public ManualTester() {
    
      Setup();
      
      Directory.CreateDirectory(serverDir);
      File.AppendAllText(serverFile, "abc");
    
      Tester.StartProcesses();
      
      Directory.CreateDirectory(clientDir);
      File.AppendAllText(clientFile, "asdasdas");

      if (SleepAndTestClientToServer())
        Console.WriteLine("                 ===== TEST PASSED ====");
      else
        Console.WriteLine("                 ===== TEST FAILED ====");
        
      ClientFile topA = Tester.clientIndexA.GetFile("/");
      ClientFile topB = Tester.clientIndexB.GetFile("/");
      
      Console.WriteLine("Comparing top checksums = {0} vs {1}", topA.Checksum, topB.Checksum);
    }
    
    public static void Main (string[] args) {
      new ManualTester();
    }
  }
}
