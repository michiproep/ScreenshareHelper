﻿
namespace ScreenshareHelper
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.buttonSetCaptureArea = new System.Windows.Forms.Button();
            this.buttonCloseApp = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // buttonSetCaptureArea
            // 
            this.buttonSetCaptureArea.Location = new System.Drawing.Point(12, 12);
            this.buttonSetCaptureArea.Name = "buttonSetCaptureArea";
            this.buttonSetCaptureArea.Size = new System.Drawing.Size(94, 29);
            this.buttonSetCaptureArea.TabIndex = 1;
            this.buttonSetCaptureArea.Text = "Set";
            this.buttonSetCaptureArea.UseVisualStyleBackColor = true;
            this.buttonSetCaptureArea.Click += new System.EventHandler(this.buttonSetCaptureArea_Click);
            // 
            // buttonCloseApp
            // 
            this.buttonCloseApp.Location = new System.Drawing.Point(112, 12);
            this.buttonCloseApp.Name = "buttonCloseApp";
            this.buttonCloseApp.Size = new System.Drawing.Size(94, 29);
            this.buttonCloseApp.TabIndex = 1;
            this.buttonCloseApp.Text = "Close App";
            this.buttonCloseApp.UseVisualStyleBackColor = true;
            this.buttonCloseApp.Click += new System.EventHandler(this.buttonCloseApp_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.buttonCloseApp);
            this.Controls.Add(this.buttonSetCaptureArea);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "ScreensharingHelper";
            this.Activated += new System.EventHandler(this.Form1_Activated);
            this.Deactivate += new System.EventHandler(this.Form1_Deactivate);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Button buttonSetCaptureArea;
        private System.Windows.Forms.Button buttonCloseApp;
    }
}

