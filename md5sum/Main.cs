using System;
using System.Diagnostics;
using System.Threading;
using System.IO;
using mybox;

namespace md5sum
{
  class MainClass {
    public static void Main (string[] args) {
      Console.WriteLine ("md5 speed test");
      
      foreach (String path in args) {
        if (File.Exists(path)) {
        
          FileInfo fi = new FileInfo(path);
          
          Console.WriteLine(path + " size: " + fi.Length);
        
          Stopwatch stopwatch = new Stopwatch();
          stopwatch.Start();
  
          String checksum = Common.FileChecksumToString(path);
          
          stopwatch.Stop();
          
          Console.WriteLine("checksum: " + checksum + " time: " + stopwatch.Elapsed);
          
        }
      }
        
    }
  }
}
