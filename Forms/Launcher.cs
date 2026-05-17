using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Rbx2Source
{
    public partial class Launcher : Form
    {
        public Launcher()
        {
            InitializeComponent();
        }

        public void setStatus(string status)
        {
            statusLbl.Text = status + "...";
            statusLbl.Refresh();
        }

        private async void Launcher_Load(object sender, EventArgs e)
        {
            string myPath = Application.ExecutablePath;
            FileInfo myInfo = new FileInfo(myPath);

            string myName = myInfo.Name;
            string dir = myInfo.DirectoryName;

            if (myName.StartsWith("NEW_", StringComparison.InvariantCulture))
            {
                string newPath = Path.Combine(dir, myName.Substring(4));
                File.Copy(myInfo.FullName, newPath, true);

                Process.Start(newPath);
                Application.Exit();
            }
            else
            {
                foreach (string filePath in Directory.GetFiles(dir))
                {
                    FileInfo info = new FileInfo(filePath);
                    string fileName = info.Name;

                    if (fileName.StartsWith("NEW_", StringComparison.InvariantCulture) && info.Extension.ToUpperInvariant() == ".EXE")
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            try
                            {
                                File.Delete(info.FullName);
                                break;
                            }
                            catch
                            {
                                await Task.Delay(100);
                            }
                        }

                        break;
                    }
                }
            }

            setStatus("Starting Rbx2Source");
            await Task.Delay(500);

            Rbx2Source rbx2Source = null;

            Task startRbx2Source = Task.Run(() =>
            {
                rbx2Source = new Rbx2Source();
                rbx2Source.baseProcess = this;
            });

            await startRbx2Source;

            rbx2Source.Show();
            Hide();
        }
    }
}
