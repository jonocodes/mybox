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
    
    private List<ClientFile> localDeletes = new List<ClientFile>();
    private Dictionary<string, ClientFile> toDelete = new Dictionary<string, ClientFile>();
    //private Dictionary<string, ClientFile> toUpdate = new Dictionary<string, ClientFile>();
    
    private Dictionary<string, Dictionary<string, ClientFile>> toUpdateDir = new Dictionary<string, Dictionary<string, ClientFile>>();
    
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
      
      if (!toUpdateDir.ContainsKey(parentDir))
        toUpdateDir.Add(parentDir, new Dictionary<string, ClientFile>());
      
      toUpdateDir[parentDir].Add(file.Path, file);
      
      // TODO: check if file is already added
    }
    

    private bool scanDirectory(String relPath, bool subDirChanged = false) {
      
      writeMessage("scanDirectory: " + relPath);
      
      bool changed = subDirChanged; //false;
      
      Dictionary<String, ClientFile> childrenFiles = toMap(getFileList(relPath));
      Dictionary<String, ClientFile> childrenIndex = toMap(fileIndex.GetDirList(relPath));
      
      // recurse depth first into directories before processing files
      foreach (KeyValuePair<String, ClientFile> kvp in childrenFiles) {
        if (kvp.Value.Type == FileType.DIR) {
          bool subDir = false;
          
          if (!childrenIndex.ContainsKey(kvp.Key))
            subDir = true;
            
          if (scanDirectory(kvp.Key, subDir))
            changed = true;
          
        }
      }
      
      
      // remove files that no longer exist
      foreach (KeyValuePair<String, ClientFile> pair in childrenIndex) {
        ClientFile r = pair.Value;
        if (!childrenFiles.ContainsKey(r.Path)) {
          changed = true;
          writeMessage("detected change, file removed: " + r.Path);
          
          toDelete.Add(r.Path, r);

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
          //toUpdate.Add(f.Path, f);
          addToUpdate(relPath, f);
          
          changed = true;
        }
      }
      /*
      // if the local directory is new, then an update is in order
      if (!childrenIndex.ContainsKey(relPath)) {
        changed = true;
      }
  */
      if (childrenFiles.Count != childrenIndex.Count)
        changed = true;
        
  /*
      if (changed) {
        writeMessage("changed records found, new directory record: " + relPath);
        
        int dirTimestamp = Common.DateTimeToUnixTimestamp(new FileInfo(absDataDir + relPath).LastWriteTime);
        
        toUpdate.Add(relPath, fileIndex.GetUpdatedDirectory(relPath, dirTimestamp, childrenFiles, toUpdate, toDelete));
      }
  */
      return changed;
    }
    
    private List<ClientFile> getRemoteDirList(String relPath) {
    
      List<ClientFile> result = new List<ClientFile>();
    
      socket.Send(Common.SignalToBuffer(Signal.requestServerFileList));
      Common.SendString(socket, relPath);
      
      String jsonStringFiles = Common.ReceiveString(socket);

      List<List<string>> fileDict =
        ClientServerConnection.JsonSerializer.Deserialize<List<List<string>>>(jsonStringFiles);

      foreach(List<string> fileItem in fileDict) {
      
        // TODO: try, catch for parse errors etc
        result.Add(new ClientFile(fileItem[0].ToString(), (FileType)(char.Parse(fileItem[1])),
          long.Parse(fileItem[2].ToString()), fileItem[3].ToString(), 0));
      }

      return result;
    }
    
    public void Sync(bool catchupSync) {
      // scan the local filesystem for client side changes
      scanDirectory("/");

      // scan the remote filesystem for server side changes and sync with client side changes
      walk("/");
      
      // leave deletes until last in case there are some bytes we can use
      processLocalDeletes();

      if (catchupSync)
        socket.Send(Common.SignalToBuffer(Signal.syncCatchupFinished));
      else
        socket.Send(Common.SignalToBuffer(Signal.syncFinished));

#if DEBUG
      //int notUptodate = fileIndex.CheckUptodate();
      
      //if (notUptodate > 0) {
//        throw new Exception("Error: Sync fininshed with " + notUptodate + " files not UPTODATE");
      //}
#endif

      OnFinished(EventArgs.Empty);
      
      writeMessage("Sync finished");
      
      toDelete.Clear();
//      toUpdate.Clear();
      toUpdateDir.Clear();
    }
