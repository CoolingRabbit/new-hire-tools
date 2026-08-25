// Dev harness (ASCII only comments): hosts ShareListView with fake entries
// so the drive-badge / drag-float / retreat animation can be visually tested
// without a real NAS. Compile together with the main source:
//   csc -target:winexe -main:NewHireTools.Tests.ListDemoProgram ^
//       -out:tests\ListDemo.exe tests\ListDemo.cs src\NewHireToolbox.cs src\PasswordGenerator.cs ...

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NewHireTools.Tests
{
    internal static class ListDemoProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new DemoForm());
        }
    }

    internal class DemoForm : Form
    {
        public DemoForm()
        {
            Text = "ShareListView Demo";
            StartPosition = FormStartPosition.Manual;
            Location = new Point(60, 60);
            ClientSize = new Size(460, 260);
            BackColor = Theme.CardBg;

            ShareListView list = new ShareListView();
            list.Bounds = new Rectangle(16, 16, 428, 228);
            List<ShareEntry> entries = new List<ShareEntry>();
            entries.Add(new ShareEntry { Name = "Finance",   Tag = "CORP1-NAS", Unc = "\\\\192.168.100.10\\Finance" });
            entries.Add(new ShareEntry { Name = "HR Admin",  Tag = "CORP1-NAS", Unc = "\\\\192.168.100.10\\HR Admin" });
            entries.Add(new ShareEntry { Name = "SG-NAS",    Tag = "CORP1-NAS", Unc = "\\\\192.168.100.10\\SG-NAS" });
            entries.Add(new ShareEntry { Name = "RA Share",  Tag = "CORP2-NAS",   Unc = "\\\\192.168.200.22\\RA Share" });
            entries.Add(new ShareEntry { Name = "Public",    Tag = "CORP2-NAS",   Unc = "\\\\192.168.200.22\\Public" });
            entries.Add(new ShareEntry { Name = "Archive",   Tag = "CORP2-NAS",   Unc = "\\\\192.168.200.22\\Archive" });
            list.SetEntries(entries);
            this.Controls.Add(list);
        }
    }
}
