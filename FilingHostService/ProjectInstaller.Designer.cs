namespace FilingHostService
{
    partial class ProjectInstaller
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
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

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.ESLProcessInstaller = new System.ServiceProcess.ServiceProcessInstaller();
            this.ESLInstaller = new System.ServiceProcess.ServiceInstaller();
            // 
            // ESLProcessInstaller
            // 
            this.ESLProcessInstaller.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
            this.ESLProcessInstaller.Password = null;
            this.ESLProcessInstaller.Username = null;
            // 
            // ESLInstaller
            // 
            this.ESLInstaller.Description = "eSeries OFS API Service for Odyssey review filing and aSync filing notifications";
            this.ESLInstaller.ServiceName = "ESL_OdysseyRfService";
            this.ESLInstaller.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
            this.ESLInstaller.AfterInstall += new System.Configuration.Install.InstallEventHandler(this.ESLInstaller_AfterInstall);
            // 
            // ProjectInstaller
            // 
            this.Installers.AddRange(new System.Configuration.Install.Installer[] {
            this.ESLProcessInstaller,
            this.ESLInstaller});

        }

        #endregion

        private System.ServiceProcess.ServiceProcessInstaller ESLProcessInstaller;
        private System.ServiceProcess.ServiceInstaller ESLInstaller;
    }
}