/*
    private void walk(String relPath) {
      writeMessage("walk: " + relPath);
      List<ClientFile> remoteFiles = getRemoteDirList(relPath);
      List<ClientFile> localFiles = getFileList(relPath); //fileIndex.GetDirList(relPath);
      walk(relPath, remoteFiles, localFiles);
    }
    */
    
    private void walk(String relPath/*, List<ClientFile> remoteFiles, List<ClientFile> localFiles*/) {
      
      Dictionary<String, ClientFile> remoteMap = toMap(getRemoteDirList(relPath));
      //Dictionary<String, ClientFile> localMap = toMap(getFileList(relPath));
      
      Dictionary<String, ClientFile> indexMap = toMap(fileIndex.GetDirList(relPath));
      
      Dictionary<String, ClientFile> updateMap;
      
      if (toUpdateDir.ContainsKey(relPath))
        updateMap = toUpdateDir[relPath];
      else
        updateMap = new Dictionary<string, ClientFile>();
      
      
      writeMessage("walk: " + relPath + " remote: " + remoteMap.Count 
        + " updates: " + updateMap.Count + " index: " + indexMap.Count);
      
        
      foreach (KeyValuePair<string, ClientFile> kvp in remoteMap) {
        ClientFile remoteFile = kvp.Value;
        
        if (toDelete.ContainsKey(remoteFile.Path)) {
          writeMessage("file locally deleted: " + remoteFile.Path);
          onLocalDeletion(remoteFile);
        }
        
        else if (updateMap != null && updateMap.ContainsKey(remoteFile.Path)) {
        
          if (updateMap[remoteFile.Path].Checksum != remoteFile.Checksum) {
            writeMessage("checksums differ: " + remoteFile.Path);
            doDifferentChecksums(relPath, remoteFile, updateMap[remoteFile.Path]);
          } else if (updateMap.ContainsKey(remoteFile.Path)) {
            // new timestamp but no file change, so update the timestamp in the index
            fileIndex.Update(updateMap[remoteFile.Path]);
          }
          
        }
        
        else if (indexMap.ContainsKey(remoteFile.Path)) {
        
          if (indexMap[remoteFile.Path].Checksum != remoteFile.Checksum) {
            writeMessage("checksums differ: " + remoteFile.Path);
            doDifferentChecksums(relPath, remoteFile, indexMap[remoteFile.Path]);
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
          doMissingRemote(relPath, kvp.Value);
        }
      }
      
      foreach (KeyValuePair<string, ClientFile> kvp in indexMap) 
        if (!remoteMap.ContainsKey(kvp.Key) && !missingRemote.ContainsKey(kvp.Key))
          doMissingRemote(relPath, kvp.Value);
          //missingRemote.Add(kvp.Key, kvp.Value);
      
      
      // update the entry for this directory in the index
      
      if (toUpdateDir.ContainsKey(relPath)) {
      
        int dirTimestamp = Common.DateTimeToUnixTimestamp(new FileInfo(absDataDir + relPath).LastWriteTime);
        
        // TODO: make sure it is not scheduled for deletion
        fileIndex.UpdateDirectoryEntry(relPath, dirTimestamp);
        
        // mark the parent for updating as well
        if (relPath != "/")
          toUpdateDir.Add(relPath.Substring(0, relPath.LastIndexOf("/")+1), new Dictionary<string, ClientFile>());
      }
      
      writeMessage("walk finished: " + relPath);
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
    private void doDifferentChecksums(string parentDir, ClientFile remoteFile, ClientFile localFile) {        
      if (remoteFile.Type == FileType.DIR && localFile.Type == FileType.DIR) {
        // both are directories, so continue. Since we have the directory checksums we can lookup files on that instead of path
        walk(localFile.Path);
        
      } else if (remoteFile.Type != FileType.DIR && localFile.Type != FileType.DIR) {
      
        if (toUpdateDir.ContainsKey(parentDir) && toUpdateDir[parentDir].ContainsKey(localFile.Path)) {
        
          ClientFile indexedFile = fileIndex.GetFile(localFile.Path);

          if (indexedFile != null && indexedFile.Checksum != remoteFile.Checksum) 
            onFileConflict(remoteFile, localFile);
          else // local changed, upload file to server
            onLocalChange(toUpdateDir[parentDir][localFile.Path]);
          
        } else {// remote changed, download file from server
          onRemoteChange(parentDir, remoteFile);
        }

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
    private void doMissingRemote(string parentDir, ClientFile localFile) {
    
      if (toUpdateDir.ContainsKey(parentDir) && toUpdateDir[parentDir].ContainsKey(localFile.Path)) {
        onLocalChange(toUpdateDir[parentDir][localFile.Path]);  // if resource is a directory this should create it            
        if( localFile.Type == FileType.DIR ) {  // continue scan
          walk(localFile.Path/*, new List<ClientFile>(), getFileList(localFile.Path)*/ /*fileIndex.GetDirList(localFile.Path)*/);
        }
      }
      else {
        // it was previously synced, but now gone. So must have been deleted remotely            
        // So we want to "delete" the local resource. But its possible this is half
        // of a move operation, so instead of immediately deleting we will defer it
        writeMessage("Queueing local deletion: " + localFile.Path + " because remote file is missing and there is a local sync record");
        localDeletes.Add(localFile);
      }
    
    }

    private void processLocalDeletes() {
      foreach (ClientFile del in localDeletes ) 
        onRemoteDelete(del);
      
      localDeletes.Clear();
    }
    
    private void onLocalDeletion(ClientFile remoteFile) {
      
      writeMessage("Delete from server for locally deleted item: " + remoteFile.Path);
      socket.Send(Common.SignalToBuffer(Signal.deleteOnServer));
      Common.SendString(socket, remoteFile.Path);

      if (Common.CheckSuccess(socket)) // check return before updating local index
        fileIndex.Remove(remoteFile.Path, remoteFile.Type);
    }
    
    private void onRemoteDelete(ClientFile localFile) {
      writeMessage("Deleting local file: " + localFile.Path);        
      
      String absPath = absDataDir + localFile.Path;

      bool deleteWorked = true;

      try {
        if (Directory.Exists(absPath))
          Directory.Delete(absPath);
        else
          File.Delete(absPath);
      }
      catch (Exception e) {
        writeMessage("Error deleting: " + localFile.Path + " " + e.Message);
        deleteWorked = false;
      }

      if (deleteWorked)
        fileIndex.Remove(localFile.Path, localFile.Type);
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
  
      // TODO: propogate these changes to the directory hashes
      
      String absLocalFilePath = absDataDir + remoteFile.Path;
      
      if (remoteFile.Type == FileType.DIR) {
        
        if (!File.Exists(absLocalFilePath)) {
        
          bool createdDirectory = true;
          try {
            Directory.CreateDirectory(absLocalFilePath);
                       
            // TODO: is this line in the original algorithm?
            walk(remoteFile.Path /*, getRemoteDirList(remoteFile.Path), new List<ClientFile>()*/);
            
          } catch (Exception e) {
            writeMessage("Error creating directory: " + absLocalFilePath + " " + e.Message);
            createdDirectory = false;
          }
          
          
//          if (createdDirectory)
//            fileIndex.Update(new ClientFile(remoteFile.Path, FileType.DIR, 0, "empty", Common.DateTimeToUnixTimestamp(DateTime.UtcNow)));
//            toUpdate.Add(remoteFile.Path, remoteFile);
          
          // TODO: figure out how to update index recursively?  or does it already handle it?
          
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

