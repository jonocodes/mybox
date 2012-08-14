namespace mybox {
  partial class PreferencesForm {
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing) {
      if (disposing && (components != null)) {
        components.Dispose();
      }
      base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent() {
      this.tabControl = new System.Windows.Forms.TabControl();
      this.tabAccount = new System.Windows.Forms.TabPage();
      this.labelAccount = new System.Windows.Forms.Label();
      this.tabMessages = new System.Windows.Forms.TabPage();
      this.richTextBoxMessages = new System.Windows.Forms.RichTextBox();
      this.buttonOK = new System.Windows.Forms.Button();
      this.backgroundWorker = new System.ComponentModel.BackgroundWorker();
      this.tabControl.SuspendLayout();
      this.tabAccount.SuspendLayout();
      this.tabMessages.SuspendLayout();
      this.SuspendLayout();
      // 
      // tabControl
      // 
      this.tabControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                  | System.Windows.Forms.AnchorStyles.Left)
                  | System.Windows.Forms.AnchorStyles.Right)));
      this.tabControl.Controls.Add(this.tabAccount);
      this.tabControl.Controls.Add(this.tabMessages);
      this.tabControl.Location = new System.Drawing.Point(12, 12);
      this.tabControl.Name = "tabControl";
      this.tabControl.SelectedIndex = 0;
      this.tabControl.Size = new System.Drawing.Size(516, 305);
      this.tabControl.TabIndex = 0;
      // 
      // tabAccount
      // 
      this.tabAccount.Controls.Add(this.labelAccount);
      this.tabAccount.Location = new System.Drawing.Point(4, 22);
      this.tabAccount.Name = "tabAccount";
      this.tabAccount.Padding = new System.Windows.Forms.Padding(3);
      this.tabAccount.Size = new System.Drawing.Size(508, 279);
      this.tabAccount.TabIndex = 0;
      this.tabAccount.Text = "Account";
      this.tabAccount.UseVisualStyleBackColor = true;
      // 
      // labelAccount
      // 
      this.labelAccount.AutoSize = true;
      this.labelAccount.Location = new System.Drawing.Point(47, 46);
      this.labelAccount.Name = "labelAccount";
      this.labelAccount.Size = new System.Drawing.Size(50, 13);
      this.labelAccount.TabIndex = 0;
      this.labelAccount.Text = "Account:";
      // 
      // tabMessages
      // 
      this.tabMessages.Controls.Add(this.richTextBoxMessages);
      this.tabMessages.Location = new System.Drawing.Point(4, 22);
      this.tabMessages.Name = "tabMessages";
      this.tabMessages.Padding = new System.Windows.Forms.Padding(3);
      this.tabMessages.Size = new System.Drawing.Size(508, 279);
      this.tabMessages.TabIndex = 1;
      this.tabMessages.Text = "Messages";
      this.tabMessages.UseVisualStyleBackColor = true;
      // 
      // richTextBoxMessages
      // 
      this.richTextBoxMessages.Dock = System.Windows.Forms.DockStyle.Fill;
      this.richTextBoxMessages.Location = new System.Drawing.Point(3, 3);
      this.richTextBoxMessages.Name = "richTextBoxMessages";
      this.richTextBoxMessages.Size = new System.Drawing.Size(502, 273);
      this.richTextBoxMessages.TabIndex = 0;
      this.richTextBoxMessages.Text = "";
      // 
      // buttonOK
      // 
      this.buttonOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
      this.buttonOK.Location = new System.Drawing.Point(424, 323);
      this.buttonOK.Name = "buttonOK";
      this.buttonOK.Size = new System.Drawing.Size(104, 25);
      this.buttonOK.TabIndex = 1;
      this.buttonOK.Text = "OK";
      this.buttonOK.UseVisualStyleBackColor = true;
      this.buttonOK.Click += new System.EventHandler(this.buttonOK_Click);
      // 
      // backgroundWorker
      // 
      this.backgroundWorker.DoWork += new System.ComponentModel.DoWorkEventHandler(this.backgroundWorker_DoWork);

      // 
      // PreferencesForm
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(540, 360);
      this.Controls.Add(this.buttonOK);
      this.Controls.Add(this.tabControl);
      this.Name = "PreferencesForm";
      this.Text = "Mybox Preferences";
      this.tabControl.ResumeLayout(false);
      this.tabAccount.ResumeLayout(false);
      this.tabAccount.PerformLayout();
      this.tabMessages.ResumeLayout(false);
      this.ResumeLayout(false);

    }

    #endregion

    private System.Windows.Forms.TabControl tabControl;
    private System.Windows.Forms.TabPage tabAccount;
    private System.Windows.Forms.TabPage tabMessages;
    private System.Windows.Forms.Label labelAccount;
    private System.Windows.Forms.RichTextBox richTextBoxMessages;
    private System.Windows.Forms.Button buttonOK;
    private System.ComponentModel.BackgroundWorker backgroundWorker;
  }
}

