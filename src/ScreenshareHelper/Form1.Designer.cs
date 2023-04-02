
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
            buttonSetCaptureArea = new System.Windows.Forms.Button();
            buttonCloseApp = new System.Windows.Forms.Button();
            button1 = new System.Windows.Forms.Button();
            SuspendLayout();
            // 
            // buttonSetCaptureArea
            // 
            buttonSetCaptureArea.Location = new System.Drawing.Point(12, 12);
            buttonSetCaptureArea.Name = "buttonSetCaptureArea";
            buttonSetCaptureArea.Size = new System.Drawing.Size(150, 83);
            buttonSetCaptureArea.TabIndex = 1;
            buttonSetCaptureArea.Text = "Set";
            buttonSetCaptureArea.UseVisualStyleBackColor = true;
            buttonSetCaptureArea.Click += buttonSetCaptureArea_Click;
            // 
            // buttonCloseApp
            // 
            buttonCloseApp.Location = new System.Drawing.Point(653, 12);
            buttonCloseApp.Name = "buttonCloseApp";
            buttonCloseApp.Size = new System.Drawing.Size(135, 83);
            buttonCloseApp.TabIndex = 1;
            buttonCloseApp.Text = "Close App";
            buttonCloseApp.UseVisualStyleBackColor = true;
            buttonCloseApp.Click += buttonCloseApp_Click;
            // 
            // button1
            // 
            button1.Location = new System.Drawing.Point(168, 12);
            button1.Name = "button1";
            button1.Size = new System.Drawing.Size(135, 83);
            button1.TabIndex = 1;
            button1.Text = "Minimize";
            button1.UseVisualStyleBackColor = true;
            button1.Click += buttonMinimizeApp_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(800, 450);
            Controls.Add(button1);
            Controls.Add(buttonCloseApp);
            Controls.Add(buttonSetCaptureArea);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            Text = "ScreensharingHelper";
            FormClosing += Form1_FormClosing;
            ResumeLayout(false);
        }

        #endregion
        private System.Windows.Forms.Button buttonSetCaptureArea;
        private System.Windows.Forms.Button buttonCloseApp;
        private System.Windows.Forms.Button button1;
    }
}

