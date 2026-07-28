
using Aveva.Core.PMLNet;
using ViewForm;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace VIewModelNew
{
    [PMLNetCallable]
    public class VIewModelNew
    {
        [PMLNetCallable]
        public VIewModelNew()
        {
        }
        [PMLNetCallable]
        public void Assign(VIewModelNew that)
        {
        }
        [PMLNetCallable]
        public void MainShowWpf()
        {

            var window = new MainWindow();
            ElementHost.EnableModelessKeyboardInterop(window);
            window.Show();
        }
    }


}
