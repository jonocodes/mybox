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
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace mybox {
  public partial class PreferencesForm : Form {

    private NotifyIcon trayIcon;
    private ContextMenu trayMenu;

    private Icon iconBlank;
    private Icon iconError;
    private Icon iconWorking;
    private Icon iconReady;

    private ClientServerConnection client;

    private delegate void setOverlayHandler(bool upToDate); // TODO: finish handling this

    private delegate void setStatusHandler (ClientStatus status);

    private void setStatus (ClientStatus status) {
      if (status == ClientStatus.ERROR)
        trayIcon.Icon = iconError;
      else if (status == ClientStatus.SYNCING || status == ClientStatus.CONNECTING)
        trayIcon.Icon = iconWorking;
      else if (status == ClientStatus.READY)
        trayIcon.Icon = iconReady;
      else
        trayIcon.Icon = iconBlank;
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
      File.AppendAllText(ClientServerConnection.LogFile, DateTime.Now + " : " + message + Environment.NewLine);
    }

    /// <summary>
    /// This will be hooked into the event handler in the MyWorker class and will make sure
    /// that the message is logged to the GUI
    /// </summary>
    /// <param name="message"></param>
    private void logToTextBoxThreadSafe(String message) {
      if (richTextBoxMessages.InvokeRequired) {
        writeMessageHandler d = new writeMessageHandler(logToTextBox);
        this.Invoke(d, new object[] { message });
      } else {
        logToTextBox(message);
      }
    }

    private void logToTextBox(String message) {
      richTextBoxMessages.AppendText(DateTime.Now + " : " + message + Environment.NewLine);
    }


    /// <summary>
    /// Converts an image into an icon.
    /// </summary>
    /// <param name="img">The image that shall become an icon</param>
    /// <param name="size">The width and height of the icon. Standard
    /// sizes are 16x16, 32x32, 48x48, 64x64.</param>
    /// <param name="keepAspectRatio">Whether the image should be squashed into a
    /// square or whether whitespace should be put around it.</param>
    /// <returns>An icon!!</returns>
    private Icon MakeIcon(Image img, int size, bool keepAspectRatio) {
      Bitmap square = new Bitmap(size, size); // create new bitmap
      Graphics g = Graphics.FromImage(square); // allow drawing to it

      int x, y, w, h; // dimensions for new image

      if(!keepAspectRatio || img.Height == img.Width) {
        // just fill the square
        x = y = 0; // set x and y to 0
        w = h = size; // set width and height to size
      } else {
        // work out the aspect ratio
        float r = (float)img.Width / (float)img.Height;

        // set dimensions accordingly to fit inside size^2 square
        if(r > 1) { // w is bigger, so divide h by r
          w = size;
          h = (int)((float)size / r);
          x = 0; y = (size - h) / 2; // center the image
        } else { // h is bigger, so multiply w by r
          w = (int)((float)size * r);
          h = size;
          y = 0; x = (size - w) / 2; // center the image
        }
      }

      // make the image shrink nicely by using HighQualityBicubic mode
      g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
      g.DrawImage(img, x, y, w, h); // draw image with specified dimensions
      g.Flush(); // make sure all drawing operations complete before we get the icon

      // following line would work directly on any image, but then
      // it wouldn't look as nice.
      return Icon.FromHandle(square.GetHicon());
    }


    private void startSysTrayItem () {

      trayMenu = new ContextMenu ();
      trayMenu.MenuItems.Add ("Open Mybox folder", openDirectory);
      trayMenu.MenuItems.Add ("Preferences", ShowPrefs);
      //trayMenu.MenuItems.Add("Connect", connect);
      trayMenu.MenuItems.Add ("Exit", OnExit);

      trayIcon = new NotifyIcon ();
      trayIcon.Text = "Mybox";

      trayIcon.Icon = iconBlank;

      // Add menu to tray icon and show it.
      trayIcon.ContextMenu = trayMenu;
      trayIcon.Visible = true;
      trayIcon.DoubleClick += new System.EventHandler (openDirectory);

      ClientServerConnection.StatusHandler = setStatus;
    }

    public PreferencesForm() {

      iconBlank = MakeIcon (ClientWindows.Properties.Resources.box_blank, 32, true);
      iconError = MakeIcon (ClientWindows.Properties.Resources.box_red, 32, true);
      iconWorking = MakeIcon (ClientWindows.Properties.Resources.box_blue, 32, true);
      iconReady = MakeIcon(ClientWindows.Properties.Resources.box_green, 32, true);

      InitializeComponent();

      // the system tray icon
      startSysTrayItem();

      // the form
//      this.ControlBox = false;
      tabControl.SelectTab(tabMessages);
      ShowDialog();

      // start the process
      backgroundWorker.RunWorkerAsync();
    }

    protected override void OnLoad(EventArgs e) {
      Visible = false; // Hide form window.
      ShowInTaskbar = false; // Remove from taskbar.

      base.OnLoad(e);
    }

    private void ShowPrefs(object sender, EventArgs e) {
      ShowDialog();
    }

    //private void connect(object sender, EventArgs e) {
    //  client.start();
    //}

    private void openDirectory(object sender, EventArgs e) {
      // TODO: make sure the conlig passed before opening a null directory
      System.Diagnostics.Process.Start(client.DataDir);
    }

    private void OnExit(object sender, EventArgs e) {
      Application.Exit();
    }

    private void buttonOK_Click(object sender, EventArgs e) {
      Hide();
    }

    

    private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e) {

      ClientServerConnection.LogHandlers.Add(new ClientServerConnection.LoggingHandlerDelegate(logToTextBoxThreadSafe));
      ClientServerConnection.LogHandlers.Add(new ClientServerConnection.LoggingHandlerDelegate(logToFile));

      try {
        ClientServerConnection.SetConfigDir(ClientServerConnection.DefaultConfigDir); // quits if it fails
        client = new ClientServerConnection();

        client.LoadConfig(ClientServerConnection.ConfigFile);
        labelAccount.Text = "Account: " + client.Account.email;
        client.start();
      } catch (Exception ec) {
        logToFile("Error: " + ec.Message);
        logToTextBoxThreadSafe("Error: " + ec.Message);
      }
    }


  }
}
