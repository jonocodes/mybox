using System;
using System.IO;
using System.Threading;
using System.ComponentModel;
using Gtk;
using Gdk;


public partial class PreferencesWindow : Gtk.Window {

  private static StatusIcon trayIcon;

  private Pixbuf iconBlank;
  private Pixbuf iconError;
  private Pixbuf iconWorking;
  private Pixbuf iconReady;

  private mybox.ClientServerConnection clientConnection;

  private ImageMenuItem menuItemPause = new ImageMenuItem("Pause");

  private Thread workerThread;

  private static Gdk.Pixbuf ImageToPixbuf(System.Drawing.Image image) {
    using (MemoryStream stream = new MemoryStream()) {
      image.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
      stream.Position = 0;
      Gdk.Pixbuf pixbuf = new Gdk.Pixbuf(stream);
      return pixbuf;
    }
  }


//  private delegate void setStatusHandler(mybox.ClientStatus status);

  private void setStatus(mybox.ClientStatus status) {
    if (status == mybox.ClientStatus.ERROR)
      trayIcon.Pixbuf = iconError;
    else if (status == mybox.ClientStatus.SYNCING || status == mybox.ClientStatus.CONNECTING)
      trayIcon.Pixbuf = iconWorking;
    else if (status == mybox.ClientStatus.READY)
      trayIcon.Pixbuf = iconReady;
    else
      trayIcon.Pixbuf = iconBlank;
  }

  /// <summary>
  /// this delegate is used for the BeginInvoke to allow for thread safe updating of the GUI
  /// </summary>
  /// <param name="message"></param>
  private delegate void writeMessageHandler(String message);

  /// <summary>
  /// this will handle logging the message to a file
  /// </summary>
  /// <param name="message"></param>
  private void logToFile(String message) {
    File.AppendAllText(clientConnection.LogFile, DateTime.Now + " : " + message + Environment.NewLine);
  }

  /// <summary>
  /// This will be hooked into the event handler in the MyWorker class and will make sure
  /// that the message is logged to the GUI
  /// </summary>
  /// <param name="message"></param>
  private void logToTextView(String message) {
    try {

      //textviewMessages.Buffer.InsertAtCursor(message);
      textviewMessages.Buffer.InsertAtCursor(DateTime.Now + " : " + message + Environment.NewLine);
      
    } catch (Exception e) {
      e = e;//
    }
  }

  public PreferencesWindow()
    : base(Gtk.WindowType.Toplevel) {

    Build();

    // preload the status iconset
    iconBlank = ImageToPixbuf(ClientGtk.Properties.Resources.box_blank);
    iconError = ImageToPixbuf(ClientGtk.Properties.Resources.box_red);
    iconWorking = ImageToPixbuf(ClientGtk.Properties.Resources.box_blue);
    iconReady = ImageToPixbuf(ClientGtk.Properties.Resources.box_green);


    trayIcon = new StatusIcon(iconBlank);

    // Creation of the Icon
    trayIcon.Visible = true;

    // Show/Hide the window (even from the Panel/Taskbar) when the TrayIcon has been clicked.
    //trayIcon.Activate += delegate { window.Visible = !window.Visible; };
    // Show a pop up menu when the icon has been right clicked.
    trayIcon.PopupMenu += OnTrayIconPopup;

    // A Tooltip for the Icon
    trayIcon.Tooltip = "Mybox";


//    textviewMessages.Buffer.InsertAtCursor("Ahoy!");

    TextIter insertIter;
    insertIter = textviewMessages.Buffer.GetIterAtMark(textviewMessages.Buffer.InsertMark);
//    textviewMessages.Buffer.Insert(insertIter, "Ahoy again!");

    // start the process
    workerThread = new Thread(new ThreadStart(doWork));
    workerThread.Start();
  }

  protected void OnDeleteEvent(object sender, DeleteEventArgs a) {
    Application.Quit();
    a.RetVal = true;
  }

  private void togglePause() {


    Menu popupMenu = new Menu();
//    popupMenu.Children[menuItemPause].la


    if (clientConnection.Paused) {  // unpause it
      clientConnection.Unpause();
      menuItemPause.Name = "Resume";
    } else {  // pause it
      clientConnection.Pause();
      menuItemPause.Name = "Pause";
    }
  }

  // Create the popup menu, on right click.
  void OnTrayIconPopup(object o, EventArgs args) {
    Menu popupMenu = new Menu();

    ImageMenuItem menuItemDir = new ImageMenuItem("Open Mybox folder");
    popupMenu.Add(menuItemDir);
    menuItemDir.Activated += delegate { System.Diagnostics.Process.Start(clientConnection.DataDir); };

    ImageMenuItem menuItemPrefs = new ImageMenuItem("Preferences");
    popupMenu.Add(menuItemPrefs);

//    ImageMenuItem menuItemPause = new ImageMenuItem("Pause");
    popupMenu.Add(menuItemPause);
    menuItemDir.Activated += delegate { togglePause(); };
    menuItemPause.Name = "Pause!!";

    ImageMenuItem menuItemQuit = new ImageMenuItem("Quit");
    Gtk.Image appimg = new Gtk.Image(Stock.Quit, IconSize.Menu);
    menuItemQuit.Image = appimg;
    popupMenu.Add(menuItemQuit);
    // Quit the application when quit has been clicked.
    menuItemQuit.Activated += delegate { Application.Quit(); };

    popupMenu.ShowAll();
    popupMenu.Popup();
  }

  private void doWork() {

    try {
    
      clientConnection = new mybox.ClientServerConnection(mybox.ClientServerConnection.DefaultConfigDir);
//      clientConnection.SetConfigDir(mybox.ClientServerConnection.DefaultConfigDir); // quits if it fails
//      clientConnection.LoadConfig(clientConnection.ConfigFile);
      
      clientConnection.LogHandlers.Add(new mybox.ClientServerConnection.LoggingHandlerDelegate(logToFile));
      clientConnection.LogHandlers.Add(new mybox.ClientServerConnection.LoggingHandlerDelegate(logToTextView));
      
      labelAccount.Text = "Account: " + clientConnection.Account.User;
      clientConnection.Start();
    } catch (Exception ec) {
      logToFile("Error: " + ec.Message);
      logToTextView("Error: " + ec.Message);
    }
  }

}
