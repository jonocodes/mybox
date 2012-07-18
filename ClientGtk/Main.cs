using System;
using Gtk;

namespace mybox
{
  class MainClass {
    public static void Main (string[] args) {
      Application.Init ();
      PreferencesWindow win = new PreferencesWindow();
      win.Show ();
      Application.Run ();
    }
  }
}

