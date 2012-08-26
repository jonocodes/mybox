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
    private Dictionary<string, ClientFile> toUpdate = new Dictionary<string, ClientFile>();

    
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

      foreach (String absChildPath in children) 
        result.Add(ClientFile.FromFileSystem(absDataDir, getRelativePath(absChildPath)));
      
      return result;
    }
    

    private String getRelativePath(String absPath) {

      string rel = absPath.Replace(absDataDir, string.Empty);

      rel = System.Text.RegularExpressions.Regex.Replace(rel, @"[\\/]+", "/");

      return rel;
    }
    
    private Dictionary<String, ClientFile> toMap(List<ClientFile> fileList) {
      Dictionary<String, ClientFile> result = new Dictionary<string, ClientFile>();
      
      foreach (ClientFile file in fileList)
        result.Add(file.Path, file);
        
      return result;
    }
    

    private bool scanDirectory(String absDirPath) {
    
      writeMessage("scanDirectory: " + absDirPath);
      
      String[] children = Directory.GetFileSystemEntries(absDirPath);
/*
      if (children.Length == 0) {
        writeMessage("No children of: " + absDirPath);
        return false;
      }
                  */
      bool changed = false;
      foreach (String child in children) {
        if (Directory.Exists(child)) {
          if (scanDirectory(child)) {
            changed = true;
          }
        }
      }
      if (scanChildren(absDirPath)) {
        changed = true;
      }
  
      if (changed) {
        writeMessage("changed records found, refresh directory record: " + absDirPath);
        string relPath = getRelativePath(absDirPath);
        
//        fileStatus.Add(relPath, FileSyncStatus.SENDTOSERVER);
        
        int dirTimestamp = Common.DateTimeToUnixTimestamp(new FileInfo(absDirPath).LastWriteTime);
        
        Dictionary<String, ClientFile> mapOfFiles = toMap(getFileList(relPath));
        toUpdate.Add(relPath, fileIndex.GetDirUpdateValues(relPath, dirTimestamp, mapOfFiles, toUpdate, toDelete));
        
        // TODO: updateInfo.Add(relPath, new KeyValuePair<string, long>(
      }
  
      return changed;
    }
    
    private bool scanChildren(String absParent) {
      
      writeMessage("scanChildren: " + absParent);
      
      String relParent = getRelativePath(absParent);
      
      Dictionary<String, ClientFile> mapOfFiles = toMap(getFileList(relParent));

      bool changed = false;

      Dictionary<String, ClientFile> records = toMap(fileIndex.GetDirList(relParent));

      // remove any that no longer exist
      foreach (KeyValuePair<String, ClientFile> pair in records) {
        ClientFile r = pair.Value;
        if (!mapOfFiles.ContainsKey(r.Path)) {
          changed = true;
          writeMessage("detected change, file removed: " + r.Path);
          
          toDelete.Add(r.Path, r);

          // else?
        }
      }

      foreach (KeyValuePair<String, ClientFile> pair in mapOfFiles) {
        
        ClientFile f = pair.Value;
        
        if (f.Type == FileType.FILE) {
        
          string absFilePath = absDataDir + f.Path;
            
          if (!records.ContainsKey(f.Path)) {
          
            writeMessage("detected change, new file: " + f.Path);
            
            changed = true;
            
            f.Checksum = Common.FileChecksumToString(absFilePath);;
            f.Size = (new FileInfo(absFilePath)).Length;
            toUpdate.Add(f.Path, f);
            
          }
          else if (f.Modtime != records[f.Path].Modtime) {
            writeMessage("detected change, file modified dates differ: " + f.Path);
            
            changed = true;

            f.Checksum = Common.FileChecksumToString(absFilePath);
            f.Size = (new FileInfo(absFilePath)).Length;;
            toUpdate.Add(f.Path, f);
          }
        } else { // directory
        
        
        
        
          // TODO: is this correct? not in original algorithm
          
          if (!records.ContainsKey(f.Path)) {
          
            writeMessage("detected change, new directory: " + f.Path);
            
            changed = true;
            
//            fileIndex.Update(f, syncStatus); // will update checksum

            //ClientFile newDir = fileIndex.GetDirUpdateValues(f.Path, f.Modtime, fileStatus, mapOfFiles, toUpdate);

            toUpdate.Add(f.Path, f);
      
      
            // will this change propogate checksums up the chain?
            

          }
          // handle 'else' case?
          
          
          
          
          
        
        }
      }

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
    
    public void Sync() {
      // update index with status codes in accordance to the filesystem
      scanDirectory(absDataDir + "/");  // TODO: shouldnt this only be done once at start up?

      walk("/");
      processLocalDeletes(); // we want to leave deletes until last in case there's some bytes we can use

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
      toUpdate.Clear();
    }

    private void walk(String relPath) {
      writeMessage("walk: " + relPath);
      List<ClientFile> remoteFiles = getRemoteDirList(relPath);
      List<ClientFile> localFiles = getFileList(relPath); //fileIndex.GetDirList(relPath);
      walk(relPath, remoteFiles, localFiles);
    }
    
    private void walk(String relPath, List<ClientFile> remoteFiles, List<ClientFile> localFiles) {
      writeMessage("walk: " + relPath + " remoteFiles: " + remoteFiles.Count + " localFiles: " + localFiles.Count);
      
      Dictionary<String, ClientFile> remoteMap = toMap(remoteFiles);
      Dictionary<String, ClientFile> localMap = toMap(localFiles);

      foreach (ClientFile remoteFile in remoteFiles) {
        
        if (toDelete.ContainsKey(remoteFile.Path)) {
        
          writeMessage("file locally deleted: " + remoteFile.Path);
          onLocalDeletion(remoteFile);
        }
        else if (localMap.ContainsKey(remoteFile.Path)) {
        
          if (toUpdate.ContainsKey(remoteFile.Path) && toUpdate[remoteFile.Path].Checksum != remoteFile.Checksum) {
            writeMessage("checksums differ: " + remoteFile.Path);
            ClientFile localFile = localMap[remoteFile.Path];
            doDifferentChecksums(remoteFile, localFile);
          } else if (toUpdate.ContainsKey(remoteFile.Path)) {
            // new timestamp but no file change, so update the timestamp in the index
            fileIndex.Update(toUpdate[remoteFile.Path]);
          }
        }
        else {
          writeMessage("new remote file detected: " + remoteFile.Path);
          onRemoteChange(remoteFile);
        }
        
      }
      
      
      // Now look for local resources which do not match (by name) remote resources
      
      foreach (ClientFile localFile in localFiles) {
        if( !remoteMap.ContainsKey(localFile.Path)) {
          doMissingRemote(localFile);
        }
      }
      
    
      // TODO: check to make sure updates worked before doing this
      
      if (toUpdate.ContainsKey(relPath)) {
        fileIndex.Update(toUpdate[relPath]);
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
    private void doDifferentChecksums(ClientFile remoteFile, ClientFile localFile) {        
      if (remoteFile.Type == FileType.DIR && localFile.Type == FileType.DIR) {
        // both are directories, so continue. Since we have the directory checksums we can lookup files on that instead of path
        walk(localFile.Path);
        
      } else if (remoteFile.Type != FileType.DIR && localFile.Type != FileType.DIR) {
      
        if (toUpdate.ContainsKey(localFile.Path)) {
        // local changed, upload file to server
      
          if (toUpdate[localFile.Path].Checksum != remoteFile.Checksum) { 
//            if (localFile.PreviousChecksum != remoteFile.Checksum) { // TODO: deal with this at some point
            onFileConflict(remoteFile, localFile);
          } else {
            onLocalChange(localFile);
          }
      
        } else {
          // remote changed, download file from server
          onRemoteChange(remoteFile);
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
    private void doMissingRemote(ClientFile localFile) {
    
      if (toUpdate.ContainsKey(localFile.Path)) {
        onLocalChange(localFile);  // if resource is a directory this should create it            
        if( localFile.Type == FileType.DIR ) {  // continue scan
          walk(localFile.Path, new List<ClientFile>(), fileIndex.GetDirList(localFile.Path));
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
      // TODO: check return before updating local index
      fileIndex.Remove(remoteFile.Path, remoteFile.Type);
    }
    
    private void onRemoteDelete(ClientFile localFile) {
      writeMessage("Deleting local file: " + localFile.Path);        
      
      String absPath = absDataDir + localFile.Path;
      
      if (Directory.Exists(absPath))
        Directory.Delete(absPath);
      else
        File.Delete(absPath);
      // TODO: check return value before updating the index
      
      fileIndex.Remove(localFile.Path, localFile.Type);
    }
    
    /// <summary>
    /// Downloads file from the server
    /// </summary>
    /// <param name='remoteFile'>
    /// Remote file.
    /// </param>
    private void downloadFile(ClientFile remoteFile) {
    
      socket.Send(Common.SignalToBuffer(Signal.clientWants));
      Common.SendString(socket, remoteFile.Path);
        
      MyFile newFile = Common.ReceiveFile(socket, absDataDir);
      
      if (newFile != null) {
      
        ClientFile clientFile = new ClientFile(newFile.Path, newFile.Type, newFile.Size,
          newFile.Checksum, Common.GetModTime(absDataDir + newFile.Path));
      
        fileIndex.Update(clientFile/*, fileStatus*/);
      }
    }

    /// <summary>
    /// Uploads  file to the server
    /// </summary>
    /// <param name='localFile'>
    /// Local file.
    /// </param>
    private void uploadFile(ClientFile localFile) {
      socket.Send(Common.SignalToBuffer(Signal.c2s));
      Common.SendFile(localFile.Path, socket, absDataDir);
      // TODO: ask server if it sucessfully recieved the checksumed file before returning it here

      fileIndex.Update(toUpdate[localFile.Path]);
    }
    
    private void onRemoteChange(ClientFile remoteFile) {
  
      String absLocalFilePath = absDataDir + remoteFile.Path;
      
      if (remoteFile.Type == FileType.DIR) {
        
        if (!File.Exists(absLocalFilePath)) {
          Directory.CreateDirectory(absLocalFilePath);
        
          
            
          // TODO: figure out how to update index recursively?  or does it already handle it?
          
              
        }
        else 
          writeMessage("Local directory already exists: " + remoteFile.Path);
        
      } else {

        if (File.Exists(absLocalFilePath))
            writeMessage("modified remote file: " + remoteFile.Path);
        else
            writeMessage("new remote file: " + remoteFile.Path);

        downloadFile(remoteFile);
      }
    }
    
    private void onLocalChange(ClientFile localFile) {
    
      String absLocalFilePath = absDataDir + localFile.Path;

      if (File.Exists(absLocalFilePath)) {
        writeMessage("upload locally new or modified file: " + localFile.Path);
        uploadFile(localFile);
        
        
      } else {
        writeMessage("create remote directory for locally new directory: " + localFile.Path);
        
        socket.Send(Common.SignalToBuffer(Signal.createDirectoryOnServer));
        
        // TODO: update remote checksum ?
        Common.SendString(socket, localFile.Path);
        fileIndex.Update(localFile);
        // TODO: wait for reply to know that it was created on server before updating index

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

