using System.Drawing;

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
            this.buttonSetVirtualMonitorArea = new System.Windows.Forms.Button();
            this.buttonRemoveVirtualMonitor = new System.Windows.Forms.Button();
            this.comboVirtualMonitorResolution = new System.Windows.Forms.ComboBox();
            this.buttonAspect16x9 = new System.Windows.Forms.Button();
            this.labelSize = new System.Windows.Forms.Label();
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
            // buttonSetVirtualMonitorArea
            // 
            this.buttonSetVirtualMonitorArea.Location = new System.Drawing.Point(12, 47);
            this.buttonSetVirtualMonitorArea.Name = "buttonSetVirtualMonitorArea";
            this.buttonSetVirtualMonitorArea.Size = new System.Drawing.Size(194, 29);
            this.buttonSetVirtualMonitorArea.TabIndex = 3;
            this.buttonSetVirtualMonitorArea.Text = "Set for virtual monitor";
            this.buttonSetVirtualMonitorArea.UseVisualStyleBackColor = true;
            this.buttonSetVirtualMonitorArea.Click += new System.EventHandler(this.buttonSetVirtualMonitorArea_Click);
            // 
            // buttonRemoveVirtualMonitor
            // 
            this.buttonRemoveVirtualMonitor.Location = new System.Drawing.Point(414, 47);
            this.buttonRemoveVirtualMonitor.Name = "buttonRemoveVirtualMonitor";
            this.buttonRemoveVirtualMonitor.Size = new System.Drawing.Size(194, 29);
            this.buttonRemoveVirtualMonitor.TabIndex = 6;
            this.buttonRemoveVirtualMonitor.Text = "Remove virtual monitor";
            this.buttonRemoveVirtualMonitor.UseVisualStyleBackColor = true;
            this.buttonRemoveVirtualMonitor.Visible = false;
            this.buttonRemoveVirtualMonitor.Click += new System.EventHandler(this.buttonRemoveVirtualMonitor_Click);
            // 
            // comboVirtualMonitorResolution
            // 
            this.comboVirtualMonitorResolution.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboVirtualMonitorResolution.Location = new System.Drawing.Point(212, 48);
            this.comboVirtualMonitorResolution.Name = "comboVirtualMonitorResolution";
            this.comboVirtualMonitorResolution.Size = new System.Drawing.Size(130, 28);
            this.comboVirtualMonitorResolution.TabIndex = 4;
            this.comboVirtualMonitorResolution.SelectionChangeCommitted += new System.EventHandler(this.comboVirtualMonitorResolution_SelectionChangeCommitted);
            // 
            // buttonAspect16x9
            // 
            this.buttonAspect16x9.Location = new System.Drawing.Point(348, 47);
            this.buttonAspect16x9.Name = "buttonAspect16x9";
            this.buttonAspect16x9.Size = new System.Drawing.Size(60, 29);
            this.buttonAspect16x9.TabIndex = 5;
            this.buttonAspect16x9.Text = "16:9";
            this.buttonAspect16x9.UseVisualStyleBackColor = true;
            this.buttonAspect16x9.Click += new System.EventHandler(this.buttonAspect16x9_Click);
            // 
            // labelSize
            // 
            this.labelSize.BackColor = Color.Black;
            this.labelSize.ForeColor = Color.White;
            this.labelSize.AutoSize = true;
            this.labelSize.Location = new System.Drawing.Point(212, 9);
            this.labelSize.Name = "labelSize";
            this.labelSize.Size = new System.Drawing.Size(60, 20);
            this.labelSize.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point); 
            this.labelSize.TabIndex = 2;
            this.labelSize.Text = "0 × 0";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.buttonCloseApp);
            this.Controls.Add(this.buttonSetCaptureArea);
            this.Controls.Add(this.buttonSetVirtualMonitorArea);
            this.Controls.Add(this.buttonRemoveVirtualMonitor);
            this.Controls.Add(this.comboVirtualMonitorResolution);
            this.Controls.Add(this.buttonAspect16x9);
            this.Controls.Add(this.labelSize);
            
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "ScreensharingHelper";
            this.Activated += new System.EventHandler(this.Form1_Activated);
            this.Deactivate += new System.EventHandler(this.Form1_Deactivate);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Button buttonSetCaptureArea;
        private System.Windows.Forms.Button buttonCloseApp;
        private System.Windows.Forms.Button buttonSetVirtualMonitorArea;
        private System.Windows.Forms.Button buttonRemoveVirtualMonitor;
        private System.Windows.Forms.ComboBox comboVirtualMonitorResolution;
        private System.Windows.Forms.Button buttonAspect16x9;
        private System.Windows.Forms.Label labelSize;
    }
}

