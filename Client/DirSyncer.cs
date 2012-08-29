using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace mybox
{
  /// <summary>
  /// Class for handling syncing directories over a socket.
  /// </summary>
  public class DirSyncer {
  
    public delegate void SyncFinishedHandler(object sender, EventArgs e);
    
    public event SyncFinishedHandler Finished;
  
    private Socket socket = null;
    private String absDataDir = null;
    private FileIndex fileIndex = null;
    
    private HashSet<string> toDelete = new HashSet<string>();    
    private Dictionary<string, Dictionary<string, ClientFile>> toUpdateFiles = new Dictionary<string, Dictionary<string, ClientFile>>();
    private HashSet<string> dirsToUpdate = new HashSet<string>();
    
    protected virtual void OnFinished(EventArgs e) {
      if (Finished != null)
        Finished(this, e);
    }
  
    private static void writeMessage(String message) {
      Console.WriteLine(message);
    /*
      // TODO: pass up delegate to ClientServerConnection
      foreach (LoggingHandlerDelegate handler in ClientServerConnection.LogHandlers) {
        handler(message);
      }
      */
    }
    
  
    public DirSyncer(String absDataDir, FileIndex fileIndex, Socket socket) {
      this.socket = socket;
      this.fileIndex = fileIndex;
      this.absDataDir = absDataDir;
    }

    /// <summary>
    /// Get a list of files non-recursively from the filesystem.
    /// </summary>
    /// <returns>
    /// The file list.
    /// </returns>
    /// <param name='relPath'>
    /// Rel path.
    /// </param>
    private List<ClientFile> getFileList(string relPath) {

      List<ClientFile> result = new List<ClientFile>();

      String[] children = Directory.GetFileSystemEntries(absDataDir + relPath);

      foreach (String absChildPath in children) {
        
        string relChildPath = absChildPath.Replace(absDataDir, string.Empty);

        relChildPath = System.Text.RegularExpressions.Regex.Replace(relChildPath, @"[\\/]+", "/");
          
        FileInfo fi = new FileInfo(absChildPath);
        int modtime = Common.DateTimeToUnixTimestamp(fi.LastWriteTimeUtc);
        
        FileType type;
        long size = 0;
        
        if (Directory.Exists(absChildPath)) {
          type = FileType.DIR;
        } else {
          type = FileType.FILE;
          size = fi.Length;
        }
        
        // set a bogus checksum. this will be set elsewhere
        
        result.Add(new ClientFile(relChildPath, type, size, "empty", modtime));
      }
      return result;
    }
    
    private Dictionary<String, ClientFile> toMap(List<ClientFile> fileList) {
      Dictionary<String, ClientFile> result = new Dictionary<string, ClientFile>();
      
      foreach (ClientFile file in fileList)
        result.Add(file.Path, file);
        
      return result;
    }
    
    private void addToUpdate(String parentDir, ClientFile file) {
      
      if (!toUpdateFiles.ContainsKey(parentDir))
        toUpdateFiles.Add(parentDir, new Dictionary<string, ClientFile>());
      
      toUpdateFiles[parentDir].Add(file.Path, file);
      
      // TODO: check if file is already added
    }
    

    private bool gatherLocalUpdates(String relpath, bool subDirChanged = false) {
      
      writeMessage("gatherLocalUpdates: " + relpath);
      
      bool changed = subDirChanged; //false;
      
      Dictionary<String, ClientFile> childrenFiles = toMap(getFileList(relpath));
      Dictionary<String, ClientFile> childrenIndex = toMap(fileIndex.GetDirList(relpath));
      
      // recurse depth first into directories before processing files
      foreach (KeyValuePair<String, ClientFile> kvp in childrenFiles) {
        if (kvp.Value.Type == FileType.DIR) {
          bool subDir = false;
          
          if (!childrenIndex.ContainsKey(kvp.Key))
            subDir = true;
            
          if (gatherLocalUpdates(kvp.Key, subDir))
            changed = true;
        }
      }
      
      
      // remove files that no longer exist
      foreach (KeyValuePair<String, ClientFile> pair in childrenIndex) {
        ClientFile r = pair.Value;
        if (!childrenFiles.ContainsKey(r.Path)) {
          changed = true;
          writeMessage("detected change, file removed: " + r.Path);
          
          toDelete.Add(r.Path);

          // else?
        }
      }

      // mark files that need to be updated
      foreach (KeyValuePair<String, ClientFile> pair in childrenFiles) {
        
        ClientFile f = pair.Value;
        
        if (f.Type == FileType.FILE &&
           (!childrenIndex.ContainsKey(f.Path) || f.Modtime != childrenIndex[f.Path].Modtime )) {
        
          writeMessage("detected file update. either new or modified dates differ: " + f.Path);
          
          string absFilePath = absDataDir + f.Path;
            
          f.Checksum = Common.FileChecksumToString(absFilePath);
          f.Size = (new FileInfo(absFilePath)).Length;
          addToUpdate(relpath, f);
          
          changed = true;
        }
      }
      
      // TODO: do we need this anymore?
      if (childrenFiles.Count != childrenIndex.Count)
        changed = true;
      
      if (changed) {
        writeMessage("Adding to changed directories list: " + relpath);
        dirsToUpdate.Add(relpath);
        
        if (relpath != "/") {
          string parent = relpath.Substring(0, relpath.LastIndexOf("/")+1);
          addToUpdate(parent, new ClientFile(relpath, FileType.DIR, 0, "empty", 0));
        }

      }

      return changed;
    }
    
    private List<ClientFile> getRemoteDirList(String relPath) {
    
      List<ClientFile> result = new List<ClientFile>();
    
      socket.Send(Common.SignalToBuffer(Signal.requestServerFileList));
      Common.SendString(socket, relPath);
      
      String jsonStringFiles = Common.ReceiveString(socket);

      Console.WriteLine("DirSyncer.getRemoteDirList got: " + jsonStringFiles);

//      try {
        List<List<string>> fileDict = ClientServerConnection.JsonSerializer.Deserialize<List<List<string>>>(jsonStringFiles);
  
        foreach(List<string> fileItem in fileDict) {
          result.Add(new ClientFile(fileItem[0].ToString(), (FileType)(char.Parse(fileItem[1])),
            long.Parse(fileItem[2].ToString()), fileItem[3].ToString(), 0));
        }
//      } catch (InvalidOperationException e) {
//        // toss this because it probably means the input is an empty list
//        writeMessage(e.Message);
//      }
      
      return result;
    }
    
    public void Sync(bool catchupSync) {
      // scan the local filesystem for client side changes
      gatherLocalUpdates("/");

      ClientFile originalLocalRoot = fileIndex.GetFile("/");

      // TODO: if there are no local updates fetch remote checksum for '/' before calling syncwithRemote

      // TODO: collect local renames here and push them to server

      // scan the remote filesystem for server side changes and sync with client side changes
      syncWithRemote("/");
      
      ClientFile finalLocalRoot = fileIndex.GetFile("/");
      
      if (catchupSync || (finalLocalRoot.Checksum == originalLocalRoot.Checksum && finalLocalRoot.Size == originalLocalRoot.Size))
        socket.Send(Common.SignalToBuffer(Signal.syncFinishedDoNotSpan));
      else
        socket.Send(Common.SignalToBuffer(Signal.syncFinished));

      OnFinished(EventArgs.Empty);
      
      writeMessage("Sync finished");
      
      toDelete.Clear();
      toUpdateFiles.Clear();
      dirsToUpdate.Clear();
    }

    /// <summary>
    /// Recursively walk the index and regard the local changes and the remote changes.
    ///   Update the fileindex accordingly as well as directory checksums.
    /// </summary>
    /// <param name='relPath'>
    /// Rel path.
    /// </param>
    private void syncWithRemote(String relPath) {
      
      Dictionary<String, ClientFile> remoteMap = toMap(getRemoteDirList(relPath));
      Dictionary<String, ClientFile> indexMap = toMap(fileIndex.GetDirList(relPath));
      
      Dictionary<String, ClientFile> updateMap;
      
      if (toUpdateFiles.ContainsKey(relPath))
        updateMap = toUpdateFiles[relPath];
      else
        updateMap = new Dictionary<string, ClientFile>();
      
      
      writeMessage("syncWithRemote: " + relPath + " remote: " + remoteMap.Count 
        + " updates: " + updateMap.Count + " index: " + indexMap.Count);
      
      
      foreach (KeyValuePair<string, ClientFile> kvp in remoteMap) {
        ClientFile remoteFile = kvp.Value;
        
        if (remoteFile.Type == FileType.DIR && dirsToUpdate.Contains(remoteFile.Path))
          syncWithRemote(remoteFile.Path);
          
        if (toDelete.Contains(remoteFile.Path)) {
          writeMessage("file locally deleted: " + remoteFile.Path);
          onLocalDeletion(remoteFile);
        }
        
        else if (updateMap.ContainsKey(remoteFile.Path)) {
        
          if (updateMap[remoteFile.Path].Checksum != remoteFile.Checksum) {
            writeMessage("checksums differ: " + remoteFile.Path);
            
            string previousChecksum = string.Empty;
            if (indexMap.ContainsKey(remoteFile.Path))
              previousChecksum = indexMap[remoteFile.Path].Checksum;
            
            doDifferentChecksums(relPath, remoteFile, updateMap[remoteFile.Path], previousChecksum);
          } else if (updateMap.ContainsKey(remoteFile.Path)) {
            // new timestamp but no file change, so update the timestamp in the index
            fileIndex.Update(updateMap[remoteFile.Path]);
          }
          
        }
        
        else if (indexMap.ContainsKey(remoteFile.Path)) {
        
          if (indexMap[remoteFile.Path].Checksum != remoteFile.Checksum) {
            writeMessage("checksums differ: " + remoteFile.Path);
            doDifferentChecksums(relPath, remoteFile, indexMap[remoteFile.Path], indexMap[remoteFile.Path].Checksum);
          }
          
        }
        else {
          writeMessage("new remote file detected: " + remoteFile.Path);
          onRemoteChange(relPath, remoteFile);
        }
        
      }
      
      // Now look for local resources which do not match (by name) remote resources
      
      Dictionary<string, ClientFile> missingRemote = new Dictionary<string, ClientFile>();
      
      foreach (KeyValuePair<string, ClientFile> kvp in updateMap) {
        if (!remoteMap.ContainsKey(kvp.Key)) {
          missingRemote.Add(kvp.Key, kvp.Value);
          doMissingRemote(relPath, kvp.Value, true);
        }
      }
      
      foreach (KeyValuePair<string, ClientFile> kvp in indexMap) 
        if (!remoteMap.ContainsKey(kvp.Key) && !missingRemote.ContainsKey(kvp.Key))
          doMissingRemote(relPath, kvp.Value, false);

      
      // update the entry for this directory in the index
      
      if (dirsToUpdate.Contains(relPath)) {
      
        int dirTimestamp = Common.DateTimeToUnixTimestamp(new FileInfo(absDataDir + relPath).LastWriteTime);
        
        // TODO: make sure it is not scheduled for deletion
        fileIndex.UpdateDirectoryEntry(relPath, dirTimestamp);
        
        markParentForUpdate(relPath);
      }
      
      writeMessage("syncWithRemote finished: " + relPath);
    }
    
    private void markParentForUpdate(string childFile) {
    
      if (childFile != "/") {
        string parent = childFile.Substring(0, childFile.LastIndexOf("/")+1);
        if (!dirsToUpdate.Contains(parent))
          dirsToUpdate.Add(parent);
      }          
    }
    
    /**
     * Called when there are local and remote resources with the same path, but
     * with different hashes
     *
     * Possibilities:
     * 
     * both are directories: so just continue the scan
     * 
     * both are files
     * 
     *      remote modified, local unchanged = downSync
     * 
     *      remote unchanged, local modified = upSync
     * 
     *      both changed = file conflict
     * 
     *      one is a file, the other a directory = tree conflict
     *
     * @param remoteTriplet
     * @param localTriplet
     * @param path
     */
    private void doDifferentChecksums(string parentDir, ClientFile remoteFile, ClientFile localFile, String previousChecksum) {        
      if (remoteFile.Type == FileType.DIR && localFile.Type == FileType.DIR) {
        // both are directories, so continue. Since we have the directory checksums we can lookup files on that instead of path
        syncWithRemote(localFile.Path);
        
      } else if (remoteFile.Type != FileType.DIR && localFile.Type != FileType.DIR) {
      
        if (previousChecksum != string.Empty && previousChecksum != remoteFile.Checksum)
          onFileConflict(remoteFile, localFile);
        else if (previousChecksum == localFile.Checksum)// remote changed, download file from server
          onRemoteChange(parentDir, remoteFile);
        else // local changed, upload file to server
          onLocalChange(localFile);        
    
      } else {
        onTreeConflict(remoteFile, localFile);
      }
    }


    /**
     * Called when there is a local resource with no matching (by name)
     * remote resource
     * 
     * Possibilities:
     *  - the resource has been added locally
     *      - if the resource is a directory we continue scan
     *      - if a file we upSync it
     *  - the resource has been remotely deleted
     * 
     * @param localTriplet
     * @param childPath 
     */
    private void doMissingRemote(string parentDir, ClientFile localFile, bool localUpdate) {
    
      if (localUpdate) {
        onLocalChange(localFile);  // if resource is a directory this should create it            
        if( localFile.Type == FileType.DIR ) {  // continue scan
          syncWithRemote(localFile.Path);
        }
      }
      else {
        onRemoteDelete(localFile);
      }
    
    }

    private void onLocalDeletion(ClientFile remoteFile) {
      
      writeMessage("Delete from server for locally deleted item: " + remoteFile.Path);
      socket.Send(Common.SignalToBuffer(Signal.deleteOnServer));
      Common.SendString(socket, remoteFile.Path);

      if (Common.CheckSuccess(socket)) // check return before updating local index
        fileIndex.Remove(remoteFile.Path, remoteFile.Type);
    }
    
    private void onRemoteDelete(ClientFile localFile) {
      writeMessage("Deleting local: " + localFile.Path);
      
      String absPath = absDataDir + localFile.Path;

      try {
        if (Directory.Exists(absPath)) {
          Directory.Delete(absPath, true);
          markParentForUpdate(localFile.Path);
        } else {
          File.Delete(absPath);
        }
        fileIndex.Remove(localFile.Path, localFile.Type);
      }
      catch (Exception e) {
        writeMessage("Error deleting: " + localFile.Path + " " + e.Message);
      }

    }
    
    /// <summary>
    /// Downloads file from the server
    /// </summary>
    /// <param name='remoteFile'>
    /// Remote file.
    /// </param>
    private void downloadFile(string parentDir, ClientFile remoteFile) {
    
      socket.Send(Common.SignalToBuffer(Signal.clientWants));
      Common.SendString(socket, remoteFile.Path);
        
      MyFile newFile = Common.ReceiveFile(socket, absDataDir);
      
      if (newFile != null) {
      
        ClientFile clientFile = new ClientFile(newFile.Path, newFile.Type, newFile.Size,
          newFile.Checksum, Common.GetModTime(absDataDir + newFile.Path));
      
        fileIndex.Update(clientFile);
        addToUpdate(parentDir, clientFile);
      }
    }

    /// <summary>
    /// Uploads  file to the server
    /// </summary>
    /// <param name='localFile'>
    /// Local file.
    /// </param>
    private void uploadFile(ClientFile localUpdatedFile) {
      socket.Send(Common.SignalToBuffer(Signal.c2s));
      if (Common.SendFile(localUpdatedFile.Path, socket, absDataDir))
        fileIndex.Update(localUpdatedFile);
    }
    
    private void onRemoteChange(string parentDir, ClientFile remoteFile) {
  
      String absLocalFilePath = absDataDir + remoteFile.Path;
      
      if (remoteFile.Type == FileType.DIR) {
        
        if (!File.Exists(absLocalFilePath)) {
        
          bool createdDirectory = true;
          try {
            Directory.CreateDirectory(absLocalFilePath);
                       
            // TODO: is this line in the original algorithm?
            
          } catch (Exception e) {
            writeMessage("Error creating directory: " + absLocalFilePath + " " + e.Message);
            createdDirectory = false;
          }
          
          if (createdDirectory) {
            dirsToUpdate.Add(remoteFile.Path);
            syncWithRemote(remoteFile.Path);
          }          
        }
        else 
          writeMessage("Local directory already exists: " + remoteFile.Path);
        
      } else {

        if (File.Exists(absLocalFilePath))
            writeMessage("modified remote file: " + remoteFile.Path);
        else
            writeMessage("new remote file: " + remoteFile.Path);

        downloadFile(parentDir, remoteFile);
      }
    }
    
    private void onLocalChange(ClientFile localUpdatedFile) {
    
      String absLocalFilePath = absDataDir + localUpdatedFile.Path;

      if (File.Exists(absLocalFilePath)) {
        writeMessage("upload locally new or modified file: " + localUpdatedFile.Path);
        uploadFile(localUpdatedFile);
      } else {
      
        // TODO: will this ever happen? do we need to add it to dirsToUpdate?
      
        writeMessage("create remote directory for locally new directory: " + localUpdatedFile.Path);
        
        socket.Send(Common.SignalToBuffer(Signal.createDirectoryOnServer));
        
        // TODO: update remote checksum
        Common.SendString(socket, localUpdatedFile.Path);
        if (Common.CheckSuccess(socket))
          fileIndex.Update(localUpdatedFile);

        // note that creating a remote directory does not ensure it is in sync               
      }
    }

    private void onTreeConflict(ClientFile remoteFile, ClientFile localFile) {
      writeMessage("!! Tree conflict at "+ remoteFile.Path);
    }    
    
    private void onFileConflict(ClientFile remoteFile, ClientFile localFile) {
      writeMessage("!! File conflict at "+ remoteFile.Path);
    }
    
  }
}
