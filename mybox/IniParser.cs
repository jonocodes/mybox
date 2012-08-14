﻿/**
    Mybox
    https://github.com/jonocodes/mybox
 
    Copyright (C) 2012  Jono Finger (jono@foodnotblogs.com)

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
using System.Collections;


// TODO: make a single loading parser

namespace mybox {

  /// <summary>
  /// Class for parsing INI files
  /// </summary>
  public class IniParser {

    private Hashtable keyPairs = new Hashtable();
    private String iniFilePath;

    private struct SectionPair {
      public String Section;
      public String Key;
    }

    /// <summary>
    /// Opens the INI file at the given path and enumerates the values in the IniParser.
    /// </summary>
    /// <param name="iniPath">Full path to INI file.</param>
    public IniParser(String iniPath) {
      TextReader iniFile = null;
      String strLine = null;
      String currentRoot = null;
      String[] keyPair = null;

      iniFilePath = iniPath;

      if (File.Exists(iniPath)) {
        try {
          iniFile = new StreamReader(iniPath);

          strLine = iniFile.ReadLine();

          while (strLine != null) {
            strLine = strLine.Trim();//.ToUpper();

            if (strLine != "") {
              if (strLine.StartsWith("[") && strLine.EndsWith("]")) {
                currentRoot = strLine.Substring(1, strLine.Length - 2);
              }
              else {
                keyPair = strLine.Split(new char[] { '=' }, 2);

                SectionPair sectionPair;
                String value = null;

                if (currentRoot == null)
                  currentRoot = "ROOT";

                sectionPair.Section = currentRoot;
                sectionPair.Key = keyPair[0];

                if (keyPair.Length > 1)
                  value = keyPair[1];

                keyPairs.Add(sectionPair, value);
              }
            }

            strLine = iniFile.ReadLine();
          }

        }
        catch (Exception ex) {
          throw ex;
        }
        finally {
          if (iniFile != null)
            iniFile.Close();
        }
      }
      else
        throw new FileNotFoundException("Unable to locate " + iniPath);

    }

    /// <summary>
    /// Returns the value for the given section, key pair.
    /// </summary>
    /// <param name="sectionName">Section name.</param>
    /// <param name="settingName">Key name.</param>
    public String GetSetting(String sectionName, String settingName) {
      SectionPair sectionPair;
      sectionPair.Section = sectionName;//.ToUpper();
      sectionPair.Key = settingName;//.ToUpper();

      return (String)keyPairs[sectionPair];
    }

    /// <summary>
    /// Enumerates all lines for given section.
    /// </summary>
    /// <param name="sectionName">Section to enum.</param>
    public String[] EnumSection(String sectionName) {
      ArrayList tmpArray = new ArrayList();

      foreach (SectionPair pair in keyPairs.Keys) {
        if (pair.Section == sectionName)//.ToUpper())
          tmpArray.Add(pair.Key);
      }

      return (String[])tmpArray.ToArray(typeof(String));
    }

    /// <summary>
    /// Adds or replaces a setting to the table to be saved.
    /// </summary>
    /// <param name="sectionName">Section to add under.</param>
    /// <param name="settingName">Key name to add.</param>
    /// <param name="settingValue">Value of key.</param>
    public void AddSetting(String sectionName, String settingName, String settingValue) {
      SectionPair sectionPair;
      sectionPair.Section = sectionName;//.ToUpper();
      sectionPair.Key = settingName;//.ToUpper();

      if (keyPairs.ContainsKey(sectionPair))
        keyPairs.Remove(sectionPair);

      keyPairs.Add(sectionPair, settingValue);
    }

    /// <summary>
    /// Adds or replaces a setting to the table to be saved with a null value.
    /// </summary>
    /// <param name="sectionName">Section to add under.</param>
    /// <param name="settingName">Key name to add.</param>
    public void AddSetting(String sectionName, String settingName) {
      AddSetting(sectionName, settingName, null);
    }

    /// <summary>
    /// Remove a setting.
    /// </summary>
    /// <param name="sectionName">Section to add under.</param>
    /// <param name="settingName">Key name to add.</param>
    public void DeleteSetting(String sectionName, String settingName) {
      SectionPair sectionPair;
      sectionPair.Section = sectionName;//.ToUpper();
      sectionPair.Key = settingName;//.ToUpper();

      if (keyPairs.ContainsKey(sectionPair))
        keyPairs.Remove(sectionPair);
    }

    /// <summary>
    /// Save settings to new file.
    /// </summary>
    /// <param name="newFilePath">New file path.</param>
    public void SaveSettings(String newFilePath) {
      ArrayList sections = new ArrayList();
      String tmpValue = "";
      String strToSave = "";

      foreach (SectionPair sectionPair in keyPairs.Keys) {
        if (!sections.Contains(sectionPair.Section))
          sections.Add(sectionPair.Section);
      }

      foreach (String section in sections) {
        strToSave += ("[" + section + "]\r\n");

        foreach (SectionPair sectionPair in keyPairs.Keys) {
          if (sectionPair.Section == section) {
            tmpValue = (String)keyPairs[sectionPair];

            if (tmpValue != null)
              tmpValue = "=" + tmpValue;

            strToSave += (sectionPair.Key + tmpValue + "\r\n");
          }
        }

        strToSave += "\r\n";
      }

      try {
        TextWriter tw = new StreamWriter(newFilePath);
        tw.Write(strToSave);
        tw.Close();
      }
      catch (Exception ex) {
        throw ex;
      }
    }

    /// <summary>
    /// Save settings back to ini file.
    /// </summary>
    public void SaveSettings() {
      SaveSettings(iniFilePath);
    }
  }


}
