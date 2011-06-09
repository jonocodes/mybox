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
using System.IO;
using System.Security.Permissions;
using System.Threading;

namespace mybox {

  /// <summary>
  /// Class for watching a directory for updates
  /// </summary>
  public class DirectoryWatcher {

    private String dir;
    private ClientServerConnection client;
    private FileSystemWatcher watcher = new FileSystemWatcher();

    public DirectoryWatcher(String _dir, ClientServerConnection _client) {
      client = _client;
      dir = _dir;

      start();
      //      listen();
    }

    /// <summary>
    /// Enable the event raiser for the update listener
    /// </summary>
    public void Listen() {
      watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Disable the event raiser for the update listener
    /// </summary>
    public void Pause() {
      watcher.EnableRaisingEvents = false;
    }

    /// <summary>
    /// Setup the notifiers and listener events
    /// </summary>
    [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
    private void start() {

      // Create a new FileSystemWatcher and set its properties.
      //      FileSystemWatcher watcher = new FileSystemWatcher ();
      watcher.Path = dir;
      watcher.IncludeSubdirectories = true; // recursive watch

      /* Watch for changes in LastAccess and LastWrite times, and
           the renaming of files or directories. */
      watcher.NotifyFilter = NotifyFilters.LastWrite
           | NotifyFilters.FileName | NotifyFilters.DirectoryName;

      watcher.Filter = "";

      // Add event handlers.
      watcher.Changed += new FileSystemEventHandler(onChanged);
      watcher.Created += new FileSystemEventHandler(onChanged);
      watcher.Deleted += new FileSystemEventHandler(onChanged);
//      watcher.Renamed += new RenamedEventHandler(onRenamed);

      // Begin watching.
      watcher.EnableRaisingEvents = true;

      Thread listenerThread = new Thread(run);
      listenerThread.Start();
    }

    /// <summary>
    /// Dummy loop for the listener thread
    /// </summary>
    private static void run() {
      while (true)
        Thread.Sleep(10000);
    }

    // Define the event handlers.

    /// <summary>
    /// Handler for directory or file change update
    /// </summary>
    /// <param name="source"></param>
    /// <param name="e"></param>
    private void onChanged(object source, FileSystemEventArgs e) {
      // Specify what is done when a file is changed, created, or deleted.
      client.DirectoryUpdate(e.ChangeType.ToString(), e.FullPath);
      //      Console.WriteLine ("File: " + e.FullPath + " " + e.ChangeType);
    }

  }
}
