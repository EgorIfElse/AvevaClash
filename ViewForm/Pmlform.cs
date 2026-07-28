using Aveva.Core.PMLNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViewForm
{
    [PMLNetCallable]
    public class Pmlform
    {
        [PMLNetCallable]
        public Pmlform()
        {

        }
        [PMLNetCallable]
        public void Assign(Pmlform that)
        {
        }
        [PMLNetCallable]
        public void Start()
        {
            var window = new ViewForm.MainWindow();
            window.Show();
        }
    }
}
