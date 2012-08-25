using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace mybox
{
  public class DirSyncer {
  
    public delegate void SyncFinishedHandler(object sender, EventArgs e);
    
    public event SyncFinishedHandler Finished;
  
    private Socket socket = null;
    private String absDataDir = null;
    private FileIndex fileIndex = null;
    
    
    private List<MyFile> localDeletes = new List<MyFile>();

    
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
    private List<MyFile> getFileList(string relPath) {

      List<MyFile> result = new List<MyFile>();

      String[] children = Directory.GetFileSystemEntries(absDataDir + relPath);

      foreach (String absChildPath in children) 
        result.Add(new MyFile(absDataDir, getRelativePath(absChildPath)));
      
      return result;
    }
    

    private String getRelativePath(String absPath) {

      string rel = absPath.Replace(absDataDir, string.Empty);

      rel = System.Text.RegularExpressions.Regex.Replace(rel, @"[\\/]+", "/");

      return rel;
    }
    
    private Dictionary<String, MyFile> toMap(List<MyFile> fileList) {
      Dictionary<String, MyFile> result = new Dictionary<string, MyFile>();
      
      foreach (MyFile file in fileList)
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
        fileIndex.Update(new MyFile(absDataDir, getRelativePath(absDirPath)));
      }
  
      return changed;
    }
    
    private bool scanChildren(String absParent) {
      
      writeMessage("scanChildren: " + absParent);
      
      String relParent = getRelativePath(absParent);
      
      Dictionary<String, MyFile> mapOfFiles = toMap(getFileList(relParent));

      bool changed = false;

      Dictionary<String, MyFile> records = toMap(fileIndex.GetDirList(relParent));

      // remove any that no longer exist
      foreach (KeyValuePair<String, MyFile> pair in records) {
        MyFile r = pair.Value;
        if (!mapOfFiles.ContainsKey(r.Path)) {
          changed = true;
          writeMessage("detected change, file removed: " + r.Path);
          
          r.SyncStatus = FileSyncStatus.DELETEONSERVER;
          fileIndex.Update(r);
        }
      }

      foreach (KeyValuePair<String, MyFile> pair in mapOfFiles) {
        
        MyFile f = pair.Value;
        
        if (f.Type == FileType.FILE) {
        
          if (!records.ContainsKey(f.Path)) {
          
            writeMessage("detected change, new file: " + f.Path);
            
            changed = true;
            f.UpdateFromFileSystem(absDataDir);
            fileIndex.Update(f);
            
          }
          else {
            MyFile r = records[f.Path];
          
            if (f.Modtime != r.Modtime) {
              writeMessage("detected change, file modified dates differ: " + f.Path);
              
              changed = true;
              f.UpdateFromFileSystem(absDataDir);
//              if (f.SyncStatus == FileSyncStatus.SENDTOSERVER)
                fileIndex.Update(f);
              
            } else {
              // file is up to date
            }
          }
        } else { // directory
          // TODO: is this correct?
          
          
          
          
          
          if (!records.ContainsKey(f.Path)) {
          
            writeMessage("detected change, new directory: " + f.Path);
            
            changed = true;
            f.UpdateFromFileSystem(absDataDir);
            fileIndex.Update(f); // will update checksum
          }
          // handle 'else' case?
          
          
          
          
          
        
        }
      }

      return changed;
    }
    
    private List<MyFile> getRemoteDirList(String relPath) {
    
      List<MyFile> result = new List<MyFile>();
    
      socket.Send(Common.SignalToBuffer(Signal.requestServerFileList));
      //sendCommandToServer(Signal.requestServerFileList);
      Common.SendString(socket, relPath);
      
      String jsonStringFiles = Common.ReceiveString(socket);

      List<List<string>> fileDict =
        ClientServerConnection.JsonSerializer.Deserialize<List<List<string>>>(jsonStringFiles);
//        JsonConvert.DeserializeObject<List<List<string>>>(jsonStringFiles);

      foreach(List<string> fileItem in fileDict) {
      
        // TODO: try, catch for parse errors etc
        result.Add(new MyFile(fileItem[0].ToString(), (FileType)(char.Parse(fileItem[1])),
          0, long.Parse(fileItem[2].ToString()), fileItem[3].ToString() ));
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
      int notUptodate = fileIndex.CheckUptodate();
      
      if (notUptodate > 0) {
//        throw new Exception("Error: Sync fininshed with " + notUptodate + " files not UPTODATE");
      }
#endif

      OnFinished(EventArgs.Empty);
      
      writeMessage("Sync finished");
    }

    private void walk(String relPath) {
      writeMessage("walk: " + relPath);
      List<MyFile> remoteFiles = getRemoteDirList(relPath);
      List<MyFile> localFiles = fileIndex.GetDirList(relPath);
      walk(relPath, remoteFiles, localFiles);
    }
    
    private void walk(String relPath, List<MyFile> remoteFiles, List<MyFile> localFiles) {
      writeMessage("walk: " + relPath + " remoteFiles: " + remoteFiles.Count + " localFiles: " + localFiles.Count);
      
      Dictionary<String, MyFile> remoteMap = toMap(remoteFiles);
      Dictionary<String, MyFile> localMap = toMap(localFiles);

      foreach (MyFile remoteFile in remoteFiles) {
        
        if (localMap.ContainsKey(remoteFile.Path)) {
        
          MyFile localFile = localMap[remoteFile.Path];
          
          if (localFile.SyncStatus == FileSyncStatus.DELETEONSERVER) {
            // delete remote
            writeMessage("MISSING LOCAL2: " + remoteFile.Path + "  was previously backed up, so locally deleted");
            onLocalDeletion(remoteFile);
          }
          else if (localFile.Checksum != remoteFile.Checksum ) {
            doDifferentChecksums(remoteFile, localFile);
          }
        
        }
        else {
      
          // create new local
      
          writeMessage("MISSING LOCAL1: " + remoteFile.Path + "  no local backup hash, so remotely new");
          // not previously synced, so is remotely new
          onRemoteChange(remoteFile);
        }
      }
      
      
      // Now look for local resources which do not match (by name) remote resources
      
      foreach (MyFile localFile in localFiles) {
          if( !remoteMap.ContainsKey(localFile.Path)) {
              //String childPath = relPath.child(localFile.Path);
              doMissingRemote(localFile);
          }
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
    private void doDifferentChecksums(MyFile remoteFile, MyFile localFile) {        
        if (remoteFile.Type == FileType.DIR && localFile.Type == FileType.DIR) {
          // both are directories, so continue. Since we have the directory checksums we can lookup files on that instead of path
          walk(localFile.Path);
        } else if (remoteFile.Type != FileType.DIR && localFile.Type != FileType.DIR) {
        
          if (localFile.SyncStatus == FileSyncStatus.SENDTOSERVER) {
            // local changed, upload file to server
            
//            if (localFile.PreviousChecksum != remoteFile.Checksum) { // TODO: deal with this at some point
//              onFileConflict(remoteFile, localFile);
//            } else {
              onLocalChange(localFile);
//            }
          
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
    private void doMissingRemote(MyFile localFile) {
    
      if (localFile.SyncStatus == FileSyncStatus.SENDTOSERVER) {
        onLocalChange(localFile);  // if resource is a directory this should create it            
        if( localFile.Type == FileType.DIR ) {  // continue scan
          walk(localFile.Path, new List<MyFile>(), fileIndex.GetDirList(localFile.Path));
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
      foreach (MyFile del in localDeletes ) 
        onRemoteDelete(del);
      
      localDeletes.Clear();
    }
    
    private void onLocalDeletion(MyFile remoteFile) {
      
      writeMessage("Delete from server for locally deleted item: " + remoteFile.Path);
      socket.Send(Common.SignalToBuffer(Signal.deleteOnServer));
      Common.SendString(socket, remoteFile.Path);
      // TODO: check return before updating local index
      fileIndex.Remove(remoteFile);
    }
    
    private void onRemoteDelete(MyFile localFile) {
      writeMessage("Deleting local file: " + localFile.Path);        
      
      String absPath = absDataDir + localFile.Path;
      
      if (Directory.Exists(absPath))
        Directory.Delete(absPath);
      else
        File.Delete(absPath);
      // TODO: check return value before updating the index
      
      fileIndex.Remove(localFile);
    }
    
    /// <summary>
    /// Downloads file from the server
    /// </summary>
    /// <param name='remoteFile'>
    /// Remote file.
    /// </param>
    private void downloadFile(MyFile remoteFile) {
    
      socket.Send(Common.SignalToBuffer(Signal.clientWants));
      Common.SendString(socket, remoteFile.Path);
        
      MyFile newFile = Common.ReceiveFile(socket, absDataDir);
      if (newFile != null) {
        newFile.SyncStatus = FileSyncStatus.UPTODATE;
        fileIndex.Update(newFile);
      }
    }

    /// <summary>
    /// Uploads  file to the server
    /// </summary>
    /// <param name='localFile'>
    /// Local file.
    /// </param>
    private void uploadFile(MyFile localFile) {
      socket.Send(Common.SignalToBuffer(Signal.c2s));
      MyFile outFile = Common.SendFile(localFile.Path, socket, absDataDir);
      // TODO: ask server if it sucessfully recieved the checksumed file before returning it here
      outFile.SyncStatus= FileSyncStatus.UPTODATE;
      fileIndex.Update(outFile);
    }
    
    
    private void onRemoteChange(MyFile remoteFile) {
  
      String absLocalFilePath = absDataDir + remoteFile.Path;
      
      if (remoteFile.Type == FileType.DIR) {
        
        if (!File.Exists(absLocalFilePath)) 
          Directory.CreateDirectory(absLocalFilePath);
        else 
          writeMessage("Local directory already exists: " + remoteFile.Path);
        
      } else {

        if( File.Exists(absLocalFilePath) )
            writeMessage("modified remote file: " + remoteFile.Path);
        else
            writeMessage("new remote file: " + remoteFile.Path);

        downloadFile(remoteFile);
      }
    }
    
    private void onLocalChange(MyFile localFile) {
    
      String absLocalFilePath = absDataDir + localFile.Path;

      if (File.Exists(absLocalFilePath)) {
        writeMessage("upload locally new or modified file: " + localFile.Path);
        uploadFile(localFile);
      } else {
        // TODO: make sure it is a directory
        writeMessage("create remote directory for locally new directory: " + localFile.Path);
        
        socket.Send(Common.SignalToBuffer(Signal.createDirectoryOnServer));
        
        // TODO: update remote checksum ?
        Common.SendString(socket, localFile.Path);
        localFile.SyncStatus = FileSyncStatus.UPTODATE;
        fileIndex.Update(localFile);
        // TODO: wait for reply to know that it was created on server before updating index

        // note that creating a remote directory does not ensure it is in sync               
      }
    }

    private void onTreeConflict(MyFile remoteFile, MyFile localFile) {
      writeMessage("!! Tree conflict at "+ remoteFile.Path);
    }    
    
    private void onFileConflict(MyFile remoteFile, MyFile localFile) {
      writeMessage("!! File conflict at "+ remoteFile.Path);
    }

    
  }
}